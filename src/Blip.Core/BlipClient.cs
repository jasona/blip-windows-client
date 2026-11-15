using System.Text;
using System.Text.Json;
using Blip.Core.Config;
using Blip.Core.Logic;
using Blip.Core.Protocol;
using Blip.Core.Transport;

namespace Blip.Core;

public sealed record ActionResult(bool Ok, bool Online, string Error)
{
    public static readonly ActionResult Success = new(true, true, "");
    public static ActionResult Fail(string error, bool online = true) => new(false, online, error);
}

public enum TapbackKind { Love, Like, Dislike, Laugh, Emphasize, Question }

/// <summary>
/// The typed face of the Mac bridge. Every call maps to one bridge tool invocation
/// with the same argv the Linux client uses, so the Mac sees nothing new.
/// </summary>
/// <remarks>
/// Invariants carried over from Blip (AGENTS.md, docs/SECURITY.md):
/// message text, captions, search queries and reply bodies travel on stdin, never argv;
/// nothing here writes message text to disk; every send passes <c>--yes</c> only
/// because the person pressed Send on that one message.
/// </remarks>
public sealed class BlipClient(IBridgeTransport transport, BridgeConfig config)
{
    public const int MaxTextChars = 4000;               // imsg-send refuses longer
    public const long MaxFileBytes = 100L * 1024 * 1024; // the Mac's ceiling both ways

    public BridgeConfig Config { get; } = config;

    /// <summary>The shim injects these on every imsg call; so does blip-bridged on the serve channel.</summary>
    private List<string> ImsgArgs(params string[] args)
    {
        var a = new List<string>(args.Length + 2);
        a.AddRange(args);
        if (Config.HideSpam) a.Add("--hide-spam");
        if (Config.HideUnknown) a.Add("--hide-unknown");
        return a;
    }

