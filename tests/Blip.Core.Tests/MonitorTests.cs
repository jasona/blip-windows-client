using Blip.Core.Live;
using Blip.Core.Transport;

namespace Blip.Core.Tests;

public class MonitorTests
{
    private static async Task Until(Func<bool> cond, int ms = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < ms) await Task.Delay(10);
        Assert.True(cond(), "condition not met in time");
    }

    [Fact]
    public async Task Ready_goes_online_and_pings_coalesce_into_one_reload()
    {
        var t = new FakeTransport();
        await using var m = new BridgeMonitor(t);
        var changes = 0;
        m.Changed += () => Interlocked.Increment(ref changes);
        m.Start();
        await t.WatchLines.Writer.WriteAsync("ready");
        await Until(() => m.Status == BridgeStatus.Online);
        await Until(() => changes == 1);       // reconnect = maybe-missed changes
        for (var i = 0; i < 5; i++) await t.WatchLines.Writer.WriteAsync("1763236000");
        await t.WatchLines.Writer.WriteAsync("hb");
        await Until(() => changes == 2);
        await Task.Delay(400);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Silence_past_the_idle_timeout_reconnects()
    {
        var t = new FakeTransport();
        await using var m = new BridgeMonitor(t, idleTimeout: TimeSpan.FromMilliseconds(300));
        m.Start();
        await t.WatchLines.Writer.WriteAsync("ready");
        await Until(() => m.Status == BridgeStatus.Online);
        await Until(() => m.Status == BridgeStatus.Offline);
        Assert.Equal("no heartbeat from the Mac", m.StatusDetail);
        await Until(() => t.WatchStarts >= 2, 8000);   // first backoff is 2 s
    }

    [Fact]
    public async Task A_changed_host_key_stops_and_asks_for_attention()
    {
        var t = new FakeTransport { WatchFailure = () => new HostKeyMismatchException("SHA256:a", "SHA256:b") };
        await using var m = new BridgeMonitor(t);
        m.Start();
        await Until(() => m.Status == BridgeStatus.NeedsAttention);
        await Task.Delay(300);
        Assert.Equal(1, t.WatchStarts);
    }
}
