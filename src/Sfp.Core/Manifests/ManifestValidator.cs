using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Sfp.Core.Manifests;

/// <summary>
/// Checks a PDF against the manifest with PdfPig: the page count, every page size within <see cref="SizeTolerancePt"/>
/// of pageSizePt, and a text probe per page type (each fixed type on its first PDF page, the weekly probes on week run 1).
/// A missing or unreadable file is a validation error, never an exception. Messages follow the PRD:
/// "Expected 132 pages, found 130", "Page 5 is 612 x 792 pt, expected 1112 x 770", "Page 17 does not contain 'Monday'".
/// </summary>
public sealed partial class ManifestValidator : IManifestValidator
{
    /// <summary>A page may differ from the manifest size by this much in either dimension.</summary>
    public const double SizeTolerancePt = 1.0;

    public ValidationResult Validate(Manifest m, string pdfPath)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            return ValidationResult.Fail("No PDF path was given.");
        }

        if (!File.Exists(pdfPath))
        {
            return ValidationResult.Fail($"PDF not found: {pdfPath}");
        }

        List<string> errors = new();
        List<string> warnings = new();
        try
        {
            using PdfDocument document = PdfDocument.Open(pdfPath);
            int found = document.NumberOfPages;
            if (found != m.PageCount)
            {
                errors.Add($"Expected {Int(m.PageCount)} pages, found {Int(found)}");
            }

            CheckSizes(m, document, errors);
            CheckProbes(m, document, errors, warnings);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // PdfPig reports a damaged or non-PDF file through several exception types; all of them mean one thing here.
            return ValidationResult.Fail($"Could not read PDF {pdfPath}: {ex.Message}");
        }

        return new ValidationResult(errors, warnings);
    }

    /// <summary>
    /// True when the probe appears in the page text, comparing case-insensitively with whitespace collapsed. As a
    /// fallback for text whose letters were split by kerning, the comparison is repeated with all whitespace removed.
    /// </summary>
    public static bool ContainsProbe(string pageText, string probe)
    {
        ArgumentNullException.ThrowIfNull(pageText);
        ArgumentNullException.ThrowIfNull(probe);
        string text = Collapse(pageText);
        string needle = Collapse(probe);
        if (needle.Length == 0)
        {
            return true;
        }

        if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Strip(text).Contains(Strip(needle), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The text of a page as the probes see it: words joined by single spaces, then the raw letter stream.</summary>
    public static string ExtractText(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
        string words = string.Join(" ", page.GetWords().Select(w => w.Text));
        return Collapse(words + " " + page.Text);
    }

    private static void CheckSizes(Manifest m, PdfDocument document, List<string> errors)
    {
        for (int n = 1; n <= document.NumberOfPages; n++)
        {
            Page page = document.GetPage(n);
            if (!Within(page.Width, m.PageWidthPt) || !Within(page.Height, m.PageHeightPt))
            {
                errors.Add($"Page {Int(n)} is {Pt(page.Width)} x {Pt(page.Height)} pt, expected {Pt(m.PageWidthPt)} x {Pt(m.PageHeightPt)}");
            }
        }
    }

    private static void CheckProbes(Manifest m, PdfDocument document, List<string> errors, List<string> warnings)
    {
        foreach ((string _, FixedPagesSpec spec) in ManifestChecks.FixedPageTypesOf(m))
        {
            CheckProbe(document, spec.PdfPages[0], spec.Probe, errors);
        }

        WeeklyPagesSpec weekly = m.PageTypes.Weekly;
        foreach (string name in weekly.Pages)
        {
            if (!weekly.Probes.TryGetValue(name, out string? probe))
            {
                warnings.Add($"No probe text for weekly page '{name}'; page not checked.");
                continue;
            }

            CheckProbe(document, weekly.PdfPageFor(1, name), probe, errors);
        }
    }

    private static void CheckProbe(PdfDocument document, int pageNumber, string probe, List<string> errors)
    {
        if (pageNumber < 1 || pageNumber > document.NumberOfPages)
        {
            // The page count error already covers a page that does not exist.
            return;
        }

        string text = ExtractText(document.GetPage(pageNumber));
        if (!ContainsProbe(text, probe))
        {
            errors.Add($"Page {Int(pageNumber)} does not contain '{probe}'");
        }
    }

    private static bool Within(double actual, double expected) => Math.Abs(actual - expected) <= SizeTolerancePt;

    private static string Collapse(string text) => WhitespaceRun().Replace(text, " ").Trim();

    private static string Strip(string text) => WhitespaceRun().Replace(text, "");

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Pt(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
