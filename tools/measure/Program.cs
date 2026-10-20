using System.Globalization;
using Sfp.Core.Pdf;

namespace Sfp.Measure;

/// <summary>
/// sfp-measure PDF [page ...]: prints every word (text, x, baseline, top, bottom, right, font size, font name) and every
/// line-like segment (x1, y1, x2, y2, stroke width) of the given pages in PDF points with a top-left origin, so manifest
/// coordinates can be checked against the real file. Without page numbers it dumps the cover, the first calendar and
/// the first week's overview, Monday and reflection. Exit 0 on success, 2 for a usage or file problem.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine("usage: sfp-measure <pdf> [page ...]   (default pages: " + string.Join(" ", PdfGeometryDump.DefaultPages) + ")");
            return 2;
        }

        string pdfPath = args[0];
        List<int> pages = new();
        foreach (string arg in args.Skip(1))
        {
            if (!int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int page) || page < 1)
            {
                Console.Error.WriteLine($"Page numbers must be positive integers, got '{arg}'.");
                return 2;
            }

            pages.Add(page);
        }

        if (pages.Count == 0)
        {
            pages.AddRange(PdfGeometryDump.DefaultPages);
        }

        if (!File.Exists(pdfPath))
        {
            Console.Error.WriteLine($"PDF not found: {pdfPath}");
            return 2;
        }

        try
        {
            IReadOnlyList<PageGeometry> geometry = new PdfGeometryDump().Dump(pdfPath, pages);
            PdfGeometryTable.Write(Console.Out, geometry);
            return 0;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Top-level handler of a developer tool: a damaged PDF is reported in one line, not as a stack trace.
            Console.Error.WriteLine($"Could not read PDF {pdfPath}: {ex.Message}");
            return 2;
        }
    }
}
