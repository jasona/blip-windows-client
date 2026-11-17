using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Blip.Windows.Services;

namespace Blip.Ui.Tests;

public class ImageDecodeTests
{
    private static byte[] Fixture(int orientation) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"orient-{orientation}.jpg"));

    private static (byte r, byte g, byte b) Pixel(Bitmap bmp, int x, int y)
    {
        var buf = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally { handle.Free(); }
        return (buf[2], buf[1], buf[0]);   // BGRA
    }

    private static void AssertUpright(Bitmap bmp, int w, int h, string what)
    {
        Assert.Equal(new PixelSize(w, h), bmp.PixelSize);
        var (r, g, b) = Pixel(bmp, w / 10, h / 8);
        Assert.True(r > 180 && g < 90 && b < 90, $"{what}: top-left should be red, got {r},{g},{b}");
        (r, g, b) = Pixel(bmp, w - 1 - w / 10, h / 8);
        Assert.True(b > 180 && r < 90 && g < 90, $"{what}: top-right should be blue, got {r},{g},{b}");
        (r, g, b) = Pixel(bmp, w / 10, h - 1 - h / 8);
        Assert.True(g > 180 && r < 90 && b < 90, $"{what}: bottom-left should be green, got {r},{g},{b}");
    }

    /// <summary>iPhone photos are stored sideways with an EXIF Orientation tag; they must show upright.</summary>
    [AvaloniaTheory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void Photos_follow_their_EXIF_orientation(int orientation)
    {
        AssertUpright(ImageDecode.ToWidth(Fixture(orientation), 640), 60, 40, $"orientation {orientation}");
        AssertUpright(ImageDecode.ToWidth(Fixture(orientation), 30), 30, 20, $"orientation {orientation}, scaled");
    }

    [AvaloniaFact]
    public void Avalonia_alone_ignores_the_tag()
    {
        // Proof the fixture exercises the bug: the plain decoder leaves a "rotate 90°" photo on its side.
        Assert.Equal(new PixelSize(40, 60), new Bitmap(new MemoryStream(Fixture(6))).PixelSize);
    }
}
