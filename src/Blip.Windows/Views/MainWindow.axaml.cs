using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Blip.Windows.ViewModels;

namespace Blip.Windows.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _stickToBottom = true;

    public event Func<Task>? SettingsRequested;

    /// <summary>Ctrl+N: the new-message field. Ctrl+F: search.</summary>
    public IRelayCommand NewMessageCommand { get; }
    public IRelayCommand FocusSearchCommand { get; }

    // For the XAML previewer only.
    public MainWindow() : this(null!) { }

    public MainWindow(MainViewModel vm)
    {
        NewMessageCommand = new RelayCommand(() => ShowNewMessage(true));
        FocusSearchCommand = new RelayCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); });
        InitializeComponent();
        _vm = vm;
        // Mica where Windows offers it; a solid backdrop everywhere else.
        void Backdrop() => Root.Classes.Set("mica", ActualTransparencyLevel == WindowTransparencyLevel.Mica
                                                     || ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty) Backdrop();
            // The system caption buttons sit over the thread header's right end.
            if (e.Property == IsExtendedIntoWindowDecorationsProperty || e.Property == WindowStateProperty)
                ThreadHeader.Padding = new Thickness(20, 0, IsExtendedIntoWindowDecorations ? 150 : 20, 0);
        };
        Backdrop();
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

    private void OnNewMessage(object? sender, RoutedEventArgs e) => ShowNewMessage(!NewMessagePanel.IsVisible);

    private void ShowNewMessage(bool show)
    {
        NewMessagePanel.IsVisible = show;
        if (show) NewRecipientBox.Focus();
    }

    /// <summary>The sidebar top and the thread header are the title bar: drag to move, double-click to maximise.</summary>
    private void OnDragArea(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1 && !OnControl(e.Source))
            BeginMoveDrag(e);
    }

    private void OnDragAreaDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (OnControl(e.Source)) return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private static bool OnControl(object? source) =>
        source is Visual v && (v.FindAncestorOfType<Button>(true) is not null || v.FindAncestorOfType<TextBox>(true) is not null);

    private void OnNewMessageGo(object? sender, RoutedEventArgs e)
    {
        _vm.StartNewConversationCommand.Execute(null);
        NewMessagePanel.IsVisible = false;
    }

    private void OnNewRecipientKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ShowNewMessage(false); e.Handled = true; return; }
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
