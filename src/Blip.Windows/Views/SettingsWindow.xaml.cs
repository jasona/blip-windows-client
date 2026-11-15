using System.Windows;
using Blip.Core.Config;
using Blip.Windows.ViewModels;

namespace Blip.Windows.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm, bool firstRun)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        if (firstRun)
        {
            Intro.Visibility = Visibility.Visible;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.ToConfig().Validate();
        }
        catch (BridgeConfigException ex)
        {
            MessageBox.Show(this, ex.Message, "Blip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
