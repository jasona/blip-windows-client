using Blip.Core;
using Blip.Core.Config;
using Blip.Core.Transport;
using Blip.Windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Blip.Windows.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _keyPath;
    [ObservableProperty] private bool _hideSpam;
    [ObservableProperty] private bool _hideUnknown;
    [ObservableProperty] private bool _tapbacks;
    [ObservableProperty] private bool _replies;
    [ObservableProperty] private bool _preferIMessage;
    [ObservableProperty] private bool _pushRead;
    [ObservableProperty] private bool _localCache;
    [ObservableProperty] private string _hostKey;
    [ObservableProperty] private string _publicKey = "";
    [ObservableProperty] private string _macCommand = "";
    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private bool _busy;

    /// <summary>Set by the window: the clipboard belongs to the top-level, not the view model.</summary>
    public Func<string, Task>? SetClipboard { get; set; }

    public SettingsViewModel(BridgeConfig c)
    {
        _host = c.Host; _port = c.Port; _keyPath = c.KeyPath; _hideSpam = c.HideSpam; _hideUnknown = c.HideUnknown;
        _tapbacks = c.Tapbacks; _replies = c.Replies; _preferIMessage = c.PreferIMessage; _pushRead = c.PushRead; _localCache = c.LocalCache;
        _hostKey = c.HostKey;
        RefreshPublicKey();
    }

    public BridgeConfig ToConfig() => new()
    {
        Host = Host.Trim(), Port = Port, KeyPath = KeyPath.Trim(), HostKey = HostKey.Trim(),
        HideSpam = HideSpam, HideUnknown = HideUnknown, Tapbacks = Tapbacks, Replies = Replies,
        PreferIMessage = PreferIMessage, PushRead = PushRead, LocalCache = LocalCache,
    };

    partial void OnKeyPathChanged(string value) => RefreshPublicKey();

    private void RefreshPublicKey()
    {
        var pub = KeySetup.ReadPublicKey(KeyPath) ?? "";
        PublicKey = pub;
        MacCommand = pub.Length > 0 ? KeySetup.MacCommand(pub) : "";
    }

    [RelayCommand]
    private async Task GenerateKeyAsync()
    {
        Busy = true;
        var err = await KeySetup.GenerateAsync(KeyPath);
        Busy = false;
        TestResult = err ?? (File.Exists(KeyPath) ? "Key ready. Run the command below on the Mac, then Test." : "");
        RefreshPublicKey();
    }

    [RelayCommand]
    private void CopyMacCommand()
    {
        if (MacCommand.Length == 0 || SetClipboard is null) return;
        _ = Copy();
        async Task Copy()
        {
            try { await SetClipboard(MacCommand); TestResult = "Copied. Paste it into Terminal on the Mac."; } catch { }
        }
    }

    [RelayCommand]
    private void ForgetHostKey()
    {
        HostKey = "";
        TestResult = "Host key forgotten: the next connection will ask you to confirm the Mac's fingerprint.";
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        Busy = true;
        TestResult = "Testing…";
        try
        {
            var cfg = ToConfig();
            cfg.Validate();
            string? pinned = null;
            await using var t = new SshBridgeTransport(cfg, App.ConfirmHostKey);
            t.HostKeyPinned += fp => pinned = fp;
            var client = new BlipClient(t, cfg);
            var session = await client.SessionAsync();
            var chats = await client.ChatsAsync(5);
            if (pinned is not null) HostKey = pinned;
            TestResult = $"Connected. {chats.Count} recent conversations visible."
                         + (session.Gui == false ? " Warning: nobody is logged in at the Mac, so sends will fail." : "");
        }
        catch (Exception e)
        {
            TestResult = e.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}
