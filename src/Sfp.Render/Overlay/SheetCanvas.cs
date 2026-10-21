using Sfp.Core;
using Sfp.Core.Manifests;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;
using SkiaSharp;

namespace Sfp.Render.Overlay;

/// <summary>Font metrics in points for layout: ascent and descent are positive distances from the baseline.</summary>
public sealed record TextMetricsPt(double Ascent, double Descent, double CapHeight, double XHeight);

/// <summary>
/// The overlay drawing surface: an <see cref="SKCanvas"/> over a sheet bitmap, with every coordinate taken in PDF points
/// (top-left origin) and scaled to pixels by dpi / 72 at draw time. Fonts passed to <see cref="Text(string, double, double, TextAlign, SKFont, SKColor, double)"/>
/// must already be at pixel size, which is what <see cref="IFontService.Font"/> gives when it is handed <see cref="Scale"/>.
/// Rectangle fills are not antialiased so an erase covers whole pixels; text, circles and rules are antialiased.
/// </summary>
public sealed class SheetCanvas : IDisposable
{
    private readonly SKBitmap bitmap;
    private readonly SKCanvas canvas;
    private readonly bool ownsBitmap;
    private bool disposed;

    /// <summary>Wraps an existing bitmap (a rasterized page); the caller keeps ownership of it.</summary>
    public SheetCanvas(SKBitmap bitmap, int dpi)
        : this(bitmap, dpi, ownsBitmap: false)
    {
    }

