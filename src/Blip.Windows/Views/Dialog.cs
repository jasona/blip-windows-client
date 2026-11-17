using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Blip.Windows.Views;

/// <summary>Minimal message box: Avalonia has none built in.</summary>
public static class Dialog
{
    public static Task AlertAsync(Window? owner, string title, string text) => ShowAsync(owner, title, text, "OK", null);

    public static Task<bool> ConfirmAsync(Window? owner, string title, string text, string yes, string no) =>
        ShowAsync(owner, title, text, yes, no);

    private static async Task<bool> ShowAsync(Window? owner, string title, string text, string yes, string? no)
    {
        var (win, result) = Build(title, text, yes, no);
        win.WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        if (owner is not null) await win.ShowDialog(owner);
        else
        {
            var closed = new TaskCompletionSource();
            win.Closed += (_, _) => closed.TrySetResult();
            win.Show();
            await closed.Task;
        }
        return result();
    }

    /// <summary>The dialog window, and what was chosen once it closes. Separate so the UI tests can draw it.</summary>
    internal static (Window Window, Func<bool> Result) Build(string title, string text, string yes, string? no)
    {
        var result = false;
        var win = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
        };
        var ok = new Button { Content = yes, MinWidth = 90, IsDefault = true, Classes = { "accent" } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (no is not null)
        {
            var cancel = new Button { Content = no, MinWidth = 90, IsCancel = true };
            cancel.Click += (_, _) => win.Close();
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(ok);
        ok.Click += (_, _) => { result = true; win.Close(); };
        win.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new SelectableTextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
        return (win, () => result);
    }
}
