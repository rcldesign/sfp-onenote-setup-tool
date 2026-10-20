using Sfp.Core.Manifests;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>A run of inked pixel columns along a horizontal band, in points.</summary>
internal sealed record InkCluster(double LeftPt, double RightPt)
{
    public double CenterPt => (LeftPt + RightPt) / 2;

    public double WidthPt => RightPt - LeftPt;
}

/// <summary>Pixel arithmetic for assertions on rendered bitmaps.</summary>
internal static class Pixels
{
    /// <summary>A region in points as a pixel rectangle at a DPI, clipped to the bitmap.</summary>
    public static SKRectI ToPixels(RectPt rect, int dpi, SKBitmap bitmap)
    {
        double s = dpi / 72.0;
        SKRectI r = new(
            (int)Math.Floor(rect.X * s),
            (int)Math.Floor(rect.Y * s),
            (int)Math.Ceiling(rect.Right * s),
            (int)Math.Ceiling(rect.Bottom * s));
        r.Intersect(new SKRectI(0, 0, bitmap.Width, bitmap.Height));
        return r;
    }

    /// <summary>Mean color over a pixel rectangle.</summary>
    public static SKColor Mean(SKBitmap bitmap, SKRectI rect)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                r += c.Red;
                g += c.Green;
                b += c.Blue;
                n++;
            }
        }

        return n == 0 ? SKColors.Empty : new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>Mean color over a region in points.</summary>
    public static SKColor Mean(SKBitmap bitmap, RectPt region, int dpi) => Mean(bitmap, ToPixels(region, dpi, bitmap));

    /// <summary>The darkest pixel (smallest channel sum) in a pixel rectangle.</summary>
    public static SKColor Darkest(SKBitmap bitmap, SKRectI rect)
    {
        SKColor darkest = SKColors.White;
        int best = int.MaxValue;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                int sum = c.Red + c.Green + c.Blue;
                if (sum < best)
                {
                    best = sum;
                    darkest = c;
                }
            }
        }

        return darkest;
    }

    /// <summary>The darkest pixel in a region in points.</summary>
    public static SKColor Darkest(SKBitmap bitmap, RectPt region, int dpi) => Darkest(bitmap, ToPixels(region, dpi, bitmap));

    /// <summary>True when some pixel in the region (points) is at least <paramref name="minDarkening"/> darker than the background in the red channel.</summary>
    public static bool HasInk(SKBitmap bitmap, RectPt region, int dpi, SKColor background, int minDarkening = 40)
    {
        SKColor darkest = Darkest(bitmap, ToPixels(region, dpi, bitmap));
        return darkest.Red <= background.Red - minDarkening;
    }

    /// <summary>True when some pixel in the region (points) is at least <paramref name="minLightening"/> lighter than the background in the red channel (white ink).</summary>
    public static bool HasWhite(SKBitmap bitmap, RectPt region, int dpi, SKColor background, int minLightening = 20)
    {
        SKRectI r = ToPixels(region, dpi, bitmap);
        for (int y = r.Top; y < r.Bottom; y++)
        {
            for (int x = r.Left; x < r.Right; x++)
            {
                if (bitmap.GetPixel(x, y).Red >= background.Red + minLightening)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Runs of inked pixel columns across a horizontal band in points: a column counts as inked when any pixel in it
    /// is at least <paramref name="minDarkening"/> darker than the background; runs closer than <paramref name="mergeGapPt"/> merge.
    /// </summary>
    public static IReadOnlyList<InkCluster> InkClusters(SKBitmap bitmap, RectPt band, int dpi, SKColor background, double mergeGapPt, int minDarkening = 40)
    {
        SKRectI r = ToPixels(band, dpi, bitmap);
        double s = dpi / 72.0;
        int gapPx = (int)Math.Round(mergeGapPt * s);
        List<InkCluster> clusters = new();
        int? start = null;
        int lastInk = -1;
        for (int x = r.Left; x < r.Right; x++)
        {
            bool inked = false;
            for (int y = r.Top; y < r.Bottom && !inked; y++)
            {
                inked = bitmap.GetPixel(x, y).Red <= background.Red - minDarkening;
            }

            if (inked)
            {
                if (start is null || x - lastInk > gapPx)
                {
                    if (start is int s0)
                    {
                        clusters.Add(new InkCluster(s0 / s, (lastInk + 1) / s));
                    }

                    start = x;
                }

                lastInk = x;
            }
        }

        if (start is int s1)
        {
            clusters.Add(new InkCluster(s1 / s, (lastInk + 1) / s));
        }

        return clusters;
    }

    /// <summary>The most frequent color, sampling every <paramref name="step"/> pixels in each direction.</summary>
    public static SKColor Dominant(SKBitmap bitmap, int step = 7)
    {
        Dictionary<uint, int> counts = new();
        for (int y = 0; y < bitmap.Height; y += step)
        {
            for (int x = 0; x < bitmap.Width; x += step)
            {
                uint key = (uint)bitmap.GetPixel(x, y);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return new SKColor(counts.MaxBy(kv => kv.Value).Key);
    }
}