    private SheetCanvas(SKBitmap bitmap, int dpi, bool ownsBitmap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);
        this.bitmap = bitmap;
        Dpi = dpi;
        Scale = dpi / 72f;
        this.ownsBitmap = ownsBitmap;
        canvas = new SKCanvas(bitmap);
    }

    /// <summary>The bitmap being drawn on, flushed. For a blank sheet it is disposed with the canvas.</summary>
    public SKBitmap Bitmap
    {
        get
        {
            canvas.Flush();
            return bitmap;
        }
    }

    public int Dpi { get; }

    /// <summary>Points to pixels factor (dpi / 72).</summary>
    public float Scale { get; }

    /// <summary>Sheet width in points (pixels / scale).</summary>
    public double WidthPt => bitmap.Width / (double)Scale;

    /// <summary>Sheet height in points (pixels / scale).</summary>
    public double HeightPt => bitmap.Height / (double)Scale;

    /// <summary>A blank sheet of the given size in points at a DPI, filled with the background color. The canvas owns the bitmap.</summary>
    public static SheetCanvas Blank(double widthPt, double heightPt, int dpi, SKColor background)
    {
        int w = Rasterizer.PixelsFor(widthPt, dpi);
        int h = Rasterizer.PixelsFor(heightPt, dpi);
        SKBitmap bitmap = new(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(background);
        return new SheetCanvas(bitmap, dpi, ownsBitmap: true);
    }

    /// <summary>Converts points to pixels on this sheet.</summary>
    public float Px(double pt) => (float)(pt * Scale);

    /// <summary>Paints a rectangle in the background color, removing printed art under it.</summary>
    public void Erase(RectPt rect, SKColor background) => FillRect(rect, background);

    /// <summary>Fills a rectangle (not antialiased, so edges snap to whole pixels).</summary>
    public void FillRect(RectPt rect, SKColor color)
    {
        using SKPaint paint = new() { Color = color, Style = SKPaintStyle.Fill, IsAntialias = false };
        canvas.DrawRect(ToPixels(rect), paint);
    }

    /// <summary>Strokes a rectangle outline with the given line width in points.</summary>
    public void RectOutline(RectPt rect, double widthPt, SKColor color)
    {
        using SKPaint paint = StrokePaint(widthPt, color);
        canvas.DrawRect(ToPixels(rect), paint);
    }

    /// <summary>
    /// Draws text with its baseline at (x, baselineY) in points, anchored on x by <paramref name="align"/>.
    /// <paramref name="font"/> is at pixel size already (see <see cref="IFontService.Font"/> with <see cref="Scale"/>).
    /// </summary>
    public void Text(string text, double x, double baselineY, TextAlign align, SKFont font, SKColor color, double opacity = 1.0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using SKPaint paint = new() { Color = ColorParser.WithOpacity(color, opacity), IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawText(text, Px(x), Px(baselineY), ToSkia(align), font, paint);
    }

    /// <summary>Draws text in the service's face at a point size, creating the font at this sheet's scale.</summary>
    public void Text(string text, double x, double baselineY, TextAlign align, IFontService fonts, double sizePt, SKColor color, double opacity = 1.0, FontStyleKind style = FontStyleKind.Regular)
    {
        using SKFont font = fonts.Font(sizePt, Scale, style);
        Text(text, x, baselineY, align, font, color, opacity);
    }

    /// <summary>Advance width of the text in points for a font at this sheet's pixel size.</summary>
    public double TextWidth(string text, SKFont font) => string.IsNullOrEmpty(text) ? 0 : font.MeasureText(text) / Scale;

    /// <summary>
    /// The box the text's glyphs actually ink, in points relative to its origin (x from the start of the advance, y from
    /// the baseline, negative upward). Unlike the advance width it leaves out the side bearings, which differ between
    /// glyphs (a "1" sits inside a wide advance), so it is what centers text on a mark.
    /// </summary>
    public RectPt InkBounds(string text, SKFont font)
    {
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        font.MeasureText(text, out SKRect bounds);
        return new RectPt(bounds.Left / Scale, bounds.Top / Scale, bounds.Width / Scale, bounds.Height / Scale);
    }

    /// <summary>Ascent, descent, cap height and x-height in points for a font at this sheet's pixel size.</summary>
    public TextMetricsPt Measure(SKFont font)
    {
        SKFontMetrics m = font.Metrics;
        return new TextMetricsPt(-m.Ascent / Scale, m.Descent / Scale, m.CapHeight / Scale, m.XHeight / Scale);
    }

    /// <summary>A circle centered at (cx, cy) with the given diameter, filled or outlined (outline width in points).</summary>
    public void Circle(double cx, double cy, double diameterPt, SKColor color, bool filled, double outlineWidthPt = 1.0)
    {
        using SKPaint paint = filled
            ? new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true }
            : StrokePaint(outlineWidthPt, color);
        canvas.DrawCircle(Px(cx), Px(cy), Px(diameterPt / 2), paint);
    }

    /// <summary>A straight rule from (x1, y1) to (x2, y2) with a stroke width in points (never thinner than one pixel, as PDFium draws hairlines).</summary>
    public void Rule(double x1, double y1, double x2, double y2, double widthPt, SKColor color)
    {
        using SKPaint paint = StrokePaint(widthPt, color);
        canvas.DrawLine(Px(x1), Px(y1), Px(x2), Px(y2), paint);
    }

    /// <summary>The color of the pixel under a point coordinate.</summary>
    public SKColor PixelAt(double xPt, double yPt)
    {
        canvas.Flush();
        int px = Math.Clamp((int)Math.Floor(xPt * Scale), 0, bitmap.Width - 1);
        int py = Math.Clamp((int)Math.Floor(yPt * Scale), 0, bitmap.Height - 1);
        return bitmap.GetPixel(px, py);
    }

    /// <summary>Mean color over a rectangle in points (alpha ignored).</summary>
    public SKColor MeanColor(RectPt rect)
    {
        canvas.Flush();
        SKRectI r = SKRectI.Round(ToPixels(rect));
        r.Intersect(new SKRectI(0, 0, bitmap.Width, bitmap.Height));
        if (r.IsEmpty)
        {
            return SKColors.Empty;
        }

        long red = 0, green = 0, blue = 0;
        for (int y = r.Top; y < r.Bottom; y++)
        {
            for (int x = r.Left; x < r.Right; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                red += c.Red;
                green += c.Green;
                blue += c.Blue;
            }
        }

        long n = (long)r.Width * r.Height;
        return new SKColor((byte)(red / n), (byte)(green / n), (byte)(blue / n));
    }

    /// <summary>Finishes pending drawing so the bitmap can be read or encoded.</summary>
    public void Flush() => canvas.Flush();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        canvas.Dispose();
        if (ownsBitmap)
        {
            bitmap.Dispose();
        }
    }

    private static SKTextAlign ToSkia(TextAlign align) => align switch
    {
        TextAlign.Center => SKTextAlign.Center,
        TextAlign.Right => SKTextAlign.Right,
        _ => SKTextAlign.Left,
    };

    private SKRect ToPixels(RectPt rect) => new(Px(rect.X), Px(rect.Y), Px(rect.Right), Px(rect.Bottom));

    private SKPaint StrokePaint(double widthPt, SKColor color) => new()
    {
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(Px(widthPt), 1f),
        StrokeCap = SKStrokeCap.Butt,
        IsAntialias = true,
    };
}
