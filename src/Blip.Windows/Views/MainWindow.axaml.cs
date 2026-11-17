using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Blip.Windows.ViewModels;

namespace Blip.Windows.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _stickToBottom = true;

    public event Func<Task>? SettingsRequested;

    // For the XAML previewer only.
    public MainWindow() : this(null!) { }

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        if (vm is null) return;
        vm.IsWindowActive = () => Dispatcher.UIThread.CheckAccess()
            ? IsActive && IsVisible
            : Dispatcher.UIThread.Invoke(() => IsActive && IsVisible);
        vm.SetClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
        vm.ThreadUpdated += () => Dispatcher.UIThread.Post(() => { if (_stickToBottom) ThreadScroll.ScrollToEnd(); }, DispatcherPriority.Background);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Selected)) { _stickToBottom = true; Composer.Focus(); }
        };
        ThreadScroll.ScrollChanged += (_, e) =>
        {
            // Follow new messages only while the person is already at the bottom.
            if (e.ExtentDelta.Y == 0)
                _stickToBottom = ThreadScroll.Offset.Y >= ThreadScroll.Extent.Height - ThreadScroll.Viewport.Height - 24;
        };
        Composer.AddHandler(KeyDownEvent, OnComposerKey, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && _vm.CanCompose ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_vm.CanCompose && e.DataTransfer.TryGetFiles() is { } items) _vm.AddFiles(LocalPaths(items));
        });
    }

    public Task RequestSettings() => SettingsRequested?.Invoke() ?? Task.CompletedTask;

    public void OpenConversation(string chat)
    {
        var c = _vm.Conversations.FirstOrDefault(x => x.Id == chat);
        if (c is not null) _vm.Selected = c;
    }

    private async void OnSettings(object? sender, RoutedEventArgs e) => await RequestSettings();

    private void OnNewMessage(object? sender, RoutedEventArgs e)
    {
        NewMessagePanel.IsVisible = !NewMessagePanel.IsVisible;
        if (NewMessagePanel.IsVisible) NewRecipientBox.Focus();
    }

    private void OnNewMessageGo(object? sender, RoutedEventArgs e)
    {
        _vm.StartNewConversationCommand.Execute(null);
        NewMessagePanel.IsVisible = false;
    }

    private void OnNewRecipientKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnNewMessageGo(sender, e);
        e.Handled = true;
    }

    private void OnComposerKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            e.Handled = true;
            _stickToBottom = true;
            if (_vm.SendCommand.CanExecute(null)) _vm.SendCommand.Execute(null);
            return;
        }
        if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            // Decide asynchronously: files and images attach, anything else pastes as text.
            e.Handled = true;
            _ = PasteAsync();
        }
    }

    /// <summary>
    /// Ctrl+V: copied files attach; text pastes as text (Office puts a picture next to copied
    /// text, which must not win); an image alone, such as a screenshot, attaches as a PNG.
    /// </summary>
    private async Task PasteAsync()
    {
        try
        {
            if (Clipboard is { } cb)
            {
                if (await cb.TryGetFilesAsync() is { } items && LocalPaths(items).ToList() is { Count: > 0 } paths)
                {
                    _vm.AddFiles(paths);
                    return;
                }
                if (string.IsNullOrEmpty(await cb.TryGetTextAsync()) && await cb.TryGetBitmapAsync() is { } bmp)
                {
                    var dir = Path.Combine(Path.GetTempPath(), "Blip");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, $"Pasted image {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
                    bmp.Save(path);
                    _vm.AddFiles([path]);
                    return;
                }
            }
        }
        catch { /* clipboard busy: fall through to a text paste */ }
        Composer.Paste();
    }

    private async void OnAttach(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Attach files" });
        _vm.AddFiles(LocalPaths(files));
    }

    private static IEnumerable<string> LocalPaths(IEnumerable<IStorageItem> items) =>
        items.Select(i => i.TryGetLocalPath()).OfType<string>();
}
