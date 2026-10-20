using System.Globalization;
using Sfp.Core.Manifests;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Writer;

namespace Sfp.Core.Tests.Manifests;

/// <summary>A page size in points.</summary>
public sealed record PtSize(double W, double H);

/// <summary>A text run for a fixture page, in points with a top-left origin (the manifest convention).</summary>
public sealed record FixtureText(string Text, double X, double BaselineY, double SizePt);

/// <summary>A stroked line for a fixture page, in points with a top-left origin.</summary>
public sealed record FixtureLine(double X1, double Y1, double X2, double Y2, double WidthPt);

/// <summary>Ways to make a template-like fixture deliberately wrong.</summary>
public sealed record PdfFixtureOptions
{
    /// <summary>Total pages; null means the manifest's pageCount.</summary>
    public int? PageCount { get; init; }

    /// <summary>Pages drawn at another size than the manifest's, by 1-based page number.</summary>
    public IReadOnlyDictionary<int, PtSize> PageSizeOverrides { get; init; } = new Dictionary<int, PtSize>();

    /// <summary>Pages whose probe text is replaced by a placeholder, so the probe is absent.</summary>
    public IReadOnlySet<int> OmitProbeOn { get; init; } = new HashSet<int>();

    /// <summary>Write every probe in upper case (the validator must not care).</summary>
    public bool UpperCaseProbes { get; init; }
}

/// <summary>
/// Builds PDFs with PdfPig's writer that stand in for the planner: the right page count and size, each page type's
/// probe text as its heading, and the geometry the renderer erases or measures (date slashes, weeks-remaining numbers,
/// the month rule, the day grid), all placed from the manifest. No test-framework dependency, so the render tests can
/// reuse it (the integrator moves it to Sfp.TestSupport).
/// </summary>
public static class PdfFixtures
{
    /// <summary>Text a page gets instead of its probe when the probe is omitted.</summary>
    public const string PlaceholderHeading = "Lorem ipsum";

    /// <summary>Writes a template-like PDF to <paramref name="path"/> and returns the path.</summary>
    public static string WriteTemplateLike(string path, Manifest manifest, PdfFixtureOptions? options = null)
    {
        File.WriteAllBytes(path, BuildTemplateLike(manifest, options));
        return path;
    }

    /// <summary>Builds a template-like PDF in memory.</summary>
    public static byte[] BuildTemplateLike(Manifest manifest, PdfFixtureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        options ??= new PdfFixtureOptions();
        IReadOnlyDictionary<int, string> pageNames = PageNameByPage(manifest);
        int pageCount = options.PageCount ?? manifest.PageCount;

        using PdfDocumentBuilder builder = new();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int number = 1; number <= pageCount; number++)
        {
            PtSize size = options.PageSizeOverrides.TryGetValue(number, out PtSize? o) ? o : new PtSize(manifest.PageWidthPt, manifest.PageHeightPt);
            PdfPageBuilder page = builder.AddPage(size.W, size.H);
            PaintBackground(page, manifest, size);

            string? pageName = pageNames.TryGetValue(number, out string? n) ? n : null;
            string? probe = pageName is null ? null : ProbeFor(manifest, pageName);
            string heading = probe is null || options.OmitProbeOn.Contains(number)
                ? PlaceholderHeading
                : options.UpperCaseProbes ? probe.ToUpperInvariant() : probe;

            if (pageName == PageNames.Cover)
            {
                CoverLabelSlot cover = manifest.Slots.CoverLabel;
                AddCenteredText(page, font, heading, cover.X, cover.Baselines[0] - (3 * cover.SizePt), 2 * cover.SizePt, size.H);
            }
            else
            {
                HeadingSpec h = manifest.Generated.Heading;
                AddText(page, font, heading, h.X, h.BaselineY, h.SizePt, size.H);
            }

            if (pageName is not null)
            {
                AddPageArt(page, font, manifest, pageName, size.H);
            }
        }

