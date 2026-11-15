using Blip.Core.Protocol;

namespace Blip.Core.Logic;

/// <summary>A sidebar row: <c>chats</c> joined with <c>read-state</c> and <c>groups</c>.</summary>
public sealed record Conversation
{
    public required string Id { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public required string Title { get; init; }
    public bool IsGroup { get; init; }
    /// <summary>AppleScript GUID for groups; empty for DMs (and for a group the Mac did not list: not sendable).</summary>
    public string GroupGuid { get; init; } = "";
    public IReadOnlyList<GroupParticipant> Participants { get; init; } = [];
    public string Service { get; init; } = "";
    public DateTimeOffset LastTime { get; init; }
    public string LastPreview { get; init; } = "";
    public bool LastFromMe { get; init; }
    public int Unread { get; init; }
    public bool Pinned { get; init; }
    public int? PinOrder { get; init; }
    public bool Muted { get; init; }

    public bool CanSend => !IsGroup || GroupGuid.Length > 0;
}

public sealed record GroupParticipant(string Handle, string Name);

public static class ConversationList
{
    public const int ChatListLimit = 300;

    /// <summary>
    /// Build the sidebar. Pinned first in Messages' pin order, then newest activity.
    /// Unread comes from the Mac's read-state (shared with every Apple client and
    /// the Linux client), folded through aliases so a re-keyed group counts once.
    /// </summary>
    public static IReadOnlyList<Conversation> Build(
        IReadOnlyList<ChatInfo> chats,
        ReadStateSnapshot? readState,
        IReadOnlyList<GroupInfo>? groups)
    {
        var unread = new Dictionary<string, int>(StringComparer.Ordinal);
        if (readState is not null)
            foreach (var r in readState.Chats)
                foreach (var id in r.Aliases.Prepend(r.Chat))
                    unread[id] = Math.Max(unread.GetValueOrDefault(id), r.Unread);

        var groupById = new Dictionary<string, GroupInfo>(StringComparer.Ordinal);
        foreach (var g in groups ?? []) groupById.TryAdd(g.Chat, g);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<Conversation>();
        foreach (var c in chats)
        {
            if (!seen.Add(c.Id)) continue;
            foreach (var a in c.Aliases) seen.Add(a);
            var isGroup = ChatIds.IsGroupChat(c.Id);
            var group = isGroup ? c.Aliases.Prepend(c.Id).Select(id => groupById.GetValueOrDefault(id)).FirstOrDefault(g => g is not null) : null;
            var participants = group is null ? [] : group.Participants
                .Select(h => new GroupParticipant(h,
                    group.ParticipantNames.GetValueOrDefault(h) is { Length: > 0 } n ? n : ChatIds.Pretty(h)))
                .ToList();
            list.Add(new Conversation
            {
                Id = c.Id,
                Aliases = c.Aliases,
                Title = TitleFor(c, group, participants),
                IsGroup = isGroup,
                GroupGuid = group?.Guid ?? "",
                Participants = participants,
                Service = c.Service,
                LastTime = BlipTime.Parse(c.Last),
                LastPreview = Preview(c),
                LastFromMe = c.LastFromMe,
                Unread = c.Aliases.Prepend(c.Id).Select(id => unread.GetValueOrDefault(id)).Max(),
                Pinned = c.Pinned,
                PinOrder = c.PinOrder,
                Muted = c.Muted,
            });
        }
        return list
            .OrderByDescending(c => c.Pinned)
            .ThenBy(c => c.Pinned ? c.PinOrder ?? int.MaxValue : 0)
            .ThenByDescending(c => c.LastTime)
            .ToList();
    }

    public static string TitleFor(ChatInfo c, GroupInfo? group, IReadOnlyList<GroupParticipant> participants)
    {
        if (!string.IsNullOrWhiteSpace(c.Name)) return c.Name!;
        if (group is { Name.Length: > 0 }) return group.Name;
        if (ChatIds.IsGroupChat(c.Id))
        {
            if (!string.IsNullOrWhiteSpace(c.PinName)) return c.PinName!;
            var names = participants.Select(p => group?.ParticipantShortNames.GetValueOrDefault(p.Handle) ?? p.Name).ToList();
            return names.Count switch
            {
                0 => "Group",
                1 => names[0],
                _ => string.Join(", ", names.Take(names.Count - 1)) + " & " + names[^1],
            };
        }
        if (!string.IsNullOrWhiteSpace(c.LastName)) return c.LastName!;  // _dm_name: resolved from the chat id, not the sender
        if (!string.IsNullOrWhiteSpace(c.PinName)) return c.PinName!;
        return ChatIds.Pretty(c.Id);
    }

    private static string Preview(ChatInfo c)
    {
        var text = c.LastText.Replace('\n', ' ').Replace("￼", "").Trim();
        if (text.Length == 0 && c.LastAttachment is { } a)
            text = a.Sticker == true ? "Sticker" : a.Mime.StartsWith("image/") ? "Photo" : a.Mime.StartsWith("audio/") ? "Audio message" : a.Name;
        return c.LastFromMe && text.Length > 0 ? "You: " + text : text;
    }
}
