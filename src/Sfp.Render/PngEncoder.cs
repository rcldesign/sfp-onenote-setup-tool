using SkiaSharp;

namespace Sfp.Render;

/// <summary>
/// Encodes a bitmap as the 24-bit RGB PNG the PRD asks for: no alpha channel (IHDR color type 2), so OneNote stores the
/// sheet as an opaque image. The pixels are converted to <see cref="SKColorType.Rgb888x"/> first; Skia's encoder writes
/// that layout as plain RGB. Encoding is deterministic for identical pixels. Every Skia object the encoder creates (the
/// converted bitmap, the pixmap and the encoded data) is disposed before it returns, so a page's buffers are released
/// with the page instead of waiting for the finalizer.
/// </summary>
public static class PngEncoder
{
    /// <summary>Encodes the bitmap as an opaque 24-bit RGB PNG.</summary>
    public static byte[] Encode(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.ColorType == SKColorType.Rgb888x)
        {
            return EncodeRgb(bitmap);
        }

        using SKBitmap rgb = ToRgb888x(bitmap);
        return EncodeRgb(rgb);
    }

    /// <summary>
    /// A copy of the bitmap in the Rgb888x layout (opaque, 4 bytes per pixel with the fourth ignored), read straight from
    /// the source pixels (never through SKBitmap.Copy, whose shader kept a full-size snapshot alive per call).
    /// </summary>
    /// <exception cref="InvalidOperationException">Skia could not read or convert the source pixels.</exception>
    public static SKBitmap ToRgb888x(SKBitmap bitmap) => BitmapCopy.Convert(bitmap, SKColorType.Rgb888x, SKAlphaType.Opaque);

    private static byte[] EncodeRgb(SKBitmap rgb)
    {
        using SKPixmap pixels = rgb.PeekPixels()
            ?? throw new InvalidOperationException("Skia exposes no pixels for the bitmap to encode.");
        using SKData data = pixels.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Skia could not encode the bitmap as PNG.");
        return data.ToArray();
    }
}
