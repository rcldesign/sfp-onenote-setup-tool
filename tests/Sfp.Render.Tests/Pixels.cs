using Sfp.Core.Manifests;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>A run of inked pixel columns along a horizontal band, in points.</summary>
internal sealed record InkCluster(double LeftPt, double RightPt)
{
    public double CenterPt => (LeftPt + RightPt) / 2;

    public double WidthPt => RightPt - LeftPt;
}

/// <summary>
/// The weeks-remaining disc measured on a rendered sheet, in points: its center and radius, the center of the white
/// digits on it, and the clearance between those digits and the rim (the radius minus the farthest white pixel corner).
/// </summary>
internal sealed record DiscFit(double CenterX, double CenterY, double Radius, double WhiteCenterX, double WhiteCenterY, double RimClearance);

/// <summary>Pixel arithmetic for assertions on rendered bitmaps.</summary>
internal static class Pixels
{
    /// <summary>
    /// Finds the disc in a band (points) as the widest run of inked pixel columns, then the white pixels on it (at least
    /// <paramref name="minLightening"/> lighter than the background in the red channel) and how they sit in it.
    /// </summary>
    public static DiscFit Disc(SKBitmap bitmap, RectPt band, int dpi, SKColor background, int minDarkening = 40, int minLightening = 20)
    {
        double s = dpi / 72.0;
        SKRectI r = ToPixels(band, dpi, bitmap);
        bool Dark(int x, int y) => bitmap.GetPixel(x, y).Red <= background.Red - minDarkening;

        int bestLeft = -1, bestRight = -1, runLeft = -1, last = r.Left - 2;
        for (int x = r.Left; x < r.Right; x++)
        {
            bool inked = false;
            for (int y = r.Top; y < r.Bottom && !inked; y++)
            {
                inked = Dark(x, y);
            }

            if (!inked)
            {
                continue;
            }

            if (x - last > 1)
            {
                runLeft = x;
            }

            last = x;
            if (bestLeft < 0 || x - runLeft > bestRight - bestLeft)
            {
                (bestLeft, bestRight) = (runLeft, x);
            }
        }

        if (bestLeft < 0)
        {
            throw new InvalidOperationException("No ink in the band " + band + ".");
        }

        int top = int.MaxValue, bottom = int.MinValue;
        for (int x = bestLeft; x <= bestRight; x++)
        {
            for (int y = r.Top; y < r.Bottom; y++)
            {
                if (Dark(x, y))
                {
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        double cx = (bestLeft + bestRight + 1) / 2.0;
        double cy = (top + bottom + 1) / 2.0;
        double radius = (bestRight - bestLeft + 1) / 2.0;
        int whiteLeft = int.MaxValue, whiteRight = int.MinValue, whiteTop = int.MaxValue, whiteBottom = int.MinValue;
        double farthest = 0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = bestLeft; x <= bestRight; x++)
            {
                if (bitmap.GetPixel(x, y).Red < background.Red + minLightening)
                {
                    continue;
                }

                whiteLeft = Math.Min(whiteLeft, x);
                whiteRight = Math.Max(whiteRight, x);
                whiteTop = Math.Min(whiteTop, y);
                whiteBottom = Math.Max(whiteBottom, y);
                double dx = Math.Abs(x + 0.5 - cx) + 0.5;
                double dy = Math.Abs(y + 0.5 - cy) + 0.5;
                farthest = Math.Max(farthest, Math.Sqrt((dx * dx) + (dy * dy)));
            }
        }

        if (whiteLeft == int.MaxValue)
        {
            throw new InvalidOperationException("No white digits on the disc found in " + band + ".");
        }

        return new DiscFit(
            cx / s,
            cy / s,
            radius / s,
            (whiteLeft + whiteRight + 1) / 2.0 / s,
            (whiteTop + whiteBottom + 1) / 2.0 / s,
            (radius - farthest) / s);
    }

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

    /// <summary>
    /// Pixels inside <paramref name="region"/> (points) whose color differs from the background by more than
    /// <paramref name="tolerance"/> in some channel and whose center lies outside every allowed rectangle: printed art
    /// an erase missed, or overlay ink outside the boxes it was drawn in. Only whole pixels inside the region are
    /// checked, so a partly covered pixel on the region's edge never counts. Returns how many there are and up to five
    /// of them (in points) for a failure message.
    /// </summary>
    public static (int Count, IReadOnlyList<(double XPt, double YPt)> Samples) StrayInk(SKBitmap bitmap, RectPt region, IReadOnlyList<RectPt> allowed, int dpi, SKColor background, int tolerance = 3)
    {
        double s = dpi / 72.0;
        int left = Math.Max(0, (int)Math.Ceiling(region.X * s));
        int top = Math.Max(0, (int)Math.Ceiling(region.Y * s));
        int right = Math.Min(bitmap.Width, (int)Math.Floor(region.Right * s));
        int bottom = Math.Min(bitmap.Height, (int)Math.Floor(region.Bottom * s));
        int count = 0;
        List<(double, double)> samples = new();
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                if (Math.Abs(c.Red - background.Red) <= tolerance && Math.Abs(c.Green - background.Green) <= tolerance && Math.Abs(c.Blue - background.Blue) <= tolerance)
                {
                    continue;
                }

                double xPt = (x + 0.5) / s;
                double yPt = (y + 0.5) / s;
                if (allowed.Any(a => xPt >= a.X && xPt <= a.Right && yPt >= a.Y && yPt <= a.Bottom))
                {
                    continue;
                }

                count++;
                if (samples.Count < 5)
                {
                    samples.Add((xPt, yPt));
                }
            }
        }

