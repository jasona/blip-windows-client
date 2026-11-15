using System.Text;
using Blip.Core.Config;
using Blip.Core.Logic;
using Blip.Core.Protocol;
using Blip.Core.Transport;

namespace Blip.Core.Tests;

public class ClientTests
{
    private static readonly Conversation Dm = new() { Id = "+15551234567", Title = "Pat" };
    private static readonly Conversation Group = new() { Id = "chat1", Title = "Crew", IsGroup = true, GroupGuid = "iMessage;+;chat1" };

    private static (BlipClient, FakeTransport) Make(BridgeConfig? cfg = null)
    {
        var t = new FakeTransport();
        return (new BlipClient(t, cfg ?? new BridgeConfig { Host = "you@your-mac" }), t);
    }

    [Fact]
    public async Task Text_rides_stdin_never_argv()
    {
        var (c, t) = Make();
        const string body = "secret 'quoted' -- --yes; $(rm -rf)";
        var r = await c.SendTextAsync(Dm, body);
        Assert.True(r.Ok);
        var call = Assert.Single(t.Calls);
        Assert.Equal(BridgeTool.ImsgSend, call.Tool);
        Assert.Equal(["--to", "+15551234567", "--yes", "--text-stdin", "--keep-dashes"], call.Args);
        Assert.Equal(body, Encoding.UTF8.GetString(call.Stdin));
        Assert.DoesNotContain(call.Args, a => a.Contains("secret"));
    }

    [Fact]
    public async Task Group_sends_by_guid_and_ignores_service()
    {
        var (c, t) = Make();
        await c.SendTextAsync(Group, "hi", SendService.SMS);
        Assert.Equal(["--chat-id", "iMessage;+;chat1", "--yes", "--text-stdin", "--keep-dashes"], t.Calls[0].Args);
    }

    [Fact]
    public async Task Green_dm_names_its_service()
    {
        var (c, t) = Make();
        await c.SendTextAsync(Dm, "hi", SendService.SMS);
        Assert.Equal(["--to", "+15551234567", "--service", "SMS", "--yes", "--text-stdin", "--keep-dashes"], t.Calls[0].Args);
    }

    [Fact]
    public async Task Group_without_guid_refuses_before_reaching_the_mac()
    {
        var (c, t) = Make();
        var r = await c.SendTextAsync(Group with { GroupGuid = "" }, "hi");
        Assert.False(r.Ok);
        Assert.Empty(t.Calls);
    }

    [Fact]
    public async Task Empty_and_oversized_text_never_send()
    {
        var (c, t) = Make();
        Assert.False((await c.SendTextAsync(Dm, "   ")).Ok);
        Assert.False((await c.SendTextAsync(Dm, new string('a', 4001))).Ok);
        Assert.Empty(t.Calls);
    }

    [Fact]
    public async Task File_send_puts_caption_ahead_of_bytes_on_stdin()
    {
        var (c, t) = Make();
        var path = Path.Combine(Path.GetTempPath(), $"blip-test-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        try
        {
            var r = await c.SendFileAsync(Dm, path, "look");
            Assert.True(r.Ok);
            var call = t.Calls.Single();
            Assert.Equal(["--to", "+15551234567", "--file-stdin", "--name", Path.GetFileName(path), "--yes",
                "--file-bytes", "4", "--keep-dashes", "--text-stdin-bytes", "4"], call.Args);
            Assert.Equal([.. "look"u8.ToArray(), 1, 2, 3, 4], call.Stdin);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Reads_go_through_the_serve_path_with_shim_flags()
    {
        var (c, t) = Make(new BridgeConfig { Host = "you@your-mac", HideSpam = true });
        t.Respond = call => call.Args.Contains("thread") ? FakeTransport.Json("thread-dm.json") : FakeTransport.Json("chats.json");
        var msgs = await c.ThreadAsync("+15550100011");
        Assert.Equal(3, msgs.Count);
        var call = t.Calls.Single();
        Assert.True(call.ViaServe);
        Assert.Equal(["--json", "--rich", "thread", "--chat", "+15550100011", "80", "--hide-spam"], call.Args);
    }

    [Fact]
    public async Task Search_query_rides_stdin()
    {
        var (c, t) = Make();
        t.Respond = _ => new BridgeResult(0, "[]"u8.ToArray(), "");
        await c.SearchAsync("dinner plans");
        var call = t.Calls.Single();
        Assert.Equal(["--json", "search", "--stdin", "50"], call.Args);
        Assert.Equal("dinner plans", Encoding.UTF8.GetString(call.Stdin));
    }

    [Fact]
    public async Task Opt_in_features_are_refused_locally_when_off()
    {
        var (c, t) = Make();
        var m = new Message { Guid = "g1" };
        Assert.False((await c.ReactAsync(m, TapbackKind.Love, false)).Ok);
        Assert.False((await c.ReplyAsync(m, "hi")).Ok);
        Assert.Empty(t.Calls);
    }

    [Fact]
    public async Task Offline_maps_to_not_online()
    {
        var (c, t) = Make();
        t.Respond = _ => new BridgeResult(BridgeExit.Unavailable, [], "unreachable");
        var r = await c.SendTextAsync(Dm, "hi");
        Assert.False(r.Ok);
        Assert.False(r.Online);
    }

    [Fact]
    public async Task Bad_chat_ids_never_reach_the_bridge()
    {
        var (c, t) = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => c.ThreadAsync("--help"));
        Assert.Empty(t.Calls);
    }

    [Fact]
    public async Task Attachment_ids_must_be_digits()
    {
        var (c, t) = Make();
        var r = await c.FetchAttachmentAsync("../../etc", Stream.Null);
        Assert.False(r.Ok);
        Assert.Empty(t.Calls);
        await c.FetchAttachmentAsync("30", Stream.Null, previewMaxDim: 800);
        Assert.Equal(["attachment", "30", "--jpeg", "--max-dim", "800"], t.Calls.Single().Args);
    }
}
