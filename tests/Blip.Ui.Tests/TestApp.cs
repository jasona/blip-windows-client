using Avalonia;
using Avalonia.Headless;
using Blip.Ui.Tests;
using Blip.Windows;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Blip.Ui.Tests;

/// <summary>The real App (styles, themes, fonts) on Avalonia's headless platform, drawn by real Skia.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Blip.Windows.App>()
            .UseSkia()
            .WithBlipFonts()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
