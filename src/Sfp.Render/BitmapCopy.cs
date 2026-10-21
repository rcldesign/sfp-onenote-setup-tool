using System.Globalization;
using SkiaSharp;

namespace Sfp.Render;

/// <summary>
/// Pixel copies that never go through a Skia shader. SKBitmap.Copy draws the source through a bitmap shader whose
/// managed wrapper is never disposed (SkiaSharp 4.150.1); for a mutable source that shader also snapshots every pixel,
/// so each copy left a full-size buffer (32 MB at 220 DPI) waiting for the finalizer, and for a cached immutable source
/// it kept an evicted page's pixels alive. These copies allocate the destination and read the source's pixels straight
/// into it (converting the layout when asked), so nothing outlives the call except the bitmap the caller owns.
/// </summary>
internal static class BitmapCopy
{
    /// <summary>An independent copy of <paramref name="source"/> in its own layout (size, color type, alpha type, color space).</summary>
    public static SKBitmap Clone(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Read(source, source.Info);
    }

    /// <summary>A copy of <paramref name="source"/> converted to the given color and alpha type, same size and color space.</summary>
    public static SKBitmap Convert(SKBitmap source, SKColorType colorType, SKAlphaType alphaType)
    {
        ArgumentNullException.ThrowIfNull(source);
        SKImageInfo info = source.Info;
        info.ColorType = colorType;
        info.AlphaType = alphaType;
        return Read(source, info);
    }

    private static SKBitmap Read(SKBitmap source, SKImageInfo info)
    {
        SKBitmap copy = new(info);
        try
        {
            using SKPixmap pixels = source.PeekPixels()
                ?? throw new InvalidOperationException(Describe("Skia exposes no pixels for a {0}x{1} {2} bitmap.", source));
            if (!pixels.ReadPixels(copy.Info, copy.GetPixels(), copy.RowBytes, 0, 0))
            {
                throw new InvalidOperationException(Describe("Skia could not copy a {0}x{1} {2} bitmap to " + info.ColorType + ".", source));
            }

            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private static string Describe(string format, SKBitmap source) =>
        string.Format(CultureInfo.InvariantCulture, format, source.Width, source.Height, source.ColorType);
}
