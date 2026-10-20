using SkiaSharp;

namespace Sfp.Render;

/// <summary>Size of a PDF page in points.</summary>
public sealed record PdfPageSize(double WidthPt, double HeightPt)
{
    /// <summary>Pixel size of this page at a DPI: Math.Round(pt * dpi / 72), so 1112 x 770 pt at 220 DPI is 3398 x 2353.</summary>
    public (int WidthPx, int HeightPx) PixelsAt(int dpi) => (Rasterizer.PixelsFor(WidthPt, dpi), Rasterizer.PixelsFor(HeightPt, dpi));
}

/// <summary>
/// Rasterizes pages of a PDF to bitmaps. One instance keeps each PDF it has seen open and caches rendered pages by
/// (path, page, dpi); every call returns a bitmap the caller owns and may draw on. Dispose releases the documents and the cache.
/// </summary>
public interface IRasterizer : IDisposable
{
    /// <summary>Renders a 1-based page at the DPI into a new bitmap owned by the caller (background opaque, no annotations, no form fill).</summary>
    SKBitmap Render(string pdfPath, int pdfPage1Based, int dpi);

    /// <summary>Number of pages in the PDF.</summary>
    int PageCount(string pdfPath);

    /// <summary>Size of a 1-based page in points.</summary>
    PdfPageSize PageSize(string pdfPath, int pdfPage1Based);
}
