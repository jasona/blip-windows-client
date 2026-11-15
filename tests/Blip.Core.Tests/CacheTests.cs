using System.Text;
using Blip.Core.Cache;
using Blip.Core.Config;
using Blip.Core.Protocol;
using Blip.Core.Transport;
using Microsoft.Extensions.Time.Testing;

namespace Blip.Core.Tests;

public sealed class CacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blip-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------ avatars

    [Fact]
    public void Avatar_round_trips_and_goes_stale_after_max_age()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = new AvatarCache(_dir, TimeSpan.FromDays(1), clock);
        Assert.Null(cache.TryGet("+15550100"));

        cache.Store("+15550100", [1, 2, 3]);
        var e = cache.TryGet("+15550100");
        Assert.NotNull(e);
        Assert.Equal([1, 2, 3], e.Value.Bytes);
        Assert.True(e.Value.Fresh);

        clock.Advance(TimeSpan.FromDays(2));
        Assert.False(cache.TryGet("+15550100")!.Value.Fresh);
    }

    [Fact]
    public void Avatar_remembers_no_photo_and_a_later_photo_replaces_it()
    {
        var cache = new AvatarCache(_dir);
        cache.Store("chat42", null);
        var none = cache.TryGet("chat42");
        Assert.NotNull(none);
        Assert.Null(none.Value.Bytes);

        cache.Store("chat42", [9]);
        Assert.Equal([9], cache.TryGet("chat42")!.Value.Bytes);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Avatar_file_names_never_contain_the_handle()
    {
        var cache = new AvatarCache(_dir);
        cache.Store("+15550100", [1]);
        cache.Store("pat@example.com", [1]);
        foreach (var f in Directory.GetFiles(_dir).Select(Path.GetFileName))
        {
            Assert.DoesNotContain("5550100", f);
            Assert.DoesNotContain("example", f);
        }
    }

    // ------------------------------------------------------------ snapshot

    private static byte[] Scramble(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).Reverse().ToArray();
    private static byte[] Unscramble(byte[] b) => b.Reverse().Select(x => (byte)(x ^ 0x5A)).ToArray();

    private SnapshotStore Store() => new(Path.Combine(_dir, "snapshot.bin"), Scramble, Unscramble);

    private static Snapshot Sample(string key) => new()
    {
        Key = key,
        SavedAt = DateTimeOffset.UtcNow,
        Chats = [new ChatInfo { Id = "+15550100", Name = "Pat", LastText = "see you at noon" }],
        ReadState = new ReadStateSnapshot { Version = 1, Chats = [new ChatReadState { Chat = "+15550100", Unread = 2 }] },
        Groups = [],
        Threads = new() { ["+15550100"] = [new Message { Id = 7, Guid = "g7", Text = "see you at noon", Ts = "2026-11-15T20:00:00Z" }] },
    };

    [Fact]
    public void Snapshot_round_trips_through_the_protector()
    {
        var store = Store();
        store.Save(Sample("k"));
        var back = store.Load("k");
        Assert.NotNull(back);
        Assert.Equal("Pat", Assert.Single(back.Chats).Name);
        Assert.Equal(2, Assert.Single(back.ReadState!.Chats).Unread);
        Assert.Equal("see you at noon", Assert.Single(back.Threads["+15550100"]).Text);
    }

    [Fact]
    public async Task Real_bridge_output_survives_a_snapshot_unchanged()
    {
        var t = new FakeTransport
        {
            Respond = call => call.Args.Contains("chats") ? FakeTransport.Json("chats.json")
                : call.Args.Contains("groups") ? FakeTransport.Json("groups.json")
                : call.Args.Contains("read-state") ? FakeTransport.Json("read-state.json")
                : FakeTransport.Json("thread-dm.json"),
        };
        var client = new BlipClient(t, new BridgeConfig { Host = "you@your-mac" });
        var live = await client.SidebarAsync();
        var thread = await client.ThreadAsync("+15550100");

        var store = Store();
        store.Save(new Snapshot
        {
            Key = "k", Chats = live.Chats.ToList(), ReadState = live.ReadState, Groups = live.Groups.ToList(),
            Threads = new() { ["+15550100"] = thread.ToList() },
        });
        var back = store.Load("k")!;
        var rebuilt = new SidebarData(back.Chats, back.ReadState!, back.Groups).Build();

        Assert.Equal(live.Build().Select(c => (c.Id, c.Title, c.Unread, c.LastTime, c.Pinned)),
                     rebuilt.Select(c => (c.Id, c.Title, c.Unread, c.LastTime, c.Pinned)));
        Assert.Equal(thread.Select(m => (m.Guid, m.Text, m.Ts, m.FromMe, m.Attachments?.Count ?? 0)),
                     back.Threads["+15550100"].Select(m => (m.Guid, m.Text, m.Ts, m.FromMe, m.Attachments?.Count ?? 0)));
    }

    [Fact]
    public void Snapshot_file_never_holds_plain_message_text()
    {
        var store = Store();
        store.Save(Sample("k"));
        var raw = File.ReadAllBytes(store.FilePath);
        Assert.DoesNotContain("see you at noon", Encoding.UTF8.GetString(raw));
        Assert.DoesNotContain("see you at noon", Encoding.Latin1.GetString(raw));
    }

    [Fact]
    public void Snapshot_for_another_mac_or_filter_set_is_ignored()
    {
        var a = new BridgeConfig { Host = "you@mac-a" };
        var store = Store();
        store.Save(Sample(SnapshotStore.KeyFor(a)));
        Assert.Null(store.Load(SnapshotStore.KeyFor(a with { Host = "you@mac-b" })));
        Assert.Null(store.Load(SnapshotStore.KeyFor(a with { HideSpam = true })));
        Assert.NotNull(store.Load(SnapshotStore.KeyFor(a with { Tapbacks = true })));
    }

    [Fact]
    public void Corrupt_or_undecryptable_snapshot_is_just_absent()
    {
        var store = Store();
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(store.FilePath, [1, 2, 3]);
        Assert.Null(store.Load("k"));

        var wrongUser = new SnapshotStore(store.FilePath, Scramble, _ => throw new System.Security.Cryptography.CryptographicException());
        wrongUser.Save(Sample("k"));
        Assert.Null(wrongUser.Load("k"));
    }

    [Fact]
    public void Delete_removes_the_snapshot()
    {
        var store = Store();
        store.Save(Sample("k"));
        store.Delete();
        Assert.False(File.Exists(store.FilePath));
        store.Delete();   // idempotent
    }

    // ------------------------------------------------------------ config + client

    [Fact]
    public void Local_cache_defaults_on_and_round_trips()
    {
        Assert.True(BridgeConfig.Parse("host=you@your-mac").LocalCache);
        Assert.False(BridgeConfig.Parse("host=you@your-mac\nlocal_cache=off").LocalCache);
        var c = new BridgeConfig { Host = "you@your-mac", LocalCache = false };
        Assert.False(BridgeConfig.Parse(c.Serialize()).LocalCache);
    }

    [Theory]
    [InlineData(0, new byte[] { 1, 2 }, true, true)]
    [InlineData(1, new byte[0], true, false)]     // imsg avatar: nothing found → cacheable "none"
    [InlineData(69, new byte[0], false, false)]   // Mac unavailable → unknown, do not cache
    public async Task Avatar_lookup_tells_none_from_unknown(int status, byte[] stdout, bool known, bool hasBytes)
    {
        var t = new FakeTransport { Respond = _ => new BridgeResult(status, stdout, "") };
        var c = new BlipClient(t, new BridgeConfig { Host = "you@your-mac" });
        var r = await c.AvatarLookupAsync("+15550100");
        Assert.Equal(known, r.Known);
        Assert.Equal(hasBytes, r.Bytes is not null);
        Assert.Equal(["avatar", "--", "+15550100"], Assert.Single(t.Calls).Args);
    }
}
