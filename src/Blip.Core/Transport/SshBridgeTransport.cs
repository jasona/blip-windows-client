using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Blip.Core.Config;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Blip.Core.Transport;

/// <summary>
/// The bridge over one persistent SSH connection (SSH.NET), with many channels on it.
/// </summary>
/// <remarks>
/// <para>Windows OpenSSH has no ControlMaster, so shelling out to ssh.exe per call
/// would pay a full handshake every time. One SshClient held open does the same
/// job ControlMaster does for the Linux shim.</para>
/// <para>The key must be the dedicated one the Mac pins with
/// <c>restrict,command="$HOME/.blip/bin/blip-dispatch"</c>: the remote command is
/// the bare tool name plus POSIX-quoted args, which only blip-dispatch runs.
/// A general-purpose key would get a shell that does not know <c>imsg</c>, so
/// the bridge fails closed instead of running with a full shell.</para>
/// <para>The host key is pinned (trust on first use, then exact match).</para>
/// </remarks>
public sealed class SshBridgeTransport : IBridgeTransport
{
    public const int PoolSize = 2;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SpawnTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromSeconds(60);

    private readonly BridgeConfig _config;
    private readonly Func<string, bool> _trustNewHostKey;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _poolGate = new(1, 1);
    private readonly List<ServeChannel> _pool = [];
    private SshClient? _client;
    private string _pinned;
    private bool _disposed;

    /// <summary>Raised when a host key is trusted for the first time, so the caller can persist it.</summary>
    public event Action<string>? HostKeyPinned;

    /// <param name="trustNewHostKey">Asked once, with <c>SHA256:…</c>, when no key is pinned yet.
    /// Return false to refuse. Never consulted on a mismatch: that always fails.</param>
    public SshBridgeTransport(BridgeConfig config, Func<string, bool> trustNewHostKey)
    {
        config.Validate();
        _config = config;
        _pinned = config.HostKey;
        _trustNewHostKey = trustNewHostKey;
    }

    // ------------------------------------------------------------ connection

