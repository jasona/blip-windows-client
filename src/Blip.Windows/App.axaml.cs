using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Blip.Core.Config;
using Blip.Windows.Services;
using Blip.Windows.ViewModels;
using Blip.Windows.Views;

namespace Blip.Windows;

public partial class App : Application
{
    private const string SingleInstanceName = "Blip.Windows.SingleInstance";
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private BlipSession? _session;
    private MainWindow? _main;
    private MainViewModel? _vm;
    private bool _quitting;

    private IClassicDesktopStyleApplicationLifetime Desktop => (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Blip Inter (no emoji glyphs) for windows; emoji come from the fallback fonts and EmojiText.
        Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.OfType<Window>(x))
        {
            Setters = { new Avalonia.Styling.Setter(TemplatedControl.FontFamilyProperty, new Avalonia.Media.FontFamily(Fonts.Ui)) },
        });
    }

    internal static string HostKeyPrompt(string fingerprint) =>
        $"First connection to this Mac.\n\nIts host key fingerprint is:\n{fingerprint}\n\n" +
        "Check it on the Mac with:\n  ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub\n\nTrust this Mac?";

    /// <summary>Trust-on-first-use for the Mac's host key. Called on SSH.NET's thread, so it may block.</summary>
    public static bool ConfirmHostKey(string fingerprint)
    {
        Task<bool> Ask() => Dialog.ConfirmAsync((Current as App)?.ActiveWindow(), "Blip: confirm the Mac",
            HostKeyPrompt(fingerprint), "Trust", "Cancel");
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(Ask).GetAwaiter().GetResult();
        // Raised on the UI thread: keep the UI pumping until the dialog answers.
        var task = Ask();
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
        return task.Result;
    }

    private Window? ActiveWindow() => Desktop.Windows.FirstOrDefault(w => w.IsActive) ?? Desktop.Windows.FirstOrDefault(w => w.IsVisible);

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime) return;

        if (OperatingSystem.IsWindows())
        {
            _single = new Mutex(true, SingleInstanceName, out var first);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceName + ".Show");
            if (!first)
            {
                _showSignal.Set();   // bring the running instance forward instead
                Desktop.Shutdown();
                return;
            }
            var signal = _showSignal;
            new Thread(() => { while (signal.WaitOne()) Dispatcher.UIThread.Post(ShowMain); }) { IsBackground = true }.Start();
        }

        Notifier.Init();
        Notifier.Activated += chat => Dispatcher.UIThread.Post(() =>
        {
            ShowMain();
            _main?.OpenConversation(chat);
        });

        var config = BridgeConfig.Load();
        if (string.IsNullOrWhiteSpace(config.Host) && !await OpenSettingsAsync(firstRun: true))
        {
            await QuitAsync();
            return;
        }
        await StartSessionAsync();
    }

    private async Task StartSessionAsync()
    {
        var config = BridgeConfig.Load();
        try
        {
            _session = new BlipSession(config, ConfirmHostKey);
        }
        catch (BridgeConfigException ex)
        {
            await Dialog.AlertAsync(null, "Blip", ex.Message);
            if (await OpenSettingsAsync(firstRun: false)) await StartSessionAsync(); else await QuitAsync();
            return;
        }
        var vm = new MainViewModel(_session);
        _vm = vm;
        vm.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(MainViewModel.UnreadTotal)) UpdateTray(vm.UnreadTotal); };
        _main = new MainWindow(vm);
        _main.Closing += (_, a) =>
        {
            if (_quitting) return;
            a.Cancel = true;     // close = hide to tray; Quit from the tray menu exits
            _main.Hide();
        };
        _main.SettingsRequested += async () =>
        {
            if (!await OpenSettingsAsync(firstRun: false)) return;
            await RestartSessionAsync();
        };
        Desktop.MainWindow = _main;
        _main.Show();
        vm.Start();
    }

    private async Task RestartSessionAsync()
    {
        var old = _session;
        _vm?.SaveSnapshot();
        _quitting = true;
        _main?.Close();
        _quitting = false;
        if (old is not null) await old.DisposeAsync();
        await StartSessionAsync();
    }

    /// <returns>true when settings were saved.</returns>
    private async Task<bool> OpenSettingsAsync(bool firstRun)
    {
        var vm = new SettingsViewModel(BridgeConfig.Load());
        var win = new SettingsWindow(vm, firstRun);
        bool saved;
        if (_main is { IsVisible: true }) saved = await win.ShowDialog<bool>(_main);
        else
        {
            var done = new TaskCompletionSource<bool>();
            win.Closed += (_, _) => done.TrySetResult(win.Saved);
            win.Show();
            saved = await done.Task;
        }
        if (!saved) return false;
        vm.ToConfig().Save();
        return true;
    }

    private TrayIcon? Tray => TrayIcon.GetIcons(this) is { Count: > 0 } icons ? icons[0] : null;

    private void UpdateTray(int unread)
    {
        if (Tray is { } t) t.ToolTipText = unread > 0 ? $"Blip: {unread} unread" : "Blip";
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void OnTrayClicked(object? sender, EventArgs e) => ShowMain();
    private void OnTrayOpen(object? sender, EventArgs e) => ShowMain();
    private async void OnTraySettings(object? sender, EventArgs e) { if (_main is not null) await _main.RequestSettings(); }
    private async void OnTrayQuit(object? sender, EventArgs e) => await QuitAsync();

    private async Task QuitAsync()
    {
        _vm?.SaveSnapshot();
        _quitting = true;
        _main?.Close();
        if (Tray is { } t) t.IsVisible = false;
        if (_session is not null) await _session.DisposeAsync();
        _single?.Dispose();
        Desktop.Shutdown();
    }
}
