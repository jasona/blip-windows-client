using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
        // Grey antialiasing: Windows' default subpixel text has coloured fringes the pixel count would see.
        Avalonia.Media.RenderOptions.SetTextRenderingMode(w, Avalonia.Media.TextRenderingMode.Antialias);
        w.Show();
        var frame = Shots.Capture(w, "emoji-colour");

        Rect Bounds(Control c) => c.Bounds.Translate(c.TranslatePoint(default, w)!.Value - c.Bounds.Position);
        PixelRect Px(Control c) { var r = Bounds(c); return new PixelRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height); }

        Assert.True(Shots.ColourfulPixels(frame, Px(label)) > 400, "read-only emoji are not in colour");
        Assert.True(Shots.ColourfulPixels(frame, Px(box)) > 400, "emoji in the composer are not in colour");
        var plainColour = Shots.ColourfulPixels(frame, Px(plain));
        Assert.True(plainColour < 20, $"digits or '#' picked up the colour emoji font ({plainColour} colour pixels)");
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
        w.SidebarWidth = UiState.DefaultSidebar;   // not whatever an earlier run remembered
        w.Show();
        vm.Start();
        await Shots.Settle(() => vm.Conversations.Count >= 8 && vm.Status == Blip.Core.Live.BridgeStatus.Online);
        // Messages' five pins, in pin order, sit in the grid; the other three are the list.
        Assert.Equal(["Maya Okafor", "Trail Crew", "Leo Brandt", "Priya Natarajan", "Book Club"], vm.PinnedConversations.Select(c => c.Title));
        Assert.Equal(3, vm.VisibleConversations.Count);
        Assert.Equal(["Maya", "Trail Crew"], vm.PinnedConversations.Take(2).Select(c => c.ShortTitle));

        var group = vm.Conversations.Single(c => c.Title == "Trail Crew");
        vm.Selected = group;
        await Shots.Settle(() => vm.Messages.Count >= 5 && !vm.IsLoadingThread);
        var rcs = vm.Messages.Single(m => m.Text.StartsWith("sent over RCS"));
        var sms = vm.Messages.Single(m => m.Text.StartsWith("sent over SMS"));
        Assert.True(rcs.IsGreenBubble);
        Assert.True(sms.IsBlue);
        Assert.True(vm.Messages.Single(m => m.Text.StartsWith("made it")).IsTheirs);
        Assert.Contains("\n", vm.Messages.Single(m => m.Text.StartsWith("😂😂")).Text);

        // The newest bubble and its receipt line must sit fully above the composer, also after the
        // window shrinks or the composer grows (it used to stay clipped until a manual scroll).
        var scroll = w.FindControl<ScrollViewer>("ThreadScroll")!;
        void AssertAtBottom(string when)
        {
            for (var i = 0; i < 3; i++) { w.UpdateLayout(); Shots.Pump(); }
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, $"{when}: thread does not scroll, so the check proves nothing");
            // Measure what is drawn, not the scroll numbers: the newest row's bottom edge, inside the
            // scroll viewer, must be within its visible height.
            var rows = scroll.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("msg")).ToList();
            var last = rows.OrderBy(r => r.TranslatePoint(default, scroll)!.Value.Y).Last();
            var bottom = last.TranslatePoint(new Point(0, last.Bounds.Height), scroll)!.Value.Y;
            Assert.True(bottom <= scroll.Bounds.Height + 0.5, $"{when}: newest message clipped by {bottom - scroll.Bounds.Height:F0} px");
        }
        w.Height = 500;   // MinHeight
        vm.ComposerText = string.Join("\n", Enumerable.Range(1, 8).Select(i => $"line {i}"));
        AssertAtBottom("after the window shrank and the composer grew");
        Shots.Capture(w, "clip-check");
        vm.ComposerText = "";
        w.Height = 720;
        Shots.Pump();
        var newestMine = vm.Messages.Last(m => m.IsMine);
        if (newestMine.Model.Delivered == true || !string.IsNullOrEmpty(newestMine.Model.ReadAt))
            Assert.True(newestMine.HasReceipt, "no Delivered/Read line under the newest own message");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Shots.Capture(w, "main-dark-group");
        vm.ComposerText = "typing 😂🔥❤️ 👍🏽 ‼️ ok";
        Shots.Capture(w, "main-dark-composer");
        vm.ComposerText = "";

        vm.Selected = vm.Conversations.Single(c => c.Id == "+15550100011");
        await Shots.Settle(() => vm.Messages.Any(m => m.Text == "unread one") && !vm.IsLoadingThread);
        // The stub's photo is not a real image: the bubble must say so instead of staying blank.
        var photo = vm.Messages.SelectMany(m => m.Attachments).Single(a => a.Name == "photo.png");
        await Shots.Settle(() => photo.Status.Length > 0 || photo.Image is not null);
        Assert.Null(photo.Image);
        Assert.Equal("preview unavailable", photo.Status);
        // A real photo, stored sideways with EXIF Orientation 6 as an iPhone does: it must stand upright.
        var real = vm.Messages.SelectMany(m => m.Attachments).Single(a => a.Name == "IMG_0042.jpeg");
        await Shots.Settle(() => real.Image is not null);
        Assert.True(real.Image!.PixelSize.Width > real.Image.PixelSize.Height, $"photo drawn {real.Image.PixelSize}, not upright");
        Shots.Capture(w, "main-dark-dm");

        // A click on a photo opens the viewer inside the window, with the large copy.
        var thumb = w.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext == real && b.Classes.Contains("plain"));
        thumb.Command!.Execute(thumb.CommandParameter);
        await Shots.Settle(() => real.FullImage is not null);
        Assert.Same(real, vm.Viewing);
        Assert.Equal(new PixelSize(1600, 1200), real.FullImage!.PixelSize);
        Assert.False(vm.HasPreviousImage || vm.HasNextImage);   // the broken one is not a stop
        Assert.True(w.FindControl<Panel>("Viewer")!.IsVisible);
        Shots.Capture(w, "viewer");
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Shots.Pump();
        Assert.False(vm.IsViewing);
        // A click on the dark surround closes it too; a click on the photo does not.
        vm.ViewImageCommand.Execute(real);
        Shots.Pump();
        var shown = w.FindControl<Image>("ViewerImage")!;
        var onPhoto = shown.TranslatePoint(new Point(shown.Bounds.Width / 2, shown.Bounds.Height / 2), w)!.Value;
        w.MouseDown(onPhoto, MouseButton.Left);
        w.MouseUp(onPhoto, MouseButton.Left);
        Shots.Pump();
        Assert.True(vm.IsViewing);
        w.MouseDown(new Point(30, w.Bounds.Height / 2), MouseButton.Left);
        w.MouseUp(new Point(30, w.Bounds.Height / 2), MouseButton.Left);
        Shots.Pump();
        Assert.False(vm.IsViewing);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        vm.Selected = group;
        await Shots.Settle(() => vm.Messages.Count >= 5 && !vm.IsLoadingThread);
        Shots.Capture(w, "main-light-group");

        // One selection across the grid and the list: a pin open clears the list's highlight,
        // a click in the list opens that row and un-highlights the pin.
        var list = w.FindControl<ListBox>("ConversationList")!;
        var tiles = w.FindControl<ItemsControl>("PinnedGrid")!.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("pin")).ToList();
        Assert.Equal(5, tiles.Count);
        Assert.Null(list.SelectedItem);
        Assert.Contains("selected", tiles.Single(t => t.DataContext == group).Classes);
        var sam = vm.VisibleConversations.Single(c => c.Title == "Sam Rivera");
        list.SelectedItem = sam;
        Shots.Pump();
        Assert.Same(sam, vm.Selected);
        Assert.DoesNotContain(tiles, t => t.Classes.Contains("selected"));
        tiles.Single(t => t.DataContext is ConversationViewModel { Title: "Leo Brandt" }).Command!.Execute(tiles[2].CommandParameter);
        Shots.Pump();
        Assert.Equal("Leo Brandt", vm.Selected!.Title);
        Assert.Null(list.SelectedItem);
        Assert.Equal(3, w.PinnedColumns);

        // Drag the divider: the sidebar widens, the grid gains columns, the width is remembered.
        var splitter = w.GetVisualDescendants().OfType<GridSplitter>().Single();
        var grip = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, 300), w)!.Value;
        w.MouseDown(grip, MouseButton.Left);
        w.MouseMove(grip + new Point(120, 0));
        w.MouseMove(grip + new Point(240, 0));
        w.MouseUp(grip + new Point(240, 0), MouseButton.Left);
        Shots.Pump();
        Assert.InRange(w.SidebarWidth, UiState.DefaultSidebar + 200, UiState.MaxSidebar);
        Assert.Equal(w.SidebarWidth, UiState.Load().SidebarWidth, 1);
        Assert.Equal(5, w.PinnedColumns);   // one row of five: every pin fits across
        Shots.Capture(w, "sidebar-wide");
        w.SidebarWidth = 420;
        Shots.Pump();
        Assert.Equal(4, w.PinnedColumns);
        Shots.Capture(w, "sidebar-medium");
        w.SidebarWidth = 100;   // clamped
        Shots.Pump();
        Assert.Equal(UiState.MinSidebar, w.SidebarWidth, 1);
        Assert.Equal(3, w.PinnedColumns);
        Assert.True(w.PinnedAvatarSize >= 44);
        Shots.Capture(w, "sidebar-narrow");
        w.SidebarWidth = UiState.DefaultSidebar;
        Shots.Pump();

        // While searching every match is in the list and the grid steps aside.
        vm.Filter = "trail";
        Shots.Pump();
        Assert.Single(vm.VisibleConversations);
        Assert.False(vm.HasPinned);
        vm.Filter = "";
        Assert.Equal(5, vm.PinnedConversations.Count);

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
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Shots.Capture(settings, "settings-dark");
        settings.Close();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        vm.SaveSnapshot();
    }
}
