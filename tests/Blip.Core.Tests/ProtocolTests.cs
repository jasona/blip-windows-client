using System.Text.Json;
using Blip.Core.Protocol;

namespace Blip.Core.Tests;

/// <summary>Contract tests: the fixtures are real output of bridge/mac/imsg run against a synthetic chat.db.</summary>
public class ProtocolTests
{
    private static T Load<T>(string name, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> t) =>
        JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), t)!;

    [Fact]
    public void Chats_parse_with_aliases_and_pin_fields()
    {
        var chats = Load("chats.json", BlipJson.Default.ListChatInfo);
        Assert.Equal(2, chats.Count);
        var dm = chats[0];
        Assert.Equal("+15550100011", dm.Id);
        Assert.Equal("unread one", dm.LastText);
        Assert.False(dm.Pinned);
        Assert.Null(dm.PinOrder);
        Assert.Empty(dm.Aliases);
        Assert.Equal("Trail Crew", chats[1].Name);
    }

    [Fact]
    public void Groups_carry_the_applescript_guid()
    {
        var g = Assert.Single(Load("groups.json", BlipJson.Default.ListGroupInfo));
        Assert.Equal("chat900000000000000001", g.Chat);
        Assert.Equal("iMessage;+;chat900000000000000001", g.Guid);
        Assert.Equal(2, g.Participants.Count);
    }

    [Fact]
    public void Rich_thread_folds_tapbacks_and_lists_attachments()
    {
        var msgs = Load("thread-dm.json", BlipJson.Default.ListMessage);
        Assert.Equal(3, msgs.Count);   // the "Loved" row is folded onto its target, not listed
        var withPhoto = msgs[0];
        var att = Assert.Single(withPhoto.Attachments!);
        Assert.Equal("30", att.Id);
        Assert.True(att.IsImage);
        var mine = msgs[1];
        Assert.True(mine.FromMe);
        Assert.True(mine.Delivered);
        var tb = Assert.Single(mine.Tapbacks!);
        Assert.Equal("❤️", tb.Emoji);
        Assert.False(tb.FromMe);
        Assert.False(msgs[2].Read);
        Assert.Equal("g12", msgs[2].Guid);
        Assert.Equal(new DateTimeOffset(2026, 5, 9, 6, 15, 20, TimeSpan.Zero), msgs[2].Time);
    }

    [Fact]
    public void Read_state_and_session_parse()
    {
        var rs = Load("read-state.json", BlipJson.Default.ReadStateSnapshot);
        Assert.Equal(1, rs.Version);
        Assert.Equal(1, rs.Chats.Single(c => c.Chat == "+15550100011").Unread);
        var s = Load("session.json", BlipJson.Default.SessionInfo);
        Assert.Null(s.Gui);   // "could not tell" is a real answer, not false
    }
}
