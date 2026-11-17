using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Blip.Ui.Tests;

internal static class Shots
{
    /// <summary>Where screenshots go: BLIP_UI_SHOTS, or the test output folder.</summary>
    public static string Dir
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("BLIP_UI_SHOTS") is { Length: > 0 } e ? e : Path.Combine(AppContext.BaseDirectory, "shots");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static void Pump(int rounds = 5)
    {
        for (var i = 0; i < rounds; i++) Dispatcher.UIThread.RunJobs();
    }

    public static async Task Settle(Func<bool> until, int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!until() && Environment.TickCount64 < deadline)
        {
            Pump();
            await Task.Delay(50);
        }
        Pump();
    }

    public static WriteableBitmap Capture(Window w, string name)
    {
        Pump();
        var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)!;
        frame.Save(Path.Combine(Dir, name + ".png"));
        return (WriteableBitmap)frame;
    }

    /// <summary>Count pixels that are clearly coloured (not grey, white, black, or the theme's blue).</summary>
    public static int ColourfulPixels(WriteableBitmap bmp, Avalonia.PixelRect area)
    {
        using var fb = bmp.Lock();
        var n = 0;
        unsafe
        {
            for (var y = area.Y; y < area.Bottom; y++)
            {
                var row = (byte*)fb.Address + y * fb.RowBytes;
                for (var x = area.X; x < area.Right; x++)
                {
                    var p = row + x * 4;   // BGRA or RGBA: hue tests below are symmetric enough for "yellow/red"
                    int a = p[0], b = p[1], c = p[2];
                    var max = Math.Max(a, Math.Max(b, c));
                    var min = Math.Min(a, Math.Min(b, c));
                    if (max - min > 90 && max > 150) n++;
                }
            }
        }
        return n;
    }
}
