using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Blip.Windows.Services;

/// <summary>
/// Decodes photos the way they were taken. iPhone pictures are stored sideways with an EXIF
/// Orientation tag (sips keeps the tag when the Mac converts HEIC to JPEG), and Avalonia's own
/// decoder ignores it, so portraits came out lying on their side.
/// </summary>
public static class ImageDecode
{
    /// <summary>Decode, apply the EXIF orientation, and scale down to at most <paramref name="maxWidth"/> wide.</summary>
    public static Bitmap ToWidth(byte[] bytes, int maxWidth)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return Bitmap.DecodeToWidth(new MemoryStream(bytes), maxWidth);

        var origin = codec.EncodedOrigin;
        var info = codec.Info;
        int w = info.Width, h = info.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int ow = swap ? h : w, oh = swap ? w : h;   // as displayed
        var scale = Math.Min(1.0, (double)maxWidth / ow);
        int dw = Math.Max(1, (int)Math.Round(ow * scale)), dh = Math.Max(1, (int)Math.Round(oh * scale));

        // Let the codec do most of the shrinking (JPEG decodes at 1/2, 1/4, 1/8 for free).
        var sampled = codec.GetScaledDimensions((float)Math.Max(scale, 1.0 / 8));
        if (sampled.Width < (swap ? dh : dw) || sampled.Height < (swap ? dw : dh)) sampled = info.Size;
        using var src = new SKBitmap(new SKImageInfo(sampled.Width, sampled.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var r = codec.GetPixels(src.Info, src.GetPixels());
        if (r is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return Bitmap.DecodeToWidth(new MemoryStream(bytes), maxWidth);

        using var dst = new SKBitmap(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.Transparent);
            var s = swap ? (float)dw / src.Height : (float)dw / src.Width;
            canvas.SetMatrix(SKMatrix.CreateScale(s, s).PreConcat(Orient(origin, src.Width, src.Height)));
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.DrawBitmap(src, 0, 0, paint);
        }

        var wb = new WriteableBitmap(new PixelSize(dw, dh), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            var pixels = dst.Bytes;
            for (var y = 0; y < dh; y++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, y * dst.RowBytes, fb.Address + y * fb.RowBytes, dw * 4);
        }
        return wb;
    }

    /// <summary>Maps stored pixels (w × h) onto the upright picture, per the EXIF Orientation values 1–8.</summary>
    internal static SKMatrix Orient(SKEncodedOrigin o, float w, float h) => o switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix { ScaleX = -1, TransX = w, ScaleY = 1, Persp2 = 1 },
        SKEncodedOrigin.BottomRight => new SKMatrix { ScaleX = -1, TransX = w, ScaleY = -1, TransY = h, Persp2 = 1 },
        SKEncodedOrigin.BottomLeft => new SKMatrix { ScaleX = 1, ScaleY = -1, TransY = h, Persp2 = 1 },
        SKEncodedOrigin.LeftTop => new SKMatrix { SkewX = 1, SkewY = 1, Persp2 = 1 },
        SKEncodedOrigin.RightTop => new SKMatrix { SkewX = -1, TransX = h, SkewY = 1, Persp2 = 1 },
        SKEncodedOrigin.RightBottom => new SKMatrix { SkewX = -1, TransX = h, SkewY = -1, TransY = w, Persp2 = 1 },
        SKEncodedOrigin.LeftBottom => new SKMatrix { SkewX = 1, SkewY = -1, TransY = w, Persp2 = 1 },
        _ => SKMatrix.Identity,
    };
}
