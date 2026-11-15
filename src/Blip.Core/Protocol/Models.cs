using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blip.Core.Protocol;

// Wire shapes of the Mac bridge's JSON, as `imsg --json …` prints them (bridge
// claude-on-mac v2.0.0 + Blip extensions; see blip/bridge/BRIDGE-VERSION).
// Property names are snake_case on the wire; BlipJson maps them. Every field the
// bridge may omit on older versions is nullable, as collector.ts treats them.
// All timestamps are UTC ISO-8601 to the second ("2026-09-07T18:33:12Z").

/// <summary>One row of <c>imsg --json chats N</c>: the complete conversation list, newest first.</summary>
public sealed record ChatInfo
{
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public string Service { get; init; } = "";
    public int Messages { get; init; }
    public string Last { get; init; } = "";
    public string LastText { get; init; } = "";
    public bool LastFromMe { get; init; }
    public string LastHandle { get; init; } = "";
    public string? LastName { get; init; }
    public LastAttachment? LastAttachment { get; init; }
    public bool Pinned { get; init; }
    public int? PinOrder { get; init; }
    public string? PinName { get; init; }
    public bool Muted { get; init; }
    /// <summary>Other chat rows of this same conversation (re-keyed groups). Fold them onto <see cref="Id"/>.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public sealed record LastAttachment
{
    public string Name { get; init; } = "";
    public string Mime { get; init; } = "";
    public bool? Sticker { get; init; }
}

/// <summary>One row of <c>imsg --json groups</c>: what a sender needs for a group.</summary>
public sealed record GroupInfo
{
    public string Chat { get; init; } = "";
    /// <summary>Full AppleScript chat GUID. Group sends use <c>imsg-send --chat-id &lt;guid&gt;</c>, never the bare id.</summary>
    public string Guid { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Participants { get; init; } = [];
    public IReadOnlyDictionary<string, string> ParticipantNames { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ParticipantShortNames { get; init; } = new Dictionary<string, string>();
    public string? Last { get; init; }
}

/// <summary>One message from <c>imsg --json [--rich] thread|recent|search</c>.</summary>
public sealed record Message
{
    public long Id { get; init; }
    public string? Guid { get; init; }
    public string Ts { get; init; } = "";
    public string? ActivityTs { get; init; }
    public bool FromMe { get; init; }
    public bool? SelfChat { get; init; }
    public string? Handle { get; init; }
    public string? Name { get; init; }
    public string Service { get; init; } = "";
    public string Chat { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>Apple-side read (chat.db is_read, phone-synced). Own messages are always true.</summary>
    public bool? Read { get; init; }
    /// <summary>A tapback row. --rich folds these onto their targets; clients skip any that remain.</summary>
    public bool? Tapback { get; init; }
    /// <summary>Delivery failure on own messages (0 = fine). AppleScript reports success for sends that die later.</summary>
    public int? Error { get; init; }
    /// <summary>A Send Later message still waiting on the Mac; its ts is the future send time.</summary>
    public bool? Scheduled { get; init; }

    // --rich extras
    public string? ReadAt { get; init; }
    public bool? Delivered { get; init; }
    public IReadOnlyList<Tapback>? Tapbacks { get; init; }
    public IReadOnlyList<AttachmentMeta>? Attachments { get; init; }
    public JsonElement? Stickers { get; init; }
    public ReplyTo? ReplyTo { get; init; }
    public bool? Edited { get; init; }
    public bool? Retracted { get; init; }
    public string? Effect { get; init; }
    public bool? Audio { get; init; }
    public LinkCard? Link { get; init; }

    [JsonIgnore] public DateTimeOffset Time => BlipTime.Parse(Ts);
    [JsonIgnore] public bool Failed => FromMe && Error is { } e && e != 0;
}

public sealed record Tapback
{
    public string Emoji { get; init; } = "";
    public bool FromMe { get; init; }
    public string? By { get; init; }
    public string? Kind { get; init; }
}

/// <summary>Attachment metadata only; the bytes stay on the Mac until fetched by <see cref="Id"/>.</summary>
public sealed record AttachmentMeta
{
    /// <summary>attachment ROWID as a string (64-bit safe).</summary>
    public string? Id { get; init; }
    public string Name { get; init; } = "";
    public string? Mime { get; init; }
    public long? Bytes { get; init; }
    public JsonElement? Sticker { get; init; }

    [JsonIgnore] public bool IsImage => Mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    [JsonIgnore] public bool IsAudio => Mime?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed record ReplyTo
{
    public string Text { get; init; } = "";
    public bool FromMe { get; init; }
    public AttachmentMeta? Attachment { get; init; }
}

public sealed record LinkCard
{
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string ImageId { get; init; } = "";
}

/// <summary><c>imsg --json read-state</c>: metadata-only unread snapshot from the Mac.</summary>
public sealed record ReadStateSnapshot
{
    public int Version { get; init; }
    public IReadOnlyList<ChatReadState> Chats { get; init; } = [];
}

public sealed record ChatReadState
{
    public string Chat { get; init; } = "";
    public int Unread { get; init; }
    public string Oldest { get; init; } = "";
    public string Latest { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

/// <summary><c>imsg --json session</c>: is anyone logged in at the Mac's screen (sends need it).</summary>
public sealed record SessionInfo
{
    public int Version { get; init; }
    public bool? Gui { get; init; }
}

public static class BlipTime
{
    public static DateTimeOffset Parse(string ts) =>
        DateTimeOffset.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;
}
