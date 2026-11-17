using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Blip.Core.Config;
using Blip.Windows.Services;
using Blip.Windows.ViewModels;
using Blip.Windows.Views;

namespace Blip.Ui.Tests;

public class UiTests
{
    /// <summary>The reason for the Avalonia port: emoji must draw in colour, in read-only text AND while typing.</summary>
    [AvaloniaFact]
    public void Emoji_draw_in_colour_in_text_and_in_the_composer()
    {
        var label = new SelectableTextBlock { Text = "😂🎉❤️🔥", FontSize = 28 };
        var box = new TextBox { Text = "😂🎉❤️🔥", FontSize = 28, Width = 300 };
        var plain = new SelectableTextBlock { Text = "plain text 0123456789 #", FontSize = 28 };
        var w = new Window
        {
            Width = 420, Height = 260,
            Content = new StackPanel { Margin = new Thickness(10), Spacing = 10, HorizontalAlignment = HorizontalAlignment.Left, Children = { label, box, plain } },
        };
        w.Show();
        var frame = Shots.Capture(w, "emoji-colour");

        Rect Bounds(Control c) => c.Bounds.Translate(c.TranslatePoint(default, w)!.Value - c.Bounds.Position);
        PixelRect Px(Control c) { var r = Bounds(c); return new PixelRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height); }

        Assert.True(Shots.ColourfulPixels(frame, Px(label)) > 400, "read-only emoji are not in colour");
        Assert.True(Shots.ColourfulPixels(frame, Px(box)) > 400, "emoji in the composer are not in colour");
        Assert.True(Shots.ColourfulPixels(frame, Px(plain)) < 20, "digits or '#' picked up the colour emoji font");
    }

    /// <summary>
    /// Avalonia shapes a space after an emoji with the emoji font, whose space is emoji-wide.
    /// EmojiTextPresenter must keep it UI-font-wide while typing, also inside a selection.
    /// </summary>
    [AvaloniaFact]
    public void A_space_after_an_emoji_is_a_normal_space_while_typing()
    {
        var box = new TextBox { FontSize = 20, Width = 400 };
        var w = new Window { Width = 450, Height = 120, Content = box };
        w.Show();
        double Width(string text)
        {
            box.Text = text;
            Shots.Pump();
            var presenter = box.GetVisualDescendants().OfType<EmojiTextPresenter>().Single();
            return presenter.TextLayout.WidthIncludingTrailingWhitespace;
        }

        var space = Width("x x") - Width("xx");
        var afterEmoji = Width("😂 x") - Width("😂x");
        Assert.InRange(afterEmoji, space - 0.5, space + 0.5);

        box.Text = "a😂 😂 b";
        box.SelectAll();
        Shots.Pump();
        var selected = box.GetVisualDescendants().OfType<EmojiTextPresenter>().Single().TextLayout.WidthIncludingTrailingWhitespace;
        Assert.InRange(selected, Width("a😂 😂 b") - 0.5, Width("a😂 😂 b") + 0.5);
        Assert.InRange(Width("a😂 😂 b") - Width("a😂😂b"), 2 * space - 0.5, 2 * space + 0.5);
    }

    /// <summary>Real SSH to the stub Mac (tests/e2e/setup-stub-mac.sh); passes without running when it is not set up.</summary>
    [AvaloniaFact]
    public async Task Main_window_against_the_stub_mac()
    {
        static string Env(string k) => Environment.GetEnvironmentVariable(k) ?? "";
        if (Env("BLIP_E2E_SENT_LOG").Length == 0) return;
        var cfg = new BridgeConfig
        {
            Host = Env("BLIP_E2E_HOST"), Port = int.Parse(Env("BLIP_E2E_PORT")), KeyPath = Env("BLIP_E2E_KEY"),
            Tapbacks = true, Replies = true, LocalCache = false,
        };
        await using var session = new BlipSession(cfg, _ => true);
        var vm = new MainViewModel(session);
        var w = new MainWindow(vm) { Width = 1100, Height = 720 };
        w.Show();
        vm.Start();
        await Shots.Settle(() => vm.Conversations.Count >= 2 && vm.Status == Blip.Core.Live.BridgeStatus.Online);
        Assert.Equal(2, vm.VisibleConversations.Count);

        var group = vm.Conversations.Single(c => c.Model.IsGroup);
        vm.Selected = group;
        await Shots.Settle(() => vm.Messages.Count >= 5 && !vm.IsLoadingThread);
        var rcs = vm.Messages.Single(m => m.Text.StartsWith("sent over RCS"));
        var sms = vm.Messages.Single(m => m.Text.StartsWith("sent over SMS"));
        Assert.True(rcs.IsGreenBubble);
        Assert.True(sms.IsBlue);
        Assert.True(vm.Messages.Single(m => m.Text.StartsWith("made it")).IsTheirs);
        Assert.Contains("\n", vm.Messages.Single(m => m.Text.StartsWith("😂😂")).Text);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Shots.Capture(w, "main-dark-group");
        vm.ComposerText = "typing 😂🔥❤️ 👍🏽 ‼️ ok";
        Shots.Capture(w, "main-dark-composer");
        vm.ComposerText = "";

        vm.Selected = vm.Conversations.Single(c => !c.Model.IsGroup);
        await Shots.Settle(() => vm.Messages.Any(m => m.Text == "unread one") && !vm.IsLoadingThread);
        // The stub's photo is not a real image: the bubble must say so instead of staying blank.
        var photo = vm.Messages.SelectMany(m => m.Attachments).Single(a => a.IsImage);
        await Shots.Settle(() => photo.Status.Length > 0 || photo.Image is not null);
        Assert.Null(photo.Image);
        Assert.Equal("preview unavailable", photo.Status);
        Shots.Capture(w, "main-dark-dm");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        vm.Selected = group;
        await Shots.Settle(() => vm.Messages.Count >= 5 && !vm.IsLoadingThread);
        Shots.Capture(w, "main-light-group");

        vm.Filter = "trail";
        Shots.Pump();
        Assert.Single(vm.VisibleConversations);
        vm.Filter = "";

        var newButton = w.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) as string == "New message");
        newButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Shots.Capture(w, "main-light-new-message");
        newButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var (hostKey, _) = Dialog.Build("Blip: confirm the Mac", Blip.Windows.App.HostKeyPrompt("SHA256:" + new string('A', 43)), "Trust", "Cancel");
        hostKey.Show();
        Shots.Capture(hostKey, "dialog-host-key");
        hostKey.Close();

        var settings = new SettingsWindow(new SettingsViewModel(cfg with { HostKey = "SHA256:" + new string('A', 43) }), firstRun: true);
        settings.Show();
        Shots.Capture(settings, "settings-light");
        settings.Close();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        vm.SaveSnapshot();
    }
}
