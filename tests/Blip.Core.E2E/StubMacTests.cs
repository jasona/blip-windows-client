using System.Text;
using System.Text.Json;
using Blip.Core.Config;
using Blip.Core.Live;
using Blip.Core.Logic;
using Blip.Core.Transport;
using Renci.SshNet;

namespace Blip.Core.E2E;

/// <summary>
/// End to end over real SSH against a STUB Mac: a local sshd whose authorized_keys pins the
/// key to the real blip-dispatch, which runs the real imsg against a synthetic chat.db.
/// Only imsg-send / imsg-read are stubbed (no AppleScript off a Mac); they log what arrived.
/// Set up by tests/e2e/setup-stub-mac.sh. Never point these at a real Mac: the send tests
/// would send real messages.
/// </summary>
public sealed class E2EFact : FactAttribute
{
    public E2EFact()
    {
        if (Environment.GetEnvironmentVariable("BLIP_E2E_SENT_LOG") is not { Length: > 0 })
            Skip = "set BLIP_E2E_HOST/PORT/KEY/SENT_LOG (tests/e2e/setup-stub-mac.sh)";
    }
}

public class StubMacTests
{
    private static string Env(string k) => Environment.GetEnvironmentVariable(k) ?? "";

    private static BridgeConfig Cfg(string? hostKey = null) => new()
    {
        Host = Env("BLIP_E2E_HOST"),
        Port = int.Parse(Env("BLIP_E2E_PORT")),
        KeyPath = Env("BLIP_E2E_KEY"),
        HostKey = hostKey ?? "",
    };

    private static SshBridgeTransport Transport(BridgeConfig? cfg = null) => new(cfg ?? Cfg(), _ => true);

    private static List<JsonElement> SentLog() =>
        File.Exists(Env("BLIP_E2E_SENT_LOG"))
            ? File.ReadAllLines(Env("BLIP_E2E_SENT_LOG")).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList()
            : [];

    [E2EFact]
    public async Task Sidebar_and_thread_load_over_the_serve_channel()
    {
        await using var t = Transport();
        var c = new BlipClient(t, Cfg());
        var convs = await c.ConversationsAsync();
        Assert.Equal(2, convs.Count);
        Assert.Equal(1, convs.Single(x => x.Id == "+15550100011").Unread);
        var group = convs.Single(x => x.IsGroup);
        Assert.Equal("Trail Crew", group.Title);
        Assert.True(group.CanSend);

        var msgs = await c.ThreadAsync("+15550100011");
        Assert.Equal(["hello there", "hi back", "unread one"], msgs.Select(m => m.Text));
        Assert.Single(msgs[1].Tapbacks!);

        // Many queries on the pooled channels: no handshake per call.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++) await c.ReadStateAsync();
        Assert.True(sw.ElapsedMilliseconds < 20_000);
    }

    [E2EFact]
    public async Task Attachment_bytes_stream_intact()
    {
        await using var t = Transport();
        var c = new BlipClient(t, Cfg());
        using var ms = new MemoryStream();
        var r = await c.FetchAttachmentAsync("30", ms);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(800, ms.Length);
        Assert.StartsWith("PNGDATA-", Encoding.ASCII.GetString(ms.ToArray()));
    }

    [E2EFact]
    public async Task Send_text_arrives_on_stdin_byte_for_byte()
    {
        await using var t = Transport();
        var c = new BlipClient(t, Cfg());
        var before = SentLog().Count;
        const string body = "it's \"quoted\"\n$(whoami) `id` ; --yes émoji 👋";
        var r = await c.SendTextAsync(new Conversation { Id = "+15551234567", Title = "me" }, body);
        Assert.True(r.Ok, r.Error);
        var e = SentLog().Skip(before).Single();
        Assert.Equal("imsg-send", e.GetProperty("tool").GetString());
        Assert.Equal(["--to", "+15551234567", "--yes", "--text-stdin", "--keep-dashes"],
            e.GetProperty("argv").EnumerateArray().Select(x => x.GetString()!));
        Assert.Equal(body, Encoding.UTF8.GetString(Convert.FromHexString(e.GetProperty("stdin_hex").GetString()!)));
    }

