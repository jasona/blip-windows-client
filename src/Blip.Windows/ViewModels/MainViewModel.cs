using System.Collections.ObjectModel;
using System.Threading.Channels;
using Avalonia.Media.Imaging;
using Blip.Core;
using Blip.Core.Cache;
using Blip.Core.Live;
using Blip.Core.Logic;
using Blip.Core.Protocol;
using Blip.Core.Transport;
using Blip.Windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Blip.Windows.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int ThreadLimit = 80;
    private const int AvatarWorkers = 2;      // photos queue behind thread loads and image previews
    private const int PrefetchThreads = 8;    // newest conversations warmed after the first live load
    private readonly BlipSession _session;
    private readonly ReadMarks _marks = new();
    private readonly Dictionary<string, DateTimeOffset> _notified = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly Dictionary<string, Bitmap?> _avatars = new(StringComparer.Ordinal);
    private bool _firstLoad = true;
    private int _reloadQueued;
    private IReadOnlyList<Message> _threadModels = [];

    // Last data the Mac returned, kept so a conversation opens instantly and so it can be snapshotted.
    private readonly object _dataLock = new();
    private readonly Dictionary<string, IReadOnlyList<Message>> _threadCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveThreads = new(StringComparer.Ordinal);
    private SidebarData? _sidebar;
    private IReadOnlyList<Conversation> _lastConversations = [];
    private int _saveQueued;
    private bool _prefetched;
    private readonly Channel<ConversationViewModel> _avatarQueue = Channel.CreateUnbounded<ConversationViewModel>();

    public ObservableCollection<ConversationViewModel> Conversations { get; } = [];
    /// <summary>What the sidebar shows: <see cref="Conversations"/> through the search filter.</summary>
    public ObservableCollection<ConversationViewModel> VisibleConversations { get; } = [];
    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public ObservableCollection<string> PendingFiles { get; } = [];

    [ObservableProperty] private ConversationViewModel? _selected;
    [ObservableProperty] private string _composerText = "";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private BridgeStatus _status = BridgeStatus.Connecting;
    [ObservableProperty] private string _statusText = "Connecting to your Mac…";
    [ObservableProperty] private string _banner = "";
    [ObservableProperty] private string _sendService = "iMessage";
    [ObservableProperty] private MessageViewModel? _replyTarget;
    [ObservableProperty] private bool _isLoadingThread;
    [ObservableProperty] private string _newRecipient = "";

    /// <summary>The window tells us whether a toast is needed (not focused, or another conversation open).</summary>
    public Func<bool> IsWindowActive { get; set; } = () => true;
    /// <summary>Set by the window: the clipboard belongs to the top-level.</summary>
    public Func<string, Task>? SetClipboard { get; set; }
    /// <summary>Raised after the open thread changed, so the view can keep the scroll at the bottom.</summary>
    public event Action? ThreadUpdated;

    public bool TapbacksEnabled => _session.Config.Tapbacks;
    public bool RepliesEnabled => _session.Config.Replies;
    public int UnreadTotal => Conversations.Count(c => c.IsUnread);
    public bool HasBanner => Banner.Length > 0;
    public bool HasReplyTarget => ReplyTarget is not null;
    public bool CanCompose => Selected?.Model.CanSend == true;

    public MainViewModel(BlipSession session)
    {
        _session = session;
        Conversations.CollectionChanged += (_, _) => RefreshVisible();
        session.Monitor.Changed += () => _ = ReloadAsync();
        session.Monitor.StatusChanged += (s, d) => Ui.InvokeAsync(() => OnStatus(s, d));
    }

    public void Start()
    {
        LoadSnapshot();
        for (var i = 0; i < AvatarWorkers; i++) _ = Task.Run(AvatarWorkerAsync);
        _session.Monitor.Start();
        _ = ReloadAsync();
    }

    // ------------------------------------------------------------ snapshot (local_cache)

    /// <summary>Paint last session's sidebar before the Mac answers. Live data replaces it moments later.</summary>
    private void LoadSnapshot()
    {
        if (_session.Snapshots is not { } store) return;
        var snap = store.Load(SnapshotStore.KeyFor(_session.Config));
        if (snap is null) return;
        var data = new SidebarData(snap.Chats, snap.ReadState ?? new ReadStateSnapshot(), snap.Groups);
        lock (_dataLock)
        {
            _sidebar = data;
            foreach (var (chat, msgs) in snap.Threads) _threadCache[chat] = msgs;
        }
        ApplyConversations(data.Build(), live: false);
        StatusText = "Showing your last session while connecting to your Mac…";
    }

    private void ScheduleSave()
    {
        if (_session.Snapshots is null || Interlocked.Exchange(ref _saveQueued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            Interlocked.Exchange(ref _saveQueued, 0);
            SaveSnapshot();
        });
    }

    /// <summary>Write the snapshot now (also called on quit). Only data the Mac returned; never a pending bubble.</summary>
    public void SaveSnapshot()
    {
        if (_session.Snapshots is not { } store) return;
        Snapshot snap;
        lock (_dataLock)
        {
            if (_sidebar is null) return;
            var threads = new Dictionary<string, List<Message>>(StringComparer.Ordinal);
            foreach (var c in _lastConversations)
            {
                if (threads.Count >= SnapshotStore.MaxThreads) break;
                if (_threadCache.TryGetValue(c.Id, out var msgs)) threads[c.Id] = msgs.ToList();
            }
            snap = new Snapshot
            {
                Key = SnapshotStore.KeyFor(_session.Config),
                SavedAt = DateTimeOffset.UtcNow,
                Chats = _sidebar.Chats.ToList(),
                ReadState = _sidebar.ReadState,
                Groups = _sidebar.Groups.ToList(),
                Threads = threads,
            };
        }
        try { store.Save(snap); } catch { /* a snapshot is a convenience; the Mac is the source */ }
    }

    /// <summary>After the first live sidebar, fetch the newest few threads one at a time so opening them is instant.</summary>
    private async Task PrefetchThreadsAsync(IReadOnlyList<Conversation> convs)
    {
        foreach (var c in convs.Where(c => ChatIds.IsValid(c.Id) && c.LastTime != DateTimeOffset.MinValue).Take(PrefetchThreads))
        {
            lock (_dataLock) if (_liveThreads.Contains(c.Id)) continue;
            try
            {
                var msgs = await _session.Client.ThreadAsync(c.Id, ThreadLimit);
                RememberThread(c.Id, msgs);
            }
            catch (BridgeOfflineException) { return; }
            catch (Exception e) when (e is BridgeException or ArgumentException) { }
        }
    }

    private void RememberThread(string chat, IReadOnlyList<Message> msgs)
    {
        lock (_dataLock)
        {
            _threadCache[chat] = msgs;
            _liveThreads.Add(chat);
        }
        ScheduleSave();
    }

    // ------------------------------------------------------------ avatars

    /// <summary>Faces from disk first (no Mac round trip), then fetch only what is missing or over a day old.</summary>
    private void QueueAvatars(IReadOnlyList<ConversationViewModel> fresh) => _ = Task.Run(() =>
    {
        foreach (var vm in fresh)
        {
            var cached = _session.Avatars.TryGet(vm.Id);
            if (cached?.Bytes is { } b) ShowAvatar(vm, b);
            if (cached is not { Fresh: true }) _avatarQueue.Writer.TryWrite(vm);
        }
    });

    private async Task AvatarWorkerAsync()
    {
        await foreach (var vm in _avatarQueue.Reader.ReadAllAsync())
        {
            try
            {
                var r = await _session.Client.AvatarLookupAsync(vm.Id);
                if (!r.Known) continue;
                var before = _session.Avatars.TryGet(vm.Id);
                _session.Avatars.Store(vm.Id, r.Bytes);
                if (r.Bytes is { } b && (before?.Bytes is not { } old || !old.AsSpan().SequenceEqual(b))) ShowAvatar(vm, b);
            }
            catch (BridgeOfflineException)
            {
                // Try again on the next sidebar load once the Mac is back.
                await Ui.InvokeAsync(() => { if (_avatars.TryGetValue(vm.Id, out var f) && f is null) _avatars.Remove(vm.Id); });
            }
            catch { /* initials stay */ }
        }
    }

    private void ShowAvatar(ConversationViewModel vm, byte[] bytes)
    {
        Bitmap bmp;
        try { bmp = Bitmap.DecodeToWidth(new MemoryStream(bytes), 96); }
        catch { return; }   // a photo we cannot decode: initials stay
        Ui.Post(() =>
        {
            _avatars[vm.Id] = bmp;
            vm.Avatar = bmp;
        });
    }

    partial void OnFilterChanged(string value) => RefreshVisible();

    private bool Matches(ConversationViewModel c) => Filter.Length == 0
        || c.Title.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)
        || c.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    private void RefreshVisible() => Sync(VisibleConversations, Conversations.Where(Matches).ToList());
    partial void OnBannerChanged(string value) => OnPropertyChanged(nameof(HasBanner));
    partial void OnReplyTargetChanged(MessageViewModel? value) => OnPropertyChanged(nameof(HasReplyTarget));

    private void OnStatus(BridgeStatus s, string detail)
    {
        Status = s;
        StatusText = s switch
        {
            BridgeStatus.Online => "Connected",
            BridgeStatus.Connecting => "Connecting to your Mac…",
            BridgeStatus.Offline => "Mac unreachable" + (detail.Length > 0 ? $": {detail}" : ""),
            _ => detail,
        };
    }

    // ------------------------------------------------------------ loading

    /// <summary>Reload the list and the open thread. A burst of pushes collapses into at most one extra pass.</summary>
    public async Task ReloadAsync()
    {
        Interlocked.Exchange(ref _reloadQueued, 1);
        if (!await _reloadGate.WaitAsync(0)) return;   // the running pass will see the flag
        try
        {
            while (Interlocked.Exchange(ref _reloadQueued, 0) == 1)
                await ReloadOnceAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
        // A push that landed between the last pass and Release would otherwise be lost.
        if (Volatile.Read(ref _reloadQueued) == 1) _ = ReloadAsync();
    }

    private async Task ReloadOnceAsync()
    {
        try
        {
            var data = await _session.Client.SidebarAsync();
            var convs = data.Build();
            lock (_dataLock) _sidebar = data;
            await Ui.InvokeAsync(() => ApplyConversations(convs, live: true));
            ScheduleSave();
            if (Selected is { } sel) await LoadThreadAsync(sel, quiet: true);
            if (!_prefetched) { _prefetched = true; _ = PrefetchThreadsAsync(convs); }
            await CheckSessionAsync();
        }
        catch (BridgeOfflineException) { /* the monitor reports status */ }
        catch (BridgeException e)
        {
            await Ui.InvokeAsync(() => Banner = e.Message);
        }
    }

    private async Task CheckSessionAsync()
    {
        try
        {
            var s = await _session.Client.SessionAsync();
            var text = s.Gui == false ? "Nobody is logged in at the Mac's screen: reading works, sending will fail until someone logs in." : "";
            await Ui.InvokeAsync(() => { if (!Banner.StartsWith("the Mac's host key")) Banner = text; });
        }
        catch { /* advisory only */ }
    }

    private void ApplyConversations(IReadOnlyList<Conversation> convs, bool live)
    {
        lock (_dataLock) _lastConversations = convs;
        var needFaces = new List<ConversationViewModel>();
        var byId = Conversations.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var next = new List<ConversationViewModel>(convs.Count);
        foreach (var c in convs)
        {
            if (byId.TryGetValue(c.Id, out var vm)) vm.Model = c;
            else vm = new ConversationViewModel(c);
            vm.LocallySeen = _marks.IsSeen(c.Id, c.LastTime);
            next.Add(vm);
            if (_avatars.TryGetValue(c.Id, out var face)) { if (face is not null) vm.Avatar = face; }
            else { _avatars[c.Id] = null; needFaces.Add(vm); }
        }
        // Keep a conversation started from "New message" until the Mac lists it.
        if (Selected is { } sel && !convs.Any(c => c.Id == sel.Id)) next.Insert(0, sel);
        Sync(Conversations, next);
        OnPropertyChanged(nameof(UnreadTotal));
        if (needFaces.Count > 0) QueueAvatars(needFaces);
        if (!live) return;   // a snapshot never raises toasts, and the first live load stays quiet too
        Notify(convs);
        _firstLoad = false;
    }

    /// <summary>Move/insert/remove in place so selection and scroll survive a reload.</summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> source) where T : class
    {
        for (var i = 0; i < source.Count; i++)
        {
            var at = target.IndexOf(source[i]);
            if (at == i) continue;
            if (at >= 0) target.Move(at, i);
            else target.Insert(i, source[i]);
        }
        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }

    private void Notify(IReadOnlyList<Conversation> convs)
    {
        foreach (var c in convs)
        {
            var seen = _notified.GetValueOrDefault(c.Id);
            _notified[c.Id] = c.LastTime;
            if (_firstLoad || c.Unread == 0 || c.LastFromMe || c.Muted || c.LastTime <= seen) continue;
            if (IsWindowActive() && Selected?.Id == c.Id) continue;
            Notifier.Show(c.Id, c.Title, c.LastPreview);
        }
    }

    partial void OnSelectedChanged(ConversationViewModel? value)
    {
        Messages.Clear();
        PendingFiles.Clear();
        ReplyTarget = null;
        _threadModels = [];
        OnPropertyChanged(nameof(CanCompose));
        if (value is null) return;
        Notifier.Clear(value.Id);
        IReadOnlyList<Message>? cached;
        lock (_dataLock) _threadCache.TryGetValue(value.Id, out cached);
        if (cached is not null) ApplyThread(value, cached);   // instant; the live copy replaces it below
        _ = LoadThreadAsync(value, quiet: cached is not null);
    }

    private async Task LoadThreadAsync(ConversationViewModel conv, bool quiet)
    {
        if (!quiet) IsLoadingThread = true;
        try
        {
            IReadOnlyList<Message> msgs = ChatIds.IsValid(conv.Id) && conv.Model.LastTime != DateTimeOffset.MinValue
                ? await _session.Client.ThreadAsync(conv.Id, ThreadLimit)
                : [];
            if (msgs.Count > 0) RememberThread(conv.Id, msgs);
            await Ui.InvokeAsync(() =>
            {
                if (Selected != conv) return;
                ApplyThread(conv, msgs);
            });
            await MarkSeenAsync(conv, msgs);
        }
        catch (Exception e) when (e is BridgeException or ArgumentException)
        {
            if (!quiet) await Ui.InvokeAsync(() => Banner = e.Message);
        }
        finally
        {
            if (!quiet) IsLoadingThread = false;
        }
    }

    private void ApplyThread(ConversationViewModel conv, IReadOnlyList<Message> msgs)
    {
        _threadModels = msgs;
        var existing = new Dictionary<string, MessageViewModel>(StringComparer.Ordinal);
        foreach (var m in Messages.Where(m => !m.Key.StartsWith("pending:"))) existing.TryAdd(m.Key, m);
        var pending = Messages.Where(m => m.Key.StartsWith("pending:")).ToList();
        var next = new List<MessageViewModel>(msgs.Count + pending.Count);
        foreach (var m in msgs)
        {
            var key = m.Guid is { Length: > 0 } g ? g : m.Id.ToString();
            next.Add(existing.TryGetValue(key, out var vm) && vm.SameAs(m)
                ? vm
                : new MessageViewModel(m, _session.Client, _session.Cache, conv.Model.IsGroup) { Owner = this });
        }
        // An optimistic bubble disappears once the Mac lists a sent message with its text.
        foreach (var p in pending)
        {
            var landed = msgs.Any(m => m.FromMe && m.Time >= p.Model.Time.AddMinutes(-1) && m.Text.Trim() == p.Model.Text.Trim());
            if (!landed) next.Add(p);
        }
        Decorate(next);
        Sync(Messages, next);
        SendService = conv.Model.IsGroup ? "iMessage" : SendServiceSelector.For(msgs, _session.Config.PreferIMessage).Wire();
        ThreadUpdated?.Invoke();
    }

    /// <summary>Day headers, sender names in groups, and the single "Read/Delivered" line under the newest own message.</summary>
    private static void Decorate(List<MessageViewModel> list)
    {
        DateTimeOffset prev = DateTimeOffset.MinValue;
        string? prevSender = null;
        foreach (var m in list)
        {
            var t = m.Model.Time;
            m.DayHeader = (t - prev).TotalMinutes > 60 ? Formatting.DayHeader(t) : "";
            m.ShowSender = m.IsGroup && !m.IsMine && (m.Model.Handle != prevSender || m.DayHeader.Length > 0);
            prev = t;
            prevSender = m.IsMine ? null : m.Model.Handle;
            m.Receipt = "";
        }
        var last = list.LastOrDefault(m => m.IsMine && !m.Pending && !m.HasFailure && m.Model.Scheduled != true && !m.IsRetracted);
        if (last is not null)
            last.Receipt = last.Model.ReadAt is { Length: > 0 } r ? "Read " + Formatting.BubbleTime(BlipTime.Parse(r))
                : last.Model.Delivered == true ? "Delivered" : "";
    }

    private async Task MarkSeenAsync(ConversationViewModel conv, IReadOnlyList<Message> msgs)
    {
        var latestInbound = msgs.LastOrDefault(m => !m.FromMe);
        _marks.MarkSeen(conv.Id, conv.Model.LastTime);
        await Ui.InvokeAsync(() =>
        {
            conv.LocallySeen = true;
            OnPropertyChanged(nameof(UnreadTotal));
        });
        // Optional: tell the Mac too (syncs to the phone; raises Messages on the Mac).
        if (_session.Config.PushRead && conv.Model.Unread > 0 && latestInbound is not null)
            await _session.Client.MarkReadAsync(conv.Id, latestInbound.Ts);
    }

    // ------------------------------------------------------------ actions

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        foreach (var c in Conversations) { _marks.MarkSeen(c.Id, c.Model.LastTime); c.LocallySeen = true; }
        OnPropertyChanged(nameof(UnreadTotal));
        var r = await _session.Client.MarkAllReadAsync();
        if (!r.Ok) Banner = MarkAllBanner(r.Error);
    }

    /// <summary>imsg-read exits 75 unless chat.db verifies zero unread. The common partial case reads
    /// "Mark All as Read was clicked but N still unread"; say that plainly instead of echoing stderr.</summary>
    internal static string MarkAllBanner(string? error)
    {
        var m = System.Text.RegularExpressions.Regex.Match(error ?? "", @"clicked but (\d+) still unread");
        return m.Success
            ? $"Marked read here. On the Mac, Mark All as Read ran but {m.Groups[1].Value} conversations are still unread there."
            : "Marked read here; the Mac said: " + error;
    }

    [RelayCommand]
    private void StartNewConversation()
    {
        var to = NewRecipient.Trim();
        if (ChatIds.IsPhone(to.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "")))
            to = new string(to.Where(ch => char.IsAsciiDigit(ch) || ch == '+').ToArray());
        if (!to.StartsWith('+') && to.Length == 10 && to.All(char.IsAsciiDigit)) to = "+1" + to;
        if (!ChatIds.IsValid(to) || ChatIds.IsGroupChat(to)) { Banner = "Enter a phone number (+15551234567) or an email address"; return; }
        var existing = Conversations.FirstOrDefault(c => c.Id == to);
        if (existing is null)
        {
            existing = new ConversationViewModel(new Conversation { Id = to, Title = ChatIds.Pretty(to) });
            Conversations.Insert(0, existing);
        }
        NewRecipient = "";
        Selected = existing;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (Selected is not { } conv || !conv.Model.CanSend) return;
        var text = ComposerText.Trim();
        var files = PendingFiles.ToList();
        if (text.Length == 0 && files.Count == 0) return;
        var service = conv.Model.IsGroup ? Core.Logic.SendService.IMessage : SendServiceSelector.Normalize(SendService);
        var reply = ReplyTarget;
        ComposerText = "";
        PendingFiles.Clear();
        ReplyTarget = null;

        if (files.Count > 0)
        {
            // Caption rides with the LAST file, as Messages does.
            for (var i = 0; i < files.Count; i++)
            {
                var caption = i == files.Count - 1 ? text : "";
                var bubble = AddPending(conv, caption.Length > 0 ? caption : "📎 " + Path.GetFileName(files[i]), service);
                var r = await _session.Client.SendFileAsync(conv.Model, files[i], caption, service);
                Settle(bubble, r);
            }
        }
        else
        {
            var bubble = AddPending(conv, text, service);
            var r = reply is not null && RepliesEnabled
                ? await _session.Client.ReplyAsync(reply.Model, text)
                : await _session.Client.SendTextAsync(conv.Model, text, service);
            Settle(bubble, r);
        }
        _ = ReloadAsync();
    }

    private MessageViewModel AddPending(ConversationViewModel conv, string text, Core.Logic.SendService service)
    {
        var m = new Message { Ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), FromMe = true, Text = text,
            Chat = conv.Id, Service = service.Wire() };
        var vm = new MessageViewModel(m, _session.Client, _session.Cache, conv.Model.IsGroup, "pending:" + Guid.NewGuid().ToString("N"))
        { Pending = true, Owner = this };
        Messages.Add(vm);
        ThreadUpdated?.Invoke();
        return vm;
    }

    private static void Settle(MessageViewModel bubble, ActionResult r)
    {
        bubble.Pending = false;
        if (!r.Ok) bubble.Failure = r.Online ? $"Not sent: {r.Error}" : "Not sent: the Mac is unreachable";
    }

    [RelayCommand]
    private void RemovePendingFile(string path) => PendingFiles.Remove(path);

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (File.Exists(p) && !PendingFiles.Contains(p)) PendingFiles.Add(p);
    }

    [RelayCommand]
    private void CancelReply() => ReplyTarget = null;

    [RelayCommand]
    private void ReplyTo(MessageViewModel m) => ReplyTarget = m;

    [RelayCommand]
    private void CopyText(MessageViewModel m)
    {
        if (SetClipboard is { } set) _ = set(m.Text).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public async Task ReactToAsync(MessageViewModel m, string kind)
    {
        if (!Enum.TryParse<TapbackKind>(kind, true, out var k)) return;
        var mine = m.Model.Tapbacks?.Any(t => t.FromMe && Map(t.Emoji) == k) == true;
        var r = await _session.Client.ReactAsync(m.Model, k, remove: mine);
        if (!r.Ok) Banner = "Tapback failed: " + r.Error;
    }

    private static TapbackKind? Map(string emoji) => emoji switch
    {
        "❤️" or "❤" => TapbackKind.Love,
        "👍" => TapbackKind.Like,
        "👎" => TapbackKind.Dislike,
        "😂" => TapbackKind.Laugh,
        "‼️" => TapbackKind.Emphasize,
        "❓" => TapbackKind.Question,
        _ => null,
    };

    [RelayCommand]
    private void DismissBanner() => Banner = "";
}
