using SkiaSharp;

namespace Sfp.Render;

/// <summary>
/// Encodes a bitmap as the 24-bit RGB PNG the PRD asks for: no alpha channel (IHDR color type 2), so OneNote stores the
/// sheet as an opaque image. The pixels are converted to <see cref="SKColorType.Rgb888x"/> first; Skia's encoder writes
/// that layout as plain RGB. Encoding is deterministic for identical pixels.
/// </summary>
public static class PngEncoder
{
    /// <summary>Encodes the bitmap as an opaque 24-bit RGB PNG.</summary>
    public static byte[] Encode(SKBitmap bitmap)
    {
        if (bitmap.ColorType == SKColorType.Rgb888x)
        {
            return EncodeRgb(bitmap);
        }

        using SKBitmap rgb = ToRgb888x(bitmap);
        return EncodeRgb(rgb);
    }

    /// <summary>A copy of the bitmap in the Rgb888x layout (opaque, 4 bytes per pixel with the fourth ignored).</summary>
    public static SKBitmap ToRgb888x(SKBitmap bitmap)
    {
        SKBitmap? copy = bitmap.Copy(SKColorType.Rgb888x);
        if (copy is not null)
        {
            return copy;
        }

        // Some source layouts cannot be converted in place; drawing through a canvas always works.
        SKBitmap drawn = new(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgb888x, SKAlphaType.Opaque));
        using SKCanvas canvas = new(drawn);
        canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.Flush();
        return drawn;
    }

    private static byte[] EncodeRgb(SKBitmap rgb)
    {
        using SKData data = rgb.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Skia could not encode the bitmap as PNG.");
        return data.ToArray();
    }
}