    private async Task<T> QueryJsonAsync<T>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,
        IReadOnlyList<string> args, string? stdin, CancellationToken ct)
    {
        var r = await transport.QueryAsync(args, stdin, ct).ConfigureAwait(false);
        if (r.Offline) throw new BridgeOfflineException(r.Reason);
        if (!r.Ok) throw new BridgeException(r.Status, $"imsg {args.FirstOrDefault(a => !a.StartsWith('-'))}: {r.Reason}");
        return JsonSerializer.Deserialize(r.Stdout, type)
               ?? throw new BridgeException(BridgeExit.TempFail, "empty reply from the bridge");
    }

    // ------------------------------------------------------------ reads

    public async Task<IReadOnlyList<ChatInfo>> ChatsAsync(int n = ConversationList.ChatListLimit, CancellationToken ct = default) =>
        await QueryJsonAsync(BlipJson.Default.ListChatInfo, ImsgArgs("--json", "chats", n.ToString()), null, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<GroupInfo>> GroupsAsync(CancellationToken ct = default) =>
        await QueryJsonAsync(BlipJson.Default.ListGroupInfo, ImsgArgs("--json", "groups"), null, ct).ConfigureAwait(false);

    public Task<ReadStateSnapshot> ReadStateAsync(CancellationToken ct = default) =>
        QueryJsonAsync(BlipJson.Default.ReadStateSnapshot, ImsgArgs("--json", "read-state"), null, ct);

    /// <summary>Whether someone is logged in at the Mac's screen. Reads work either way; sends do not.</summary>
    public Task<SessionInfo> SessionAsync(CancellationToken ct = default) =>
        QueryJsonAsync(BlipJson.Default.SessionInfo, ["--json", "session"], null, ct);

    /// <summary>The sidebar's raw inputs: chats + read-state + groups, fetched in parallel (the serve pool has two channels).</summary>
    public async Task<SidebarData> SidebarAsync(CancellationToken ct = default)
    {
        var chats = ChatsAsync(ct: ct);
        var read = ReadStateAsync(ct);
        var groups = GroupsAsync(ct);
        await Task.WhenAll(chats, read, groups).ConfigureAwait(false);
        return new SidebarData(chats.Result, read.Result, groups.Result);
    }

    /// <summary>The sidebar, built.</summary>
    public async Task<IReadOnlyList<Conversation>> ConversationsAsync(CancellationToken ct = default) =>
        (await SidebarAsync(ct).ConfigureAwait(false)).Build();

    /// <summary>One conversation, oldest → newest, with tapbacks, replies, attachments and receipts folded in.</summary>
    public async Task<IReadOnlyList<Message>> ThreadAsync(string chat, int limit = 80, CancellationToken ct = default)
    {
        RequireValidChat(chat);
        var msgs = await QueryJsonAsync(BlipJson.Default.ListMessage,
            ImsgArgs("--json", "--rich", "thread", "--chat", chat, limit.ToString()), null, ct).ConfigureAwait(false);
        return msgs.Where(m => m.Tapback != true).OrderBy(m => m.Ts, StringComparer.Ordinal).ThenBy(m => m.Id).ToList();
    }

    /// <summary>Full-text search. The query is message text, so it rides stdin.</summary>
    public async Task<IReadOnlyList<Message>> SearchAsync(string query, int n = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return await QueryJsonAsync(BlipJson.Default.ListMessage,
            ImsgArgs("--json", "search", "--stdin", n.ToString()), query, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ sends

    /// <summary>Send one text message. The caller must only call this for a message the person sent.</summary>
    public async Task<ActionResult> SendTextAsync(Conversation conv, string text, SendService service = SendService.IMessage,
        CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) return ActionResult.Fail("message is empty");
        if (text.Length > MaxTextChars) return ActionResult.Fail($"message is longer than {MaxTextChars} characters");
        var target = Target(conv, service);
        if (target.Error is { } err) return ActionResult.Fail(err);
        // --keep-dashes: the person's punctuation is theirs (upstream scrubs em dashes by default).
        var args = target.Args.Concat(["--yes", "--text-stdin", "--keep-dashes"]).ToList();
        return Outcome(await RunAsync(BridgeTool.ImsgSend, args, Encoding.UTF8.GetBytes(text), TimeSpan.FromSeconds(90), ct).ConfigureAwait(false));
    }

    /// <summary>Send a file (≤100 MB) with an optional caption. Bytes cross on stdin, never as a Mac path.</summary>
    public async Task<ActionResult> SendFileAsync(Conversation conv, string path, string caption = "",
        SendService service = SendService.IMessage, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return ActionResult.Fail($"no such file: {info.Name}");
        if (info.Length == 0) return ActionResult.Fail("file is empty");
        if (info.Length > MaxFileBytes) return ActionResult.Fail("file is larger than 100 MB");
        var target = Target(conv, service);
        if (target.Error is { } err) return ActionResult.Fail(err);

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        if (bytes.LongLength != info.Length) return ActionResult.Fail("file changed while it was being read");
        var cap = Encoding.UTF8.GetBytes(caption.Trim());
        var args = target.Args.Concat([
            "--file-stdin", "--name", info.Name, "--yes",
            "--file-bytes", bytes.Length.ToString(),   // the Mac refuses a short stream instead of sending a truncated file
            "--keep-dashes",
        ]).ToList();
        byte[] stdin = bytes;
        if (cap.Length > 0)
        {
            args.AddRange(["--text-stdin-bytes", cap.Length.ToString()]);
            stdin = [.. cap, .. bytes];
        }
        return Outcome(await RunAsync(BridgeTool.ImsgSend, args, stdin, TimeSpan.FromMinutes(3), ct).ConfigureAwait(false));
    }

    /// <summary>Native threaded reply (opt-in: replies=on). Drives Messages through Accessibility.</summary>
    public async Task<ActionResult> ReplyAsync(Message to, string text, CancellationToken ct = default)
    {
        if (!Config.Replies) return ActionResult.Fail("replies are off: set replies=on");
        if (string.IsNullOrEmpty(to.Guid)) return ActionResult.Fail("this message has no GUID");
        text = text.Trim();
        if (text.Length == 0) return ActionResult.Fail("reply is empty");
        return Outcome(await RunAsync(BridgeTool.ImsgReply, ["--guid", to.Guid, "--yes", "--text-stdin"],
            Encoding.UTF8.GetBytes(text), TimeSpan.FromSeconds(90), ct).ConfigureAwait(false));
    }

    /// <summary>Add or remove a classic tapback (opt-in: tapbacks=on).</summary>
    public async Task<ActionResult> ReactAsync(Message to, TapbackKind kind, bool remove, CancellationToken ct = default)
    {
        if (!Config.Tapbacks) return ActionResult.Fail("tapbacks are off: set tapbacks=on");
        if (string.IsNullOrEmpty(to.Guid)) return ActionResult.Fail("this message has no GUID");
        var args = new List<string> { "--guid", to.Guid, kind.ToString().ToLowerInvariant() };
        if (remove) args.Add("--remove");
        args.Add("--yes");
        return Outcome(await RunAsync(BridgeTool.ImsgReact, args, default, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Mark one conversation read on the Mac (syncs to the iPhone like any read).
    /// <paramref name="through"/> rejects a stale read when a newer inbound has arrived.
    /// This brings Messages to the front on the Mac, so the UI only calls it when push_read=on.
    /// </summary>
    public async Task<ActionResult> MarkReadAsync(string chat, string? through = null, CancellationToken ct = default)
    {
        RequireValidChat(chat);
        var args = new List<string> { "--chat", chat };
        if (!string.IsNullOrEmpty(through)) args.AddRange(["--through", through]);
        return Outcome(await RunAsync(BridgeTool.ImsgRead, args, default, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false));
    }

    /// <summary>Mark everything read. Leaves the Mac alone: no window raised, no selection change.</summary>
    public async Task<ActionResult> MarkAllReadAsync(CancellationToken ct = default) =>
        Outcome(await RunAsync(BridgeTool.ImsgRead, ["--all"], default, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false));

    // ------------------------------------------------------------ binary fetches

    /// <summary>Stream one attachment. <paramref name="previewMaxDim"/> &gt; 0 asks the Mac for a JPEG
    /// (HEIC converted by sips) resampled to that long edge, as the Linux client does for inline images.</summary>
    /// <param name="convertJpeg">Full-size JPEG of an image (HEIC needs a codec Windows lacks by default).</param>
    public async Task<ActionResult> FetchAttachmentAsync(string id, Stream destination, int previewMaxDim = 0,
        bool png = false, bool convertJpeg = false, CancellationToken ct = default)
    {
        if (!id.All(char.IsAsciiDigit) || id.Length is 0 or > 18) return ActionResult.Fail("bad attachment id");
        var args = new List<string> { "attachment", id };
        if (previewMaxDim > 0 || convertJpeg || png) args.Add(png ? "--png" : "--jpeg");
        if (previewMaxDim > 0) args.AddRange(["--max-dim", previewMaxDim.ToString()]);
        var r = await transport.StreamAsync(BridgeTool.Imsg, args, destination, MaxFileBytes, ct).ConfigureAwait(false);
        return Outcome(r);
    }

    /// <summary>A contact photo (DM) or a group's own photo, as JPEG; null when there is none.</summary>
    public async Task<byte[]?> AvatarAsync(string chatOrHandle, CancellationToken ct = default) =>
        (await AvatarLookupAsync(chatOrHandle, ct).ConfigureAwait(false)).Bytes;

    /// <summary>
    /// A contact or group photo, telling "the Mac has none" (<c>imsg avatar</c> exits 1:
    /// <see cref="AvatarLookup.Known"/> with null bytes, safe to cache) apart from "could not ask"
    /// (any other status: not known, do not cache).
    /// </summary>
    public async Task<AvatarLookup> AvatarLookupAsync(string chatOrHandle, CancellationToken ct = default)
    {
        RequireValidChat(chatOrHandle);
        string[] args = ChatIds.IsGroupChat(chatOrHandle) ? ["avatar", "--chat", chatOrHandle] : ["avatar", "--", chatOrHandle];
        using var ms = new MemoryStream();
        var r = await transport.StreamAsync(BridgeTool.Imsg, args, ms, 8 << 20, ct).ConfigureAwait(false);
        if (r.Ok && ms.Length > 0) return new AvatarLookup(true, ms.ToArray());
        return new AvatarLookup(r.Status is 0 or 1, null);
    }

    // ------------------------------------------------------------ helpers

    private sealed record SendTarget(IReadOnlyList<string> Args, string? Error);

    /// <summary>collector.ts resolveTarget: --chat-id GUID for a group, --to id (+service) for a DM.</summary>
    private static SendTarget Target(Conversation conv, SendService service)
    {
        if (conv.IsGroup)
            return conv.GroupGuid.Length == 0
                ? new SendTarget([], "group id unknown: refusing to send")
                : new SendTarget(["--chat-id", conv.GroupGuid], null);
        if (!ChatIds.IsValid(conv.Id)) return new SendTarget([], "not a sendable address");
        // Green threads go out on their own service, never silently as iMessage.
        return service == SendService.IMessage
            ? new SendTarget(["--to", conv.Id], null)
            : new SendTarget(["--to", conv.Id, "--service", service.Wire()], null);
    }

    private async Task<BridgeResult> RunAsync(BridgeTool tool, IReadOnlyList<string> args, ReadOnlyMemory<byte> stdin,
        TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            return await transport.RunAsync(tool, args, stdin, timeout, ct).ConfigureAwait(false);
        }
        catch (BridgeOfflineException e)
        {
            return new BridgeResult(BridgeExit.Unavailable, [], e.Message);
        }
    }

    private static ActionResult Outcome(BridgeResult r) => r.Status switch
    {
        BridgeExit.Ok => ActionResult.Success,
        BridgeExit.Unavailable => ActionResult.Fail("Mac unreachable", online: false),
        BridgeExit.NoPerm => ActionResult.Fail("the Mac needs Accessibility for sshd-keygen-wrapper (run blip-check on the Mac)"),
        _ => ActionResult.Fail(r.Reason.Length > 240 ? r.Reason[..240] : r.Reason),
    };

    private static void RequireValidChat(string chat)
    {
        if (!ChatIds.IsValid(chat)) throw new ArgumentException("not a chat id the bridge accepts", nameof(chat));
    }
}

/// <summary>The three reads behind the sidebar, kept raw so they can be snapshotted and rebuilt.</summary>
public sealed record SidebarData(IReadOnlyList<ChatInfo> Chats, ReadStateSnapshot ReadState, IReadOnlyList<GroupInfo> Groups)
{
    public IReadOnlyList<Conversation> Build() => ConversationList.Build(Chats, ReadState, Groups);
}

/// <param name="Known">The Mac answered: <paramref name="Bytes"/> is the photo, or null for none.</param>
public readonly record struct AvatarLookup(bool Known, byte[]? Bytes);
