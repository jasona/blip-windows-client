using System.IO.Pipelines;
using System.Text;
using Blip.Core.Config;
using Blip.Core.Transport;

namespace Blip.Core.Tests;

public class PosixQuoteTests
{
    [Fact]
    public void Quotes_like_blip_shim()
    {
        Assert.Equal("imsg-send '--to' '+15551234567' 'it'\\''s' '$HOME' 'a\tb'",
            PosixQuote.Command("imsg-send", ["--to", "+15551234567", "it's", "$HOME", "a\tb"]));
    }
}

public class ConfigTests
{
    [Fact]
    public void Parses_like_the_shim_and_ignores_unknown_keys()
    {
        var c = BridgeConfig.Parse("""
            # comment
            host = "you@your-mac"   # trailing
            hide_spam=on
            tapbacks=yes
            replies=0
            evil=$(touch /tmp/x)
            port=2222
            """);
        Assert.Equal("you@your-mac", c.Host);
        Assert.Equal("you", c.User);
        Assert.Equal("your-mac", c.HostName);
        Assert.Equal(2222, c.Port);
        Assert.True(c.HideSpam);
        Assert.True(c.Tapbacks);
        Assert.False(c.Replies);
        c.Validate();
    }

    [Theory]
    [InlineData("you@mac;rm -rf /")]
    [InlineData("-oProxyCommand=evil")]
    [InlineData("")]
    public void Refuses_hostile_hosts(string host) =>
        Assert.Throws<BridgeConfigException>(() => new BridgeConfig { Host = host }.Validate());

    [Fact]
    public void Round_trips()
    {
        var c = new BridgeConfig { Host = "you@your-mac", Port = 22, HostKey = "SHA256:" + new string('A', 43), PushRead = true };
        var back = BridgeConfig.Parse(c.Serialize());
        Assert.Equal(c with { KeyPath = back.KeyPath }, back);
    }
}

public class ServeChannelTests
{
    /// <summary>A scripted `imsg serve` on the far side of two pipes.</summary>
    private static (ServeChannel ch, Task server, List<string> requests) Serve(Func<string, (int status, string body)> answer,
        Action<Stream>? sabotage = null)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var requests = new List<string>();
        var killed = false;
        var ch = new ServeChannel(toServer.Writer.AsStream(), toClient.Reader.AsStream(), () => { killed = true; return ValueTask.CompletedTask; });
        var server = Task.Run(async () =>
        {
            var output = toClient.Writer.AsStream();
            await output.WriteAsync("ready\n"u8.ToArray());
            using var reader = new StreamReader(toServer.Reader.AsStream());
            while (await reader.ReadLineAsync() is { } line && !killed)
            {
                requests.Add(line);
                if (sabotage is not null) { sabotage(output); continue; }
                var (status, body) = answer(line);
                var bytes = Encoding.UTF8.GetBytes(body);
                await output.WriteAsync(Encoding.UTF8.GetBytes($"{{\"status\":{status},\"len\":{bytes.Length}}}\n"));
                // split the body across writes to exercise framing
                for (var i = 0; i < bytes.Length; i += 7)
                    await output.WriteAsync(bytes.AsMemory(i, Math.Min(7, bytes.Length - i)));
            }
        });
        return (ch, server, requests);
    }

    [Fact]
    public async Task Frames_requests_and_replies()
    {
        var (ch, _, requests) = Serve(_ => (0, "[{\"id\":\"x\"}]\n"));
        await ch.HandshakeAsync(TimeSpan.FromSeconds(5), default);
        await ch.EnterAsync(default);
        var r1 = await ch.RequestAsync(["--json", "chats", "5"], null, TimeSpan.FromSeconds(5), default);
        var r2 = await ch.RequestAsync(["--json", "search", "--stdin", "5"], "q\n'\"", TimeSpan.FromSeconds(5), default);
        Assert.Equal("[{\"id\":\"x\"}]\n", r1.Text);
        Assert.True(r2.Ok);
        Assert.Equal("{\"argv\":[\"--json\",\"chats\",\"5\"]}", requests[0]);
        Assert.Contains("\"stdin\":\"q\\n\\u0027\\u0022\"", requests[1]);
    }

    [Fact]
    public async Task Error_status_carries_reason()
    {
        var (ch, _, _) = Serve(_ => (64, "serve: 'watch' is not available on this channel\n"));
        await ch.HandshakeAsync(TimeSpan.FromSeconds(5), default);
        await ch.EnterAsync(default);
        var r = await ch.RequestAsync(["watch"], null, TimeSpan.FromSeconds(5), default);
        Assert.Equal(64, r.Status);
        Assert.Contains("not available", r.Reason);
    }

    [Fact]
    public async Task A_stalled_reply_kills_the_channel()
    {
        var (ch, _, _) = Serve(_ => (0, ""), sabotage: _ => { /* never answer */ });
        await ch.HandshakeAsync(TimeSpan.FromSeconds(5), default);
        await ch.EnterAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ch.RequestAsync(["--json", "chats", "5"], null, TimeSpan.FromMilliseconds(200), default));
        Assert.True(ch.Dead);
    }

    [Fact]
    public async Task An_oversized_header_is_a_desync()
    {
        var (ch, _, _) = Serve(_ => (0, ""), sabotage: s => s.Write("{\"status\":0,\"len\":999999999999}\n"u8));
        await ch.HandshakeAsync(TimeSpan.FromSeconds(5), default);
        await ch.EnterAsync(default);
        await Assert.ThrowsAnyAsync<Exception>(() => ch.RequestAsync(["x"], null, TimeSpan.FromSeconds(5), default));
        Assert.True(ch.Dead);
    }
}
