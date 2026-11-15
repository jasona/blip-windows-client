using Blip.Core.Transport;

namespace Blip.Core.Live;

public enum BridgeStatus { Connecting, Online, Offline, NeedsAttention }

/// <summary>
/// Real-time updates: holds <c>imsg watch</c> open and turns its lines into events.
/// </summary>
/// <remarks>
/// watch emits "ready", then a unix timestamp whenever chat.db or its WAL changes
/// (an INVALIDATION, never content), and "hb" every ~30 s. A half-open link emits
/// nothing forever, so silence longer than <see cref="IdleTimeout"/> counts as dead.
/// After any reconnect a change is raised too: pings may have been missed.
/// </remarks>
public sealed class BridgeMonitor : IAsyncDisposable
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly IBridgeTransport _transport;
    private readonly TimeProvider _time;
    private readonly TimeSpan _idleTimeout;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private int _pendingChange;

    /// <summary>Something changed on the Mac: reload the list and the open conversation.</summary>
    public event Action? Changed;
    public event Action<BridgeStatus, string>? StatusChanged;

    public BridgeStatus Status { get; private set; } = BridgeStatus.Connecting;
    public string StatusDetail { get; private set; } = "";

    public BridgeMonitor(IBridgeTransport transport, TimeProvider? time = null, TimeSpan? idleTimeout = null)
    {
        _transport = transport;
        _time = time ?? TimeProvider.System;
        _idleTimeout = idleTimeout ?? IdleTimeout;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private void SetStatus(BridgeStatus s, string detail)
    {
        if (s == Status && detail == StatusDetail) return;
        Status = s;
        StatusDetail = detail;
        StatusChanged?.Invoke(s, detail);
    }

    private async Task RunAsync(CancellationToken stop)
    {
        var backoff = MinBackoff;
        while (!stop.IsCancellationRequested)
        {
            SetStatus(BridgeStatus.Connecting, "");
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stop);
            using var watchdog = _time.CreateTimer(_ => { try { attempt.Cancel(); } catch (ObjectDisposedException) { } },
                null, _idleTimeout, Timeout.InfiniteTimeSpan);
            try
            {
                await foreach (var line in _transport.ReadLinesAsync(BridgeTool.Imsg, ["watch"], attempt.Token).ConfigureAwait(false))
                {
                    watchdog.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                    if (line == "ready")
                    {
                        backoff = MinBackoff;
                        SetStatus(BridgeStatus.Online, "");
                        RaiseChanged();      // whatever happened while we were away
                    }
                    else if (line == "hb")
                    {
                        // liveness only
                    }
                    else if (line.Length > 0 && line.All(char.IsAsciiDigit))
                    {
                        RaiseChanged();
                    }
                }
                // Stream ended (Mac rebooted, sshd restarted): fall through to retry.
                SetStatus(BridgeStatus.Offline, "watch channel closed");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                SetStatus(BridgeStatus.Offline, "no heartbeat from the Mac");
            }
            catch (HostKeyMismatchException e)
            {
                SetStatus(BridgeStatus.NeedsAttention, e.Message);
                return;   // never retry a changed host key on our own
            }
            catch (BridgeException e) when (e.Status == BridgeExit.Config)
            {
                SetStatus(BridgeStatus.NeedsAttention, e.Message);
                return;
            }
            catch (Exception e)
            {
                SetStatus(BridgeStatus.Offline, e.Message);
            }

            try { await Task.Delay(backoff, _time, stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    /// <summary>Coalesce bursts (a send touches the WAL several times) into one reload.</summary>
    private void RaiseChanged()
    {
        if (Interlocked.Exchange(ref _pendingChange, 1) == 1) return;
        _ = Task.Delay(Debounce, _time).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref _pendingChange, 0);
            Changed?.Invoke();
        }, TaskScheduler.Default);
    }

    /// <summary>Restart after the person fixed whatever NeedsAttention reported.</summary>
    public void Restart()
    {
        if (_loop is { IsCompleted: true }) _loop = null;
        Start();
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _stop.Cancel();
        if (_loop is not null)
            try { await _loop.ConfigureAwait(false); } catch { /* stopping */ }
        _stop.Dispose();
    }
}
