using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Blip.Core.Config;
using Blip.Windows.ViewModels;

namespace Blip.Windows.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    /// <summary>True once Save validated; also the ShowDialog result.</summary>
    public bool Saved { get; private set; }

    // For the XAML previewer only.
    public SettingsWindow() : this(null!, false) { }

    public SettingsWindow(SettingsViewModel vm, bool firstRun)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        if (vm is not null) vm.SetClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
        if (firstRun)
        {
            Intro.IsVisible = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            _vm.ToConfig().Validate();
        }
        catch (BridgeConfigException ex)
        {
            await Dialog.AlertAsync(this, "Blip", ex.Message);
            return;
        }
        Saved = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
