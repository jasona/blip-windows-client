using System.Windows.Media.Imaging;
using Blip.Core.Logic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Blip.Windows.ViewModels;

public sealed partial class ConversationViewModel(Conversation model) : ObservableObject
{
    [ObservableProperty] private Conversation _model = model;
    [ObservableProperty] private bool _locallySeen;
    [ObservableProperty] private BitmapImage? _avatar;

    public string Id => Model.Id;
    public string Title => Model.Title;
    public string Preview => Model.LastPreview;
    public string Time => Formatting.ListTime(Model.LastTime);
    public bool IsUnread => Model.Unread > 0 && !LocallySeen;
    public bool Pinned => Model.Pinned;
    public bool Muted => Model.Muted;
    public string Initials => Formatting.Initials(Model.Title);
    public string Color => Formatting.ColorFor(Model.Id);
    public bool IsGreen => !Model.IsGroup && SendServiceSelector.Normalize(Model.Service) != SendService.IMessage;
    public string Subtitle => Model.IsGroup
        ? string.Join(", ", Model.Participants.Select(p => p.Name))
        : Model.Id.Contains('@') ? Model.Id : ChatIds.Pretty(Model.Id);

    partial void OnModelChanged(Conversation value)
    {
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(IsUnread)); OnPropertyChanged(nameof(Pinned)); OnPropertyChanged(nameof(Muted));
        OnPropertyChanged(nameof(Initials)); OnPropertyChanged(nameof(Subtitle)); OnPropertyChanged(nameof(IsGreen));
    }

    partial void OnLocallySeenChanged(bool value) => OnPropertyChanged(nameof(IsUnread));
}
