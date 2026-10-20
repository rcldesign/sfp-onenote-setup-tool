using System.Globalization;

namespace Sfp.Core.Pdf;

/// <summary>Prints page geometry as fixed-width text tables, one block per page, for reading beside the manifest.</summary>
public static class PdfGeometryTable
{
    private const string WordHeader = "text                            x    base     top  bottom   right  size  font";
    private const string LineHeader = "     x1      y1      x2      y2   width";

    /// <summary>Writes every page.</summary>
    public static void Write(TextWriter writer, IEnumerable<PageGeometry> pages)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(pages);
        bool first = true;
        foreach (PageGeometry page in pages)
        {
            if (!first)
            {
                writer.WriteLine();
            }

            first = false;
            Write(writer, page);
        }
    }

    /// <summary>Writes one page: a heading, the words table, then the lines table.</summary>
    public static void Write(TextWriter writer, PageGeometry page)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(page);
        CultureInfo inv = CultureInfo.InvariantCulture;
        writer.WriteLine(string.Format(
            inv,
            "== Page {0}: {1} x {2} pt, {3} words, {4} lines ==",
            page.PageNumber,
            page.WidthPt.ToString("0.#", inv),
            page.HeightPt.ToString("0.#", inv),
            page.Words.Count,
            page.Lines.Count));

        writer.WriteLine("WORDS (pt from the top-left corner: x = glyph origin, base = baseline, top and bottom = ink, right = advance end)");
        writer.WriteLine(WordHeader);
        foreach (WordGeometry w in page.Words)
        {
            writer.WriteLine(string.Format(
                inv,
                "{0,-28}{1,8:0.0}{2,8:0.0}{3,8:0.0}{4,8:0.0}{5,8:0.0}{6,6:0.0}  {7}",
                w.Text,
                w.X,
                w.Baseline,
                w.Top,
                w.Bottom,
                w.Right,
                w.FontSizePt,
                w.FontName));
        }

        writer.WriteLine("LINES (pt from the top-left corner; width = stroke width, or the thickness of a thin filled rectangle)");
        writer.WriteLine(LineHeader);
        foreach (LineGeometry l in page.Lines)
        {
            writer.WriteLine(string.Format(inv, "{0,8:0.0}{1,8:0.0}{2,8:0.0}{3,8:0.0}{4,8:0.00}", l.X1, l.Y1, l.X2, l.Y2, l.StrokeWidth));
        }
    }

    /// <summary>The tables of every page as one string.</summary>
    public static string Format(IEnumerable<PageGeometry> pages)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        Write(writer, pages);
        return writer.ToString();
    }
}