    [E2EFact]
    public async Task Send_file_with_awkward_name_and_caption()
    {
        await using var t = Transport();
        var c = new BlipClient(t, Cfg());
        var dir = Directory.CreateTempSubdirectory("blip-e2e");
        var path = Path.Combine(dir.FullName, "it's a\tphoto.png");
        var bytes = Enumerable.Range(0, 70_000).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var before = SentLog().Count;
        var r = await c.SendFileAsync(new Conversation { Id = "chat900000000000000001", Title = "g", IsGroup = true,
            GroupGuid = "iMessage;+;chat900000000000000001" }, path, "caption ✓");
        Assert.True(r.Ok, r.Error);
        var e = SentLog().Skip(before).Single();
        var argv = e.GetProperty("argv").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal("it's a\tphoto.png", argv[argv.IndexOf("--name") + 1]);
        Assert.Equal("iMessage;+;chat900000000000000001", argv[argv.IndexOf("--chat-id") + 1]);
        var stdin = Convert.FromHexString(e.GetProperty("stdin_hex").GetString()!);
        var cap = Encoding.UTF8.GetBytes("caption ✓");
        Assert.Equal([.. cap, .. bytes], stdin);
        dir.Delete(true);
    }

    [E2EFact]
    public async Task Watch_pushes_a_change_when_chat_db_is_written()
    {
        await using var t = Transport();
        await using var m = new BridgeMonitor(t);
        var changes = 0;
        m.Changed += () => Interlocked.Increment(ref changes);
        m.Start();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (m.Status != BridgeStatus.Online && sw.ElapsedMilliseconds < 15000) await Task.Delay(50);
        Assert.Equal(BridgeStatus.Online, m.Status);
        while (changes < 1 && sw.ElapsedMilliseconds < 15000) await Task.Delay(50);
        var baseline = changes;
        File.SetLastWriteTimeUtc(Env("BLIP_E2E_CHATDB"), DateTime.UtcNow.AddSeconds(5));  // what a Messages write does to mtime
        while (changes == baseline && sw.ElapsedMilliseconds < 20000) await Task.Delay(50);
        Assert.True(changes > baseline, "no push after chat.db changed");
        // Stopping must not wait for the next 30 s heartbeat: a pending read is torn down.
        var stop = System.Diagnostics.Stopwatch.StartNew();
        await m.DisposeAsync();
        Assert.True(stop.ElapsedMilliseconds < 5000, $"stop took {stop.ElapsedMilliseconds} ms");
    }

    [E2EFact]
    public async Task Pinned_host_key_mismatch_refuses_to_connect()
    {
        var wrong = "SHA256:" + Convert.ToBase64String(new byte[32]).TrimEnd('=');
        await using var t = Transport(Cfg(wrong));
        await Assert.ThrowsAsync<HostKeyMismatchException>(() => new BlipClient(t, Cfg(wrong)).ReadStateAsync());
    }

    [E2EFact]
    public async Task First_connect_pins_the_host_key()
    {
        string? pinned = null;
        await using var t = new SshBridgeTransport(Cfg(), fp => true);
        t.HostKeyPinned += fp => pinned = fp;
        await new BlipClient(t, Cfg()).SessionAsync();
        Assert.Equal(Env("BLIP_E2E_HOSTKEY"), pinned);
    }

    [E2EFact]
    public void The_key_cannot_get_a_shell()
    {
        var cfg = Cfg();
        using var client = new SshClient(new ConnectionInfo(cfg.HostName, cfg.Port, cfg.User,
            new PrivateKeyAuthenticationMethod(cfg.User, new PrivateKeyFile(cfg.KeyPath))));
        client.Connect();
        var r = client.RunCommand("bash -c id");
        Assert.Equal(64, r.ExitStatus);
        Assert.Contains("not a Blip tool", r.Error);
    }
}
