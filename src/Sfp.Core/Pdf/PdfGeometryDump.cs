using System.Globalization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;

namespace Sfp.Core.Pdf;

/// <summary>Reads the words and line segments of PDF pages so manifest coordinates can be checked against the real file.</summary>
public interface IPdfGeometryDump
{
    /// <summary>
    /// Dumps the given 1-based pages. Throws <see cref="FileNotFoundException"/> when the file is missing and
    /// <see cref="ArgumentOutOfRangeException"/> when a page number is outside the document.
    /// </summary>
    IReadOnlyList<PageGeometry> Dump(string pdfPath, IReadOnlyList<int> pages);
}

/// <summary>
/// The one production <see cref="IPdfGeometryDump"/>, built on PdfPig. PdfPig reports a bottom-left origin; every
/// y here is converted to the manifest's top-left origin with the page height.
/// </summary>
public sealed class PdfGeometryDump : IPdfGeometryDump
{
    /// <summary>
    /// Pages the measure tool dumps when none are given: the cover, the first calendar, and the first week's overview,
    /// Monday and reflection of the 2023 template. These are page numbers, not measured geometry.
    /// </summary>
    public static IReadOnlyList<int> DefaultPages { get; } = new[] { 1, 13, 16, 17, 24 };

    /// <summary>A filled rectangle no thicker than this is reported as a rule (planner PDFs draw some rules as fills).</summary>
    public const double ThinFillPt = 2.0;

    public IReadOnlyList<PageGeometry> Dump(string pdfPath, IReadOnlyList<int> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentNullException.ThrowIfNull(pages);
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException($"PDF not found: {pdfPath}", pdfPath);
        }

        using PdfDocument document = PdfDocument.Open(pdfPath);
        List<PageGeometry> result = new(pages.Count);
        foreach (int number in pages)
        {
            if (number < 1 || number > document.NumberOfPages)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pages),
                    number,
                    $"Page {number.ToString(CultureInfo.InvariantCulture)} is outside the document ({document.NumberOfPages.ToString(CultureInfo.InvariantCulture)} pages).");
            }

            result.Add(DumpPage(document.GetPage(number)));
        }

        return result;
    }

    private static PageGeometry DumpPage(Page page)
    {
        double height = page.Height;
        List<WordGeometry> words = page.GetWords()
            .Where(w => w.Letters.Count > 0)
            .Select(w => ToWord(w, height))
            .OrderBy(w => w.Baseline)
            .ThenBy(w => w.X)
            .ToList();

        List<LineGeometry> lines = new();
        foreach (PdfPath path in page.Paths)
        {
            CollectLines(path, height, lines);
        }

        lines = lines.OrderBy(l => l.Y1).ThenBy(l => l.X1).ToList();
        return new PageGeometry(page.Number, page.Width, height, words, lines);
    }

    private static WordGeometry ToWord(Word word, double pageHeight)
    {
        IReadOnlyList<Letter> letters = word.Letters;
        double x = letters.Min(l => l.StartBaseLine.X);
        double right = letters.Max(l => l.EndBaseLine.X);
        double top = pageHeight - letters.Max(l => l.BoundingBox.Top);
        double bottom = pageHeight - letters.Min(l => l.BoundingBox.Bottom);
        double baseline = pageHeight - letters[0].StartBaseLine.Y;
        double size = letters.Max(l => l.PointSize);
        return new WordGeometry(word.Text, x, top, bottom, right, size, word.FontName ?? "", baseline);
    }

    private static void CollectLines(PdfPath path, double pageHeight, List<LineGeometry> lines)
    {
        if (path.IsClipping)
        {
            return;
        }

        if (path.IsStroked)
        {
            double width = path.LineWidth;
            foreach (PdfSubpath subpath in path)
            {
                CollectStrokedSegments(subpath, width, pageHeight, lines);
            }

            return;
        }

        if (path.IsFilled)
        {
            foreach (PdfSubpath subpath in path)
            {
                CollectThinFill(subpath, pageHeight, lines);
            }
        }
    }

    private static void CollectStrokedSegments(PdfSubpath subpath, double width, double pageHeight, List<LineGeometry> lines)
    {
        PdfPoint? start = null;
        PdfPoint? current = null;
        foreach (PdfSubpath.IPathCommand command in subpath.Commands)
        {
            switch (command)
            {
                case PdfSubpath.Move move:
                    start = move.Location;
                    current = move.Location;
                    break;
                case PdfSubpath.Line line:
                    lines.Add(Segment(line.From, line.To, width, pageHeight));
                    current = line.To;
                    break;
                case PdfSubpath.Close when start is not null && current is not null && current.Value != start.Value:
                    lines.Add(Segment(current.Value, start.Value, width, pageHeight));
                    current = start;
                    break;
                case PdfSubpath.BezierCurve curve:
                    // Curves are not line-like; only their end point advances the current point.
                    current = curve.EndPoint;
                    break;
                default:
                    break;
            }
        }
    }

    private static void CollectThinFill(PdfSubpath subpath, double pageHeight, List<LineGeometry> lines)
    {
        if (!subpath.IsDrawnAsRectangle)
        {
            return;
        }

        PdfRectangle? bounds = subpath.GetBoundingRectangle();
        if (bounds is null)
        {
            return;
        }

        PdfRectangle r = bounds.Value;
        if (r.Height <= ThinFillPt && r.Width > r.Height)
        {
            double y = (r.Top + r.Bottom) / 2;
            lines.Add(Segment(new PdfPoint(r.Left, y), new PdfPoint(r.Right, y), r.Height, pageHeight));
        }
        else if (r.Width <= ThinFillPt && r.Height > r.Width)
        {
            double x = (r.Left + r.Right) / 2;
            lines.Add(Segment(new PdfPoint(x, r.Top), new PdfPoint(x, r.Bottom), r.Width, pageHeight));
        }
    }

    private static LineGeometry Segment(PdfPoint from, PdfPoint to, double width, double pageHeight) =>
        new(from.X, pageHeight - from.Y, to.X, pageHeight - to.Y, width);
}
