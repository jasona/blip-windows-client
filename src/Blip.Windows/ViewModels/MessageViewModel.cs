using Blip.Core;
using Blip.Core.Cache;
using Blip.Core.Logic;
using Blip.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Blip.Windows.ViewModels;

public enum BubbleKind { Theirs, MineBlue, MineGreen }

/// <summary>One bubble. Built from a bridge message, or optimistic while a send is in flight.</summary>
public sealed partial class MessageViewModel : ObservableObject
{
    public Message Model { get; }
    public string Key { get; }

    [ObservableProperty] private bool _pending;
    [ObservableProperty] private string _failure = "";
    [ObservableProperty] private string _receipt = "";
    [ObservableProperty] private bool _showSender;
    [ObservableProperty] private string _dayHeader = "";

    public IReadOnlyList<AttachmentViewModel> Attachments { get; }
    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>The window's view model: the bubble's context menu runs its commands.</summary>
    public MainViewModel? Owner { get; init; }

    public MessageViewModel(Message m, BlipClient client, AttachmentCache cache, bool isGroup, string? pendingKey = null)
    {
        Model = m;
        Key = pendingKey ?? (m.Guid is { Length: > 0 } g ? g : m.Id.ToString());
        IsGroup = isGroup;
        Attachments = (m.Attachments ?? [])
            .Where(a => a.Id is not null)
            .Select(a => new AttachmentViewModel(a, client, cache))
            .ToList();
        if (m.Failed) Failure = $"Not delivered (error {m.Error})";
    }

    public bool IsGroup { get; }
    public bool IsMine => Model.FromMe;
    public BubbleKind Kind => !IsMine ? BubbleKind.Theirs
        : SendServiceSelector.IsGreen(Model.Service) ? BubbleKind.MineGreen : BubbleKind.MineBlue;
    public bool IsTheirs => Kind == BubbleKind.Theirs;
    public bool IsBlue => Kind == BubbleKind.MineBlue;
    public bool IsGreenBubble => Kind == BubbleKind.MineGreen;
    public bool TapbacksEnabled => Owner?.TapbacksEnabled == true;
    public bool RepliesEnabled => Owner?.RepliesEnabled == true;

    [RelayCommand] private void Copy() => Owner?.CopyTextCommand.Execute(this);
    [RelayCommand] private void Reply() => Owner?.ReplyToCommand.Execute(this);
    [RelayCommand] private Task React(string kind) => Owner?.ReactToAsync(this, kind) ?? Task.CompletedTask;

    /// <summary>Only web links: a message can carry any URI scheme.</summary>
    [RelayCommand]
    private void OpenLink()
    {
        if (Uri.TryCreate(LinkUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Text minus the object-replacement chars Messages leaves where attachments sat.</summary>
    public string Text => Model.Retracted == true ? "" : Model.Text.Replace("￼", "").Trim();
    public bool HasText => Text.Length > 0;
    public bool IsRetracted => Model.Retracted == true;
    public string Sender => Model.Name ?? (Model.Handle is { } h ? ChatIds.Pretty(h) : "");
    public string Time => Formatting.BubbleTime(Model.Time);
    public string Meta
    {
        get
        {
            var parts = new List<string> { Time };
            if (Model.Edited == true) parts.Add("Edited");
            if (!string.IsNullOrEmpty(Model.Effect)) parts.Add("sent with " + Model.Effect);
            if (Model.Scheduled == true) parts.Add("Send Later");
            return string.Join(" · ", parts);
        }
    }

    public string Tapbacks => Model.Tapbacks is { Count: > 0 } t
        ? string.Join(" ", t.GroupBy(x => x.Emoji).Select(g => g.Count() > 1 ? $"{g.Key}{g.Count()}" : g.Key))
        : "";
    public bool HasTapbacks => Tapbacks.Length > 0;

    public string ReplyQuote => Model.ReplyTo is { } r
        ? (r.FromMe ? "You: " : "") + (r.Text.Length > 0 ? r.Text.Replace("￼", "").Trim() : r.Attachment?.Name ?? "attachment")
        : "";
    public bool HasReply => ReplyQuote.Length > 0;

    public string LinkTitle => Model.Link is { } l ? (l.Title.Length > 0 ? l.Title : l.Url) : "";
    public string LinkUrl => Model.Link?.Url ?? "";
    public bool HasLink => Model.Link is not null;

    public bool HasFailure => Failure.Length > 0;
    public bool HasReceipt => Receipt.Length > 0;

    partial void OnFailureChanged(string value) => OnPropertyChanged(nameof(HasFailure));
    partial void OnReceiptChanged(string value) => OnPropertyChanged(nameof(HasReceipt));

    /// <summary>Same content as a fresh load? Then keep this instance (and its loaded images).</summary>
    public bool SameAs(Message m) =>
        m.Text == Model.Text && m.Edited == Model.Edited && m.Retracted == Model.Retracted && m.Error == Model.Error
        && m.ReadAt == Model.ReadAt && m.Delivered == Model.Delivered && m.ActivityTs == Model.ActivityTs
        && (m.Tapbacks?.Count ?? 0) == (Model.Tapbacks?.Count ?? 0);
}
