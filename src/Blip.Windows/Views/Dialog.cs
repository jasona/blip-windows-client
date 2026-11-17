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
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
        };
        win[!Window.BackgroundProperty] = win.GetResourceObservable("WindowSolid").ToBinding();
        var ok = new Button { Content = yes, MinWidth = 96, IsDefault = true, Classes = { "accent" } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (no is not null)
        {
            var cancel = new Button { Content = no, MinWidth = 96, IsCancel = true };
            cancel.Click += (_, _) => win.Close();
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(ok);
        ok.Click += (_, _) => { result = true; win.Close(); };
        // Heading from the window title ("Blip: confirm the Mac" reads "Confirm the Mac").
        var heading = title.StartsWith("Blip: ") ? title[6..] : title;
        heading = heading.Length > 0 ? char.ToUpperInvariant(heading[0]) + heading[1..] : heading;
        var footer = new Border { Padding = new Thickness(24, 14), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
        footer[!Border.BackgroundProperty] = footer.GetResourceObservable("Card").ToBinding();
        footer[!Border.BorderBrushProperty] = footer.GetResourceObservable("CardStroke").ToBinding();
        DockPanel.SetDock(footer, Dock.Bottom);
        win.Content = new DockPanel
        {
            Children =
            {
                footer,
                new StackPanel
                {
                    Margin = new Thickness(24, 20, 24, 22),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = heading, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new SelectableTextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 13, LineHeight = 19 },
                    },
                },
            },
        };
        return (win, () => result);
    }
}
