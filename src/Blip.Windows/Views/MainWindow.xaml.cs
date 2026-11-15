using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Blip.Windows.ViewModels;
using Microsoft.Win32;

namespace Blip.Windows.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _stickToBottom = true;

    public event Action? SettingsRequested;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.IsWindowActive = () => Dispatcher.Invoke(() => IsActive && IsVisible);
        vm.ThreadUpdated += () => Dispatcher.InvokeAsync(() => { if (_stickToBottom) ThreadScroll.ScrollToEnd(); },
            System.Windows.Threading.DispatcherPriority.Background);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Selected)) { _stickToBottom = true; Composer.Focus(); }
        };
        ThreadScroll.ScrollChanged += (_, e) =>
        {
            // Follow new messages only while the person is already at the bottom.
            if (e.ExtentHeightChange == 0) _stickToBottom = ThreadScroll.VerticalOffset >= ThreadScroll.ScrollableHeight - 24;
        };
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _vm.CanCompose ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
    }

    public void RequestSettings() => SettingsRequested?.Invoke();

    public void OpenConversation(string chat)
    {
        var c = _vm.Conversations.FirstOrDefault(x => x.Id == chat);
        if (c is not null) _vm.Selected = c;
    }

    private void OnSettings(object sender, RoutedEventArgs e) => RequestSettings();

    private void OnNewMessage(object sender, RoutedEventArgs e)
    {
        NewMessagePanel.Visibility = NewMessagePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (NewMessagePanel.Visibility == Visibility.Visible) NewRecipientBox.Focus();
    }

    private void OnNewMessageGo(object sender, RoutedEventArgs e) => NewMessagePanel.Visibility = Visibility.Collapsed;

    private void OnNewRecipientKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm.StartNewConversationCommand.Execute(null);
        NewMessagePanel.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnComposerKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            _stickToBottom = true;
            if (_vm.SendCommand.CanExecute(null)) _vm.SendCommand.Execute(null);
            return;
        }
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && TryPasteFiles())
            e.Handled = true;
    }

    /// <summary>Ctrl+V of an image or of copied files attaches them; plain text pastes as usual.</summary>
    private bool TryPasteFiles()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                _vm.AddFiles(Clipboard.GetFileDropList().Cast<string>());
                return true;
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                var dir = Path.Combine(Path.GetTempPath(), "Blip");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"Pasted image {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(img));
                    enc.Save(fs);
                }
                _vm.AddFiles([path]);
                return true;
            }
        }
        catch { /* clipboard busy */ }
        return false;
    }

    private void OnAttach(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Attach files" };
        if (dlg.ShowDialog(this) == true) _vm.AddFiles(dlg.FileNames);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm.CanCompose && e.Data.GetData(DataFormats.FileDrop) is string[] files) _vm.AddFiles(files);
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        // Only web links: a message can carry any URI scheme.
        if (e.Uri.Scheme is "http" or "https")
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
