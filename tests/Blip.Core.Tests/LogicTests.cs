using Blip.Core.Logic;
using Blip.Core.Protocol;

namespace Blip.Core.Tests;

public class ChatIdTests
{
    [Theory]
    [InlineData("+15551234567", false)]
    [InlineData("you@example.com", false)]
    [InlineData("chat900000000000000001", true)]
    [InlineData("0123456789abcdef0123456789ABCDEF", true)]
    public void Group_detection_matches_collector(string id, bool group) => Assert.Equal(group, ChatIds.IsGroupChat(id));

    [Theory]
    [InlineData("+15551234567", true)]
    [InlineData("a@b.c", true)]
    [InlineData("chat123", true)]
    [InlineData("rm -rf /", false)]
    [InlineData("", false)]
    [InlineData("a\n@b", false)]
    public void Only_bridge_safe_ids_are_valid(string id, bool ok) => Assert.Equal(ok, ChatIds.IsValid(id));

    [Fact]
    public void Pretty_formats_nanp() => Assert.Equal("(555) 123-4567", ChatIds.Pretty("+15551234567"));
}

public class SendServiceTests
{
    private static Message M(string ts, bool fromMe, string service, int error = 0, string chat = "+15551234567") =>
        new() { Ts = ts, FromMe = fromMe, Service = service, Error = error, Chat = chat };

    [Fact]
    public void Empty_is_imessage() => Assert.Equal(SendService.IMessage, SendServiceSelector.For([]));

    [Fact]
    public void Failed_imessage_to_a_phone_switches_to_sms() =>
        Assert.Equal(SendService.SMS, SendServiceSelector.For([
            M("2026-01-01T00:00:00Z", false, "iMessage"),
            M("2026-01-01T00:01:00Z", true, "iMessage", error: 22)]));

    [Fact]
    public void Last_inbound_service_wins_over_outbound() =>
        Assert.Equal(SendService.RCS, SendServiceSelector.For([
            M("2026-01-01T00:00:00Z", false, "RCS"),
            M("2026-01-01T00:01:00Z", true, "iMessage")]));

    [Fact]
    public void Prefer_imessage_overrides_inbound_green() =>
        Assert.Equal(SendService.IMessage, SendServiceSelector.For([
            M("2026-01-01T00:00:00Z", false, "iMessage"),
            M("2026-01-01T00:01:00Z", false, "SMS")], preferIMessage: true));

    [Fact]
    public void No_inbound_uses_last_successful_outbound() =>
        Assert.Equal(SendService.SMS, SendServiceSelector.For([
            M("2026-01-01T00:00:00Z", true, "SMS"),
            M("2026-01-01T00:01:00Z", true, "iMessage", error: 4),
            M("2026-01-01T00:02:00Z", true, "SMS", error: 0) with { Chat = "a@b.c" }]));
}

public class ConversationListTests
{
    private static ChatInfo C(string id, string last, bool pinned = false, int? order = null, string[]? aliases = null, string? name = null) =>
        new() { Id = id, Last = last, Pinned = pinned, PinOrder = order, Aliases = aliases ?? [], Name = name, LastText = "x" };

    [Fact]
    public void Pinned_first_in_pin_order_then_newest()
    {
        var list = ConversationList.Build([
            C("+15550000001", "2026-01-03T00:00:00Z"),
            C("+15550000002", "2026-01-01T00:00:00Z", pinned: true, order: 1),
            C("+15550000003", "2026-01-02T00:00:00Z", pinned: true, order: 0),
            C("+15550000004", "2026-01-04T00:00:00Z")], null, null);
        Assert.Equal(["+15550000003", "+15550000002", "+15550000004", "+15550000001"], list.Select(c => c.Id));
    }

    [Fact]
    public void Unread_folds_through_aliases_and_group_gets_guid()
    {
        var chats = new[] { C("chat2", "2026-01-01T00:00:00Z", aliases: ["chat1"]) };
        var rs = new ReadStateSnapshot { Chats = [new ChatReadState { Chat = "chat1", Unread = 3 }] };
        var groups = new[] { new GroupInfo { Chat = "chat1", Guid = "any;+;chat1", Participants = ["+15550000001", "+15550000002"],
            ParticipantNames = new Dictionary<string, string> { ["+15550000001"] = "Jamie Rivera" } } };
        var c = Assert.Single(ConversationList.Build(chats, rs, groups));
        Assert.Equal(3, c.Unread);
        Assert.True(c.IsGroup);
        Assert.Equal("any;+;chat1", c.GroupGuid);
        Assert.True(c.CanSend);
        Assert.Equal("Jamie Rivera & (555) 000-0002", c.Title);
    }

    [Fact]
    public void Unknown_group_is_not_sendable()
    {
        var c = Assert.Single(ConversationList.Build([C("chat9", "2026-01-01T00:00:00Z")], null, null));
        Assert.False(c.CanSend);
    }

    [Fact]
    public void Real_fixtures_build_a_sidebar()
    {
        var t = new FakeTransport();
        var chats = System.Text.Json.JsonSerializer.Deserialize(FakeTransport.Json("chats.json").Stdout, BlipJson.Default.ListChatInfo)!;
        var rs = System.Text.Json.JsonSerializer.Deserialize(FakeTransport.Json("read-state.json").Stdout, BlipJson.Default.ReadStateSnapshot)!;
        var gs = System.Text.Json.JsonSerializer.Deserialize(FakeTransport.Json("groups.json").Stdout, BlipJson.Default.ListGroupInfo)!;
        var list = ConversationList.Build(chats, rs, gs);
        Assert.Equal(1, list[0].Unread);
        Assert.Equal("Trail Crew", list[1].Title);
        Assert.Equal("iMessage;+;chat900000000000000001", list[1].GroupGuid);
    }

    [Theory]
    [InlineData("RCS", true)]
    [InlineData("rcs", true)]
    [InlineData("SMS", false)]
    [InlineData("iMessage", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_rcs_draws_green(string? service, bool green) =>
        Assert.Equal(green, SendServiceSelector.IsGreen(service));
}
