using Avalonia.Media;
using Avalonia.Media.Imaging;
using Blip.Core.Logic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Blip.Windows.ViewModels;

public sealed partial class ConversationViewModel(Conversation model) : ObservableObject
{
    [ObservableProperty] private Conversation _model = model;
    [ObservableProperty] private bool _locallySeen;
    [ObservableProperty] private Bitmap? _avatar;
    /// <summary>The open conversation; the pinned grid draws its own selection from this.</summary>
    [ObservableProperty] private bool _isSelected;

    public string Id => Model.Id;
    public string Title => Model.Title;
    /// <summary>First name, or the group name: what fits under a pinned tile.</summary>
    public string ShortTitle => Model.IsGroup || !char.IsLetter(Model.Title.FirstOrDefault()) ? Model.Title : Model.Title.Split(' ')[0];
    public string Preview => Model.LastPreview;
    public string Time => Formatting.ListTime(Model.LastTime);
    public bool IsUnread => Model.Unread > 0 && !LocallySeen;
    public bool Pinned => Model.Pinned;
    public bool Muted => Model.Muted;
    public string Initials => Formatting.Initials(Model.Title);
    public string Color => Formatting.ColorFor(Model.Id);
    public IBrush ColorBrush => Avalonia.Media.Color.TryParse(Color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;
    public IBrush AvatarBrush => Formatting.AvatarBrush(Model.Id);
    public bool HasInitials => Initials != "#";
    public bool IsGroupChat => Model.IsGroup;
    public bool HasAvatar => Avatar is not null;
    public bool IsGreen => !Model.IsGroup && SendServiceSelector.IsGreen(Model.Service);
    /// <summary>Under the name in the header: the group's people, or the number when the name is not already it.</summary>
    public string Subtitle
    {
        get
        {
            if (Model.IsGroup)
                return Model.Participants.Count == 0 ? "Group"
                    : string.Join(", ", Model.Participants.Select(p => ChatIds.Pretty(p.Name)));
            var id = Model.Id.Contains('@') ? Model.Id : ChatIds.Pretty(Model.Id);
            return id == Title ? "" : id;
        }
    }
    public bool HasSubtitle => Subtitle.Length > 0;

    partial void OnModelChanged(Conversation value)
    {
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(ShortTitle)); OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(IsUnread)); OnPropertyChanged(nameof(Pinned)); OnPropertyChanged(nameof(Muted));
        OnPropertyChanged(nameof(Initials)); OnPropertyChanged(nameof(Subtitle)); OnPropertyChanged(nameof(HasSubtitle)); OnPropertyChanged(nameof(IsGreen));
        OnPropertyChanged(nameof(Color)); OnPropertyChanged(nameof(ColorBrush)); OnPropertyChanged(nameof(AvatarBrush));
        OnPropertyChanged(nameof(HasInitials)); OnPropertyChanged(nameof(IsGroupChat));
    }

    partial void OnLocallySeenChanged(bool value) => OnPropertyChanged(nameof(IsUnread));
    partial void OnAvatarChanged(Bitmap? value) => OnPropertyChanged(nameof(HasAvatar));
}
