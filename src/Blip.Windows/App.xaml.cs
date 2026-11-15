using System.Windows;
using Blip.Core.Config;
using Blip.Windows.Services;
using Blip.Windows.ViewModels;
using Blip.Windows.Views;
using Microsoft.Toolkit.Uwp.Notifications;
using Forms = System.Windows.Forms;

namespace Blip.Windows;

public partial class App : Application
{
    private const string SingleInstanceName = "Blip.Windows.SingleInstance";
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private Forms.NotifyIcon? _tray;
    private BlipSession? _session;
    private MainWindow? _main;
    private bool _quitting;

    /// <summary>Trust-on-first-use for the Mac's host key. Called on SSH.NET's thread.</summary>
    public static bool ConfirmHostKey(string fingerprint) =>
        Current.Dispatcher.Invoke(() => MessageBox.Show(
            $"First connection to this Mac.\n\nIts host key fingerprint is:\n{fingerprint}\n\n" +
            "Check it on the Mac with:\n  ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub\n\nTrust this Mac?",
            "Blip: confirm the Mac", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, SingleInstanceName, out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceName + ".Show");
        if (!first)
        {
            _showSignal.Set();   // bring the running instance forward instead
            Shutdown();
            return;
        }
        new Thread(() => { while (_showSignal.WaitOne()) Dispatcher.Invoke(ShowMain); }) { IsBackground = true }.Start();

        ToastNotificationManagerCompat.OnActivated += args =>
        {
            var parsed = ToastArguments.Parse(args.Argument);
            Dispatcher.Invoke(() =>
            {
                ShowMain();
                if (parsed.TryGetValue(Notifier.ChatArg, out var chat)) _main?.OpenConversation(chat);
            });
        };

        CreateTray();
        var config = BridgeConfig.Load();
        if (string.IsNullOrWhiteSpace(config.Host) && !OpenSettings(firstRun: true))
        {
            Quit();
            return;
        }
        StartSession();
    }

    private void StartSession()
    {
        var config = BridgeConfig.Load();
        try
        {
            _session = new BlipSession(config, ConfirmHostKey);
        }
        catch (BridgeConfigException ex)
        {
            MessageBox.Show(ex.Message, "Blip", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (OpenSettings(firstRun: false)) StartSession(); else Quit();
            return;
        }
        var vm = new MainViewModel(_session);
        vm.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(MainViewModel.UnreadTotal)) UpdateTray(vm.UnreadTotal); };
        _main = new MainWindow(vm);
        _main.Closing += (_, a) =>
        {
            if (_quitting) return;
            a.Cancel = true;     // close = hide to tray; Quit from the tray menu exits
            _main.Hide();
        };
        _main.SettingsRequested += () =>
        {
            if (!OpenSettings(firstRun: false)) return;
            _ = RestartSessionAsync();
        };
        _main.Show();
        vm.Start();
    }

    private async Task RestartSessionAsync()
    {
        var old = _session;
        _quitting = true;
        _main?.Close();
        _quitting = false;
        if (old is not null) await old.DisposeAsync();
        StartSession();
    }

    /// <returns>true when settings were saved.</returns>
    private bool OpenSettings(bool firstRun)
    {
        var vm = new SettingsViewModel(BridgeConfig.Load());
        var win = new SettingsWindow(vm, firstRun);
        if (_main is { IsVisible: true }) win.Owner = _main;
        if (win.ShowDialog() != true) return false;
        vm.ToConfig().Save();
        return true;
    }

    private void CreateTray()
    {
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Themes/blip.ico"))?.Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = iconStream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(iconStream),
            Text = "Blip",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("Open Blip", null, (_, _) => ShowMain());
        _tray.ContextMenuStrip.Items.Add("Settings…", null, (_, _) => _main?.RequestSettings());
        _tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Quit", null, (_, _) => Quit());
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) ShowMain(); };
    }

    private void UpdateTray(int unread)
    {
        if (_tray is null) return;
        _tray.Text = unread > 0 ? $"Blip: {unread} unread" : "Blip";
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private async void Quit()
    {
        _quitting = true;
        _main?.Close();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        if (_session is not null) await _session.DisposeAsync();
        _single?.Dispose();
        Shutdown();
    }
}