        return builder.Build();
    }

    /// <summary>Builds a one-page PDF with the given text runs and lines, for geometry tests.</summary>
    public static byte[] BuildPage(PtSize size, IEnumerable<FixtureText> texts, IEnumerable<FixtureLine> lines)
    {
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(lines);
        using PdfDocumentBuilder builder = new();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(size.W, size.H);
        foreach (FixtureText t in texts)
        {
            AddText(page, font, t.Text, t.X, t.BaselineY, t.SizePt, size.H);
        }

        foreach (FixtureLine l in lines)
        {
            AddLine(page, l.X1, l.Y1, l.X2, l.Y2, l.WidthPt, size.H);
        }

        return builder.Build();
    }

    /// <summary>Builds a one-page PDF holding one thin filled rectangle (a rule drawn as a fill), top-left coordinates.</summary>
    public static byte[] BuildPageWithFilledRectangle(PtSize size, double x, double y, double w, double h)
    {
        ArgumentNullException.ThrowIfNull(size);
        using PdfDocumentBuilder builder = new();
        PdfPageBuilder page = builder.AddPage(size.W, size.H);
        page.SetTextAndFillColor(0, 0, 0);
        FillRectangle(page, x, size.H - y - h, w, h);
        return builder.Build();
    }

    /// <summary>A fill-only rectangle in PdfPig coordinates (the builder's DrawRectangle with fill also strokes a hairline).</summary>
    private static void FillRectangle(PdfPageBuilder page, double lowerLeftX, double lowerLeftY, double w, double h)
    {
        page.CurrentStream.Operations.Add(new AppendRectangle(lowerLeftX, lowerLeftY, w, h));
        page.CurrentStream.Operations.Add(FillPathNonZeroWinding.Value);
    }

    /// <summary>The manifest page name of every PDF page the manifest describes (cover, contents, annual, objective, calendar, overview, mon ... reflection).</summary>
    public static IReadOnlyDictionary<int, string> PageNameByPage(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Dictionary<int, string> result = new();
        foreach ((string name, FixedPagesSpec spec) in ManifestChecks.FixedPageTypesOf(manifest))
        {
            foreach (int page in spec.PdfPages)
            {
                result[page] = name;
            }
        }

        WeeklyPagesSpec weekly = manifest.PageTypes.Weekly;
        for (int run = 1; run <= weekly.Count; run++)
        {
            foreach (string pageName in weekly.Pages)
            {
                result[weekly.PdfPageFor(run, pageName)] = pageName;
            }
        }

        return result;
    }

    /// <summary>The probe text of a page name, or null when the manifest has none.</summary>
    public static string? ProbeFor(Manifest manifest, string pageName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        foreach ((string name, FixedPagesSpec spec) in ManifestChecks.FixedPageTypesOf(manifest))
        {
            if (name == pageName)
            {
                return spec.Probe;
            }
        }

        return manifest.PageTypes.Weekly.Probes.TryGetValue(pageName, out string? probe) ? probe : null;
    }

    private static void PaintBackground(PdfPageBuilder page, Manifest manifest, PtSize size)
    {
        (byte r, byte g, byte b) = ParseHex(manifest.Colors.Background);
        page.SetTextAndFillColor(r, g, b);
        FillRectangle(page, 0, 0, size.W, size.H);
        (byte ir, byte ig, byte ib) = ParseHex(manifest.Colors.Ink);
        page.SetTextAndFillColor(ir, ig, ib);
        page.SetStrokeColor(ir, ig, ib);
    }

    private static void AddPageArt(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, Manifest m, string pageName, double pageHeight)
    {
        DateSlot date = m.Slots.Date;
        if (date.AppliesTo.Contains(pageName, StringComparer.Ordinal))
        {
            foreach (LinePt slash in date.Slashes)
            {
                AddLine(page, slash.X1, slash.Y1, slash.X2, slash.Y2, 1, pageHeight);
            }
        }

        WeeksRemainingSlot strip = m.Slots.WeeksRemaining;
        if (strip.AppliesTo.Contains(pageName, StringComparer.Ordinal))
        {
            AddText(page, font, "Weeks Remaining In Quarter", strip.LabelRect.X, strip.BaselineY, strip.SizePt, pageHeight);
            for (int i = 0; i < strip.NumberX.Count; i++)
            {
                string number = (strip.NumberX.Count - i).ToString(CultureInfo.InvariantCulture);
                AddText(page, font, number, strip.NumberX[i], strip.BaselineY, strip.SizePt, pageHeight);
            }
        }

        if (pageName == PageNames.Calendar)
        {
            MonthRule? rule = m.Slots.MonthName.Rule;
            if (rule is not null)
            {
                AddLine(page, rule.X1, rule.Y, rule.X2, rule.Y, 0.5, pageHeight);
            }

            DayGridSlot grid = m.Slots.DayGrid;
            for (int row = 0; row < grid.RowY.Count; row++)
            {
                for (int column = 0; column < grid.ColumnX.Count; column++)
                {
                    RectPt box = grid.Box(row, column);
                    page.DrawRectangle(new PdfPoint(box.X, pageHeight - box.Bottom), box.W, box.H, 0.25, fill: false);
                }
            }
        }
    }

    private static void AddText(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text, double x, double baselineY, double sizePt, double pageHeight) =>
        page.AddText(text, sizePt, new PdfPoint(x, pageHeight - baselineY), font);

    private static void AddCenteredText(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text, double centerX, double baselineY, double sizePt, double pageHeight)
    {
        var letters = page.MeasureText(text, sizePt, new PdfPoint(0, 0), font);
        double width = letters.Count == 0 ? 0 : letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
        AddText(page, font, text, centerX - (width / 2), baselineY, sizePt, pageHeight);
    }

    private static void AddLine(PdfPageBuilder page, double x1, double y1, double x2, double y2, double widthPt, double pageHeight) =>
        page.DrawLine(new PdfPoint(x1, pageHeight - y1), new PdfPoint(x2, pageHeight - y2), widthPt);

    private static (byte R, byte G, byte B) ParseHex(string hex)
    {
        string digits = hex.TrimStart('#');
        return (
            byte.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