        return (count, samples);
    }

    /// <summary>
    /// Printed art that survived a render: pixels inside <paramref name="region"/> (points) that are ink on the raw
    /// rasterization of the PDF page (some channel more than <paramref name="tolerance"/> from the background), are
    /// unchanged in the render (every channel within <paramref name="tolerance"/> of the raw pixel), and whose center
    /// lies outside every allowed rectangle (the boxes the overlay drew in). Passing the raw page as both bitmaps counts
    /// the printed ink outside the allowed boxes, which shows the check would see an erase that missed it. Only whole
    /// pixels inside the region are checked. Returns how many there are and up to five of them (in points).
    /// </summary>
    public static (int Count, IReadOnlyList<(double XPt, double YPt)> Samples) SurvivingPrint(SKBitmap raw, SKBitmap rendered, RectPt region, IReadOnlyList<RectPt> allowed, int dpi, SKColor background, int tolerance = 3)
    {
        if (raw.Width != rendered.Width || raw.Height != rendered.Height)
        {
            throw new ArgumentException($"The raw page is {raw.Width}x{raw.Height} and the render {rendered.Width}x{rendered.Height}; compare them at the same DPI.", nameof(rendered));
        }

        double s = dpi / 72.0;
        SKRectI r = WholePixels(region, dpi, raw);
        int count = 0;
        List<(double, double)> samples = new();
        for (int y = r.Top; y < r.Bottom; y++)
        {
            for (int x = r.Left; x < r.Right; x++)
            {
                SKColor p = raw.GetPixel(x, y);
                if (Near(p, background, tolerance) || !Near(rendered.GetPixel(x, y), p, tolerance))
                {
                    continue;
                }

                double xPt = (x + 0.5) / s;
                double yPt = (y + 0.5) / s;
                if (allowed.Any(a => xPt >= a.X && xPt <= a.Right && yPt >= a.Y && yPt <= a.Bottom))
                {
                    continue;
                }

                count++;
                if (samples.Count < 5)
                {
                    samples.Add((xPt, yPt));
                }
            }
        }

        return (count, samples);
    }

    /// <summary>
    /// Bounds in points (pixel edges) of the pixels inside <paramref name="region"/> whose color differs from the
    /// background by more than <paramref name="tolerance"/> in some channel, or null when there are none. Only whole
    /// pixels inside the region are checked.
    /// </summary>
    public static RectPt? InkBounds(SKBitmap bitmap, RectPt region, int dpi, SKColor background, int tolerance = 3) =>
        Bounds(bitmap, region, dpi, (x, y) => !Near(bitmap.GetPixel(x, y), background, tolerance));

    /// <summary>
    /// Bounds in points (pixel edges) of the pixels inside <paramref name="region"/> that differ between two bitmaps of the
    /// same page at the same DPI by more than <paramref name="tolerance"/> in some channel (what a render drew over the
    /// raw page), or null when none do. Only whole pixels inside the region are compared.
    /// </summary>
    public static RectPt? ChangedBounds(SKBitmap before, SKBitmap after, RectPt region, int dpi, int tolerance = 3)
    {
        if (before.Width != after.Width || before.Height != after.Height)
        {
            throw new ArgumentException($"The bitmaps are {before.Width}x{before.Height} and {after.Width}x{after.Height}; compare them at the same DPI.", nameof(after));
        }

        return Bounds(before, region, dpi, (x, y) => !Near(before.GetPixel(x, y), after.GetPixel(x, y), tolerance));
    }

    /// <summary>Bounds in points (pixel edges) of the whole pixels inside a region that a predicate marks, or null when it marks none.</summary>
    private static RectPt? Bounds(SKBitmap bitmap, RectPt region, int dpi, Func<int, int, bool> marked)
    {
        double s = dpi / 72.0;
        SKRectI r = WholePixels(region, dpi, bitmap);
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int y = r.Top; y < r.Bottom; y++)
        {
            for (int x = r.Left; x < r.Right; x++)
            {
                if (!marked(x, y))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return left == int.MaxValue ? null : new RectPt(left / s, top / s, (right - left) / s, (bottom - top) / s);
    }

    /// <summary>The pixels lying wholly inside a region in points, clipped to the bitmap.</summary>
    private static SKRectI WholePixels(RectPt region, int dpi, SKBitmap bitmap)
    {
        double s = dpi / 72.0;
        return new SKRectI(
            Math.Max(0, (int)Math.Ceiling(region.X * s)),
            Math.Max(0, (int)Math.Ceiling(region.Y * s)),
            Math.Min(bitmap.Width, (int)Math.Floor(region.Right * s)),
            Math.Min(bitmap.Height, (int)Math.Floor(region.Bottom * s)));
    }

    private static bool Near(SKColor a, SKColor b, int tolerance) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance && Math.Abs(a.Blue - b.Blue) <= tolerance;

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