    private async Task<SshClient> ClientAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var c = _client;
        if (c is { IsConnected: true }) return c;
        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is { IsConnected: true }) return _client;
            await DropClientAsync().ConfigureAwait(false);
            if (!File.Exists(_config.KeyPath))
                throw new BridgeException(BridgeExit.Config, $"no key at {_config.KeyPath}: run setup to create and enroll one");

            var key = new PrivateKeyFile(_config.KeyPath);
            var info = new ConnectionInfo(_config.HostName, _config.Port, _config.User,
                new PrivateKeyAuthenticationMethod(_config.User, key))
            {
                Timeout = ConnectTimeout,
                MaxSessions = 8,   // below sshd's default MaxSessions 10
            };
            var client = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(15) };
            HostKeyMismatchException? mismatch = null;
            string? newlyTrusted = null;
            client.HostKeyReceived += (_, e) =>
            {
                var actual = "SHA256:" + e.FingerPrintSHA256;
                if (_pinned.Length > 0)
                {
                    e.CanTrust = Same(_pinned, actual);
                    if (!e.CanTrust) mismatch = new HostKeyMismatchException(_pinned, actual);
                }
                else
                {
                    e.CanTrust = _trustNewHostKey(actual);
                    if (e.CanTrust) newlyTrusted = actual;
                }
            };
            try
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (SshConnectionException) when (mismatch is not null)
            {
                client.Dispose();
                throw mismatch;
            }
            catch (SshAuthenticationException e)
            {
                client.Dispose();
                throw new BridgeException(BridgeExit.Config,
                    $"the Mac refused the key {_config.KeyPath} for user {_config.User} ({e.Message}): enroll its .pub with blip-dispatch (see README)", e);
            }
            catch (Exception e) when (e is SocketException or SshConnectionException or SshOperationTimeoutException
                                          or ProxyException or TimeoutException or IOException)
            {
                client.Dispose();
                if (mismatch is not null) throw mismatch;
                // Keep the real reason (DNS, refused, timeout, key exchange): "offline" alone hides it.
                throw new BridgeOfflineException($"{_config.HostName}:{_config.Port} unreachable ({e.GetType().Name}: {e.Message})", e);
            }
            if (newlyTrusted is not null)
            {
                _pinned = newlyTrusted;
                HostKeyPinned?.Invoke(newlyTrusted);
            }
            client.ErrorOccurred += (_, _) => _ = DropPoolAsync();
            _client = client;
            return client;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private static bool Same(string a, string b) => a.TrimEnd('=') == b.TrimEnd('=');

    private async Task DropClientAsync()
    {
        await DropPoolAsync().ConfigureAwait(false);
        var c = _client;
        _client = null;
        if (c is null) return;
        try { c.Disconnect(); } catch { /* already gone */ }
        c.Dispose();
    }

    private static BridgeOfflineException Offline(Exception e) =>
        new("the iMessage bridge connection dropped", e);

    private static bool IsTransportFailure(Exception e) =>
        e is SshConnectionException or SocketException or ObjectDisposedException or IOException;

    // ------------------------------------------------------------ one-shot

    public async Task<BridgeResult> RunAsync(BridgeTool tool, IReadOnlyList<string> args,
        ReadOnlyMemory<byte> stdin = default, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var sink = new MemoryStream();
        return await ExecuteAsync(tool, args, stdin, sink, long.MaxValue, timeout ?? DefaultRunTimeout, ct).ConfigureAwait(false);
    }

    public Task<BridgeResult> StreamAsync(BridgeTool tool, IReadOnlyList<string> args, Stream destination,
        long maxBytes, CancellationToken ct = default) =>
        ExecuteAsync(tool, args, default, destination, maxBytes, TimeSpan.FromMinutes(5), ct);

    private async Task<BridgeResult> ExecuteAsync(BridgeTool tool, IReadOnlyList<string> args, ReadOnlyMemory<byte> stdin,
        Stream destination, long maxBytes, TimeSpan timeout, CancellationToken ct)
    {
        var client = await ClientAsync(ct).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var cmd = client.CreateCommand(PosixQuote.Command(tool.Name(), args), Encoding.UTF8);
        using var reg = cts.Token.Register(() => Abort(cmd));
        try
        {
            var exec = cmd.ExecuteAsync(cts.Token);
            // Always open and close stdin: EOF is what tells imsg-send --text-stdin the body ended,
            // and a tool that never reads stdin is not affected.
            await using (var input = cmd.CreateInputStream())
            {
                if (!stdin.IsEmpty) await input.WriteAsync(stdin, cts.Token).ConfigureAwait(false);
            }
            var outTask = CopyCappedAsync(cmd.OutputStream, destination, maxBytes, cts.Token);
            var errTask = ReadAllTextAsync(cmd.ExtendedOutputStream, cts.Token);
            await exec.ConfigureAwait(false);
            await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);
            var status = cmd.ExitStatus ?? BridgeExit.TempFail;
            var stdout = destination is MemoryStream ms ? ms.ToArray() : [];
            return new BridgeResult(status, stdout, stderr);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            throw new BridgeException(BridgeExit.TempFail, $"{tool.Name()} timed out after {timeout.TotalSeconds:0}s");
        }
        catch (Exception e) when (IsTransportFailure(e))
        {
            await DropClientAsync().ConfigureAwait(false);
            throw Offline(e);
        }
    }

    private static async Task CopyCappedAsync(Stream src, Stream dst, long max, CancellationToken ct)
    {
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > max) throw new BridgeException(BridgeExit.Usage, $"reply exceeds {max} bytes");
            await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadAllTextAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct).ConfigureAwait(false);
        // stderr is diagnostics; keep the tail only.
        var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        return text.Length > 8192 ? text[^8192..] : text;
    }

    // ------------------------------------------------------------ streaming lines (watch)

    public async IAsyncEnumerable<string> ReadLinesAsync(BridgeTool tool, IReadOnlyList<string> args,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = await ClientAsync(ct).ConfigureAwait(false);
        using var cmd = client.CreateCommand(PosixQuote.Command(tool.Name(), args), Encoding.UTF8);
        var exec = cmd.ExecuteAsync(CancellationToken.None);
        await using (cmd.CreateInputStream()) { }
        using var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8);
        // SSH.NET's output stream ignores the token on a pending read, so a silent
        // half-open channel would hold the reader forever. Cancelling tears the
        // channel down instead, which ends the read.
        using var reg = ct.Register(() => Abort(cmd));
        try
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (Exception e) when (IsTransportFailure(e))
                {
                    await DropClientAsync().ConfigureAwait(false);
                    throw Offline(e);
                }
                if (line is null)
                {
                    ct.ThrowIfCancellationRequested();
                    yield break;
                }
                yield return line;
            }
        }
        finally
        {
            try { cmd.CancelAsync(forceKill: false, millisecondsTimeout: 500); } catch { /* finished or gone */ }
            try { await exec.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    /// <summary>Stop a remote command and unblock anything reading its output.</summary>
    private static void Abort(SshCommand cmd)
    {
        try { cmd.CancelAsync(forceKill: true, millisecondsTimeout: 200); } catch { /* finished or gone */ }
        try { cmd.OutputStream.Dispose(); } catch { }
        try { cmd.ExtendedOutputStream.Dispose(); } catch { }
    }

    // ------------------------------------------------------------ serve pool

    public async Task<BridgeResult> QueryAsync(IReadOnlyList<string> imsgArgs, string? stdin = null, CancellationToken ct = default)
    {
        ServeChannel? ch = null;
        try
        {
            ch = await AcquireAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException and not HostKeyMismatchException
                                      and not BridgeOfflineException and not BridgeConfigException)
        {
            ch = null; // serve unavailable (old bridge, spawn failure): one-shot below
        }
        if (ch is not null)
        {
            try
            {
                var r = await ch.RequestAsync(imsgArgs, stdin, RequestTimeout, ct).ConfigureAwait(false);
                if (r.Status != BridgeExit.Usage) return r;
                // 64 = serve refused the command (not on its allowlist): the one-shot path decides.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* channel died: it killed itself; fall back */ }
            finally { ch.Exit(); }
        }
        var bytes = stdin is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(stdin);
        return await RunAsync(BridgeTool.Imsg, imsgArgs, bytes, null, ct).ConfigureAwait(false);
    }

    private async Task<ServeChannel?> AcquireAsync(CancellationToken ct)
    {
        await _poolGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ServeChannel[] live;
            lock (_pool) { _pool.RemoveAll(c => c.Dead); live = [.. _pool]; }
            foreach (var c in live)
                if (c.TryEnter()) return c;
            if (live.Length < PoolSize)
            {
                var c = await SpawnAsync(ct).ConfigureAwait(false);
                lock (_pool) _pool.Add(c);
                await c.EnterAsync(ct).ConfigureAwait(false);
                return c;
            }
        }
        finally
        {
            _poolGate.Release();
        }
        // Pool full and busy: wait for the first slot rather than queueing behind a stall forever.
        ServeChannel? first;
        lock (_pool) first = _pool.FirstOrDefault(c => !c.Dead);
        if (first is null) return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        await first.EnterAsync(cts.Token).ConfigureAwait(false);
        if (!first.Dead) return first;
        first.Exit();
        return null;
    }

    private async Task<ServeChannel> SpawnAsync(CancellationToken ct)
    {
        var client = await ClientAsync(ct).ConfigureAwait(false);
        var cmd = client.CreateCommand(PosixQuote.Command(BridgeTool.Imsg.Name(), ["serve"]), Encoding.UTF8);
        var exec = cmd.ExecuteAsync(CancellationToken.None);
        var input = cmd.CreateInputStream();
        var ch = new ServeChannel(input, cmd.OutputStream, async () =>
        {
            try { await input.DisposeAsync().ConfigureAwait(false); } catch { }
            Abort(cmd);
            try { await exec.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            cmd.Dispose();
        });
        await ch.HandshakeAsync(SpawnTimeout, ct).ConfigureAwait(false);
        return ch;
    }

    private async Task DropPoolAsync()
    {
        ServeChannel[] old;
        lock (_pool) { old = [.. _pool]; _pool.Clear(); }
        foreach (var c in old) await c.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DropClientAsync().ConfigureAwait(false);
        _connectGate.Dispose();
        _poolGate.Dispose();
    }
}
