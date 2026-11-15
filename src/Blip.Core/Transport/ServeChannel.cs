using System.Text;
using System.Text.Json;
using Blip.Core.Protocol;

namespace Blip.Core.Transport;

/// <summary>
/// One open <c>imsg serve</c> process on the Mac: one chat.db handle, many queries.
/// A one-shot call pays ~90 ms of ssh + Python startup before reading a row; on
/// this channel that is paid once.
/// <code>
///   -> {"argv":["--json","chats","300"],"stdin":"optional"}\n
///   &lt;- {"status":0,"len":N}\n + N bytes
/// </code>
/// Any error (deadline, EOF, desync) kills the channel. Callers then fall back to
/// a one-shot run, exactly as the Linux client falls back from blip-bridged.
/// </summary>
internal sealed class ServeChannel : IAsyncDisposable
{
    public const int MaxBody = 256 << 20;   // a header claiming more is a desync
    private const int MaxHeader = 4096;

    private readonly Stream _input;
    private readonly FramedReader _reader;
    private readonly Func<ValueTask> _kill;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _dead;

    public bool Dead => Volatile.Read(ref _dead) == 1;

    public ServeChannel(Stream input, Stream output, Func<ValueTask> kill)
    {
        _input = input;
        _reader = new FramedReader(output);
        _kill = kill;
    }

    /// <summary>Waits for the "ready" line serve prints after opening chat.db.</summary>
    public async Task HandshakeAsync(TimeSpan deadline, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);
        using var reg = cts.Token.Register(() => _ = KillAsync());
        try
        {
            var line = await _reader.ReadLineAsync(MaxHeader, cts.Token).ConfigureAwait(false);
            if (line != "ready") throw new InvalidDataException($"serve did not start: {line ?? "EOF"}");
        }
        catch
        {
            await KillAsync().ConfigureAwait(false);
            throw;
        }
    }

    public bool TryEnter() => !Dead && _gate.Wait(0);

    public Task EnterAsync(CancellationToken ct) => _gate.WaitAsync(ct);

    public void Exit() => _gate.Release();

    /// <summary>Caller must hold the gate (TryEnter/EnterAsync).</summary>
    public async Task<BridgeResult> RequestAsync(IReadOnlyList<string> argv, string? stdin, TimeSpan deadline, CancellationToken ct)
    {
        if (Dead) throw new ObjectDisposedException(nameof(ServeChannel));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);
        // The underlying SSH stream may ignore the token on a pending read: a deadline
        // kills the channel outright, which is what unblocks it.
        using var reg = cts.Token.Register(() => _ = KillAsync());
        try
        {
            var req = stdin is null
                ? JsonSerializer.Serialize(new ServeRequest(argv), ServeJson.Default.ServeRequest)
                : JsonSerializer.Serialize(new ServeRequestWithStdin(argv, stdin), ServeJson.Default.ServeRequestWithStdin);
            var bytes = Encoding.UTF8.GetBytes(req + "\n");
            await _input.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
            await _input.FlushAsync(cts.Token).ConfigureAwait(false);

            var header = await _reader.ReadLineAsync(MaxHeader, cts.Token).ConfigureAwait(false)
                         ?? throw new EndOfStreamException("serve channel closed");
            var frame = JsonSerializer.Deserialize(header, ServeJson.Default.ServeFrame)
                        ?? throw new InvalidDataException("empty frame header");
            if (frame.Len < 0 || frame.Len > MaxBody) throw new InvalidDataException($"frame length {frame.Len}");
            var body = await _reader.ReadExactAsync(frame.Len, cts.Token).ConfigureAwait(false);
            // serve reports errors in the body (status != 0), stdout and stderr share it.
            return frame.Status == 0
                ? new BridgeResult(0, body, "")
                : new BridgeResult(frame.Status, [], Encoding.UTF8.GetString(body));
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            await KillAsync().ConfigureAwait(false);
            throw new OperationCanceledException("serve request deadline", cts.Token);
        }
        catch
        {
            // A half-read frame leaves the stream desynchronised: this channel is done.
            await KillAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask KillAsync()
    {
        if (Interlocked.Exchange(ref _dead, 1) == 1) return;
        try { await _kill().ConfigureAwait(false); } catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        await KillAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}

internal sealed record ServeRequest(IReadOnlyList<string> argv);
internal sealed record ServeRequestWithStdin(IReadOnlyList<string> argv, string stdin);
internal sealed record ServeFrame(int Status, int Len);
