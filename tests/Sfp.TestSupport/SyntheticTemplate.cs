using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sfp.Core.Manifests;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Sfp.TestSupport;

/// <summary>
/// A 132-page look-alike of the SFP planner PDF built with PdfPig from the manifest's geometry: the page size and
/// background on every page, the probe word of each page type, the calendar grid and day boxes, the weeks-remaining
/// strip with its circle, the date slashes, the section labels and the time-block hour labels on daily pages, and the
/// day-name row on overview pages. It stands in for geometry and probes, not for looks: text is Times-Roman, and the
/// real PDF (SFP_PDF_PATH) is still needed for anything that depends on the printed art.
/// Built once per process into the temp folder; <see cref="Build()"/> returns the path.
/// </summary>
public static class SyntheticTemplate
{
    /// <summary>Bump when the drawing changes so cached copies from earlier runs are regenerated.</summary>
    public const string Version = "1";

    /// <summary>Section labels printed on daily pages that the manifest does not model (positions from the PRD's measured daily-page table).</summary>
    private static readonly (string Text, double X, double BaselineY)[] DailyLabels =
    [
        ("Daily Goals", 40.7, 77),
        ("Plan", 404.0, 77),
        ("Time Blocks", 935.3, 77),
    ];

    private static readonly string[] DayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static readonly Lazy<string> Cached = new(() => BuildToTemp(TestManifest.Shipped));

    /// <summary>Path of the synthetic PDF built from the shipped manifest, cached per process.</summary>
    public static string Build() => Cached.Value;

    /// <summary>Builds the PDF bytes for a manifest (any manifest, so tests can make variants).</summary>
    public static byte[] BuildBytes(Manifest manifest)
    {
        using PdfDocumentBuilder builder = new();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.TimesRoman);
        for (int pdfPage = 1; pdfPage <= manifest.PageCount; pdfPage++)
        {
            PdfPageBuilder page = builder.AddPage(manifest.PageWidthPt, manifest.PageHeightPt);
            Sheet sheet = new(page, font, manifest);
            DrawPage(sheet, manifest, pdfPage);
        }

        return builder.Build();
    }

    private static string BuildToTemp(Manifest manifest)
    {
        string manifestText = File.ReadAllText(TestPaths.ManifestPath);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + manifestText)))[..16].ToLowerInvariant();
        string dir = Path.Combine(Path.GetTempPath(), "sfp2onenote-tests");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "synthetic-template-" + hash + ".pdf");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return path;
        }

        byte[] bytes = BuildBytes(manifest);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, bytes);
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another test process won the race; its copy is identical.
            File.Delete(temp);
        }

        return path;
    }

    private static void DrawPage(Sheet sheet, Manifest m, int pdfPage)
    {
        sheet.Fill(new RectPt(0, 0, m.PageWidthPt, m.PageHeightPt), m.Colors.Background);
        PageTypesSpec types = m.PageTypes;
        HeadingSpec heading = m.Generated.Heading;

        if (types.Cover.PdfPages.Contains(pdfPage))
        {
            sheet.TextCentered(types.Cover.Probe, m.Slots.CoverLabel.X, m.PageHeightPt * 0.4, m.Slots.CoverLabel.SizePt);
            return;
        }

        if (types.Contents.PdfPages.Contains(pdfPage))
        {
            sheet.Text(types.Contents.Probe, heading.X, heading.BaselineY, heading.SizePt);
            return;
        }

        if (types.Annual.PdfPages.Contains(pdfPage))
        {
            sheet.Text(types.Annual.Probe, heading.X, heading.BaselineY, heading.SizePt);
            return;
        }

        if (types.Objective.PdfPages.Contains(pdfPage))
        {
            sheet.Text(types.Objective.Probe, heading.X, heading.BaselineY, heading.SizePt);
            return;
        }

        if (types.Calendar.PdfPages.Contains(pdfPage))
        {
            sheet.Text(types.Calendar.Probe, heading.X, heading.BaselineY, heading.SizePt);
            DrawCalendar(sheet, m);
            return;
        }

        WeeklyPagesSpec weekly = types.Weekly;
        int offset = pdfPage - weekly.FirstPdfPage;
        if (offset < 0 || offset >= weekly.Stride * weekly.Count)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "PDF page {0} belongs to no page type in the manifest.", pdfPage));
        }

        int run = (offset / weekly.Stride) + 1;
        string pageName = weekly.Pages[offset % weekly.Stride];
        string probe = weekly.Probes[pageName];
        sheet.Text(probe, heading.X, heading.BaselineY, heading.SizePt);
        DrawDateSlashes(sheet, m);

        int dayIndex = Array.IndexOf(DayNames, probe);
        if (dayIndex >= 0)
        {
            DrawDailyPage(sheet, m, run);
        }
        else if (pageName == weekly.Pages[0])
        {
            DrawOverviewDayNames(sheet, m);
        }
    }

    private static void DrawCalendar(Sheet sheet, Manifest m)
    {
        DayGridSlot grid = m.Slots.DayGrid;
        MonthRule? rule = m.Slots.MonthName.Rule;
        if (rule is not null)
        {
            sheet.Line(rule.X1, rule.Y, rule.X2, rule.Y, 0.5);
        }

        for (int c = 0; c < grid.ColumnX.Count; c++)
        {
            sheet.Text(DayNames[c], grid.ColumnX[c] + 2, grid.DayHeaderY, grid.SizePt);
        }

        double left = grid.ColumnX[0];
        double right = grid.ColumnX[^1] + grid.ColumnPitch;
        double top = grid.RowY[0];
        double bottom = grid.RowY[^1] + grid.RowPitch;
        foreach (double x in grid.ColumnX)
        {
            sheet.Line(x, top, x, bottom, 0.5);
        }

        sheet.Line(right, top, right, bottom, 0.5);
        foreach (double y in grid.RowY)
        {
            sheet.Line(left, y, right, y, 0.5);
        }

        sheet.Line(left, bottom, right, bottom, 0.5);

        for (int r = 0; r < grid.RowY.Count; r++)
        {
            for (int c = 0; c < grid.ColumnX.Count; c++)
            {
                sheet.Fill(grid.Box(r, c), m.Colors.DayBox);
            }
        }
    }

    private static void DrawDateSlashes(Sheet sheet, Manifest m)
    {
        foreach (LinePt slash in m.Slots.Date.Slashes)
        {
            sheet.Line(slash.X1, slash.Y1, slash.X2, slash.Y2, 1.0);
        }
    }

    private static void DrawOverviewDayNames(Sheet sheet, Manifest m)
    {
        OverviewDayHeaderAnchor header = m.Links.OverviewDayHeader;
        for (int i = 0; i < DayNames.Length; i++)
        {
            RectPt cell = header.Rect(i);
            sheet.TextCentered(DayNames[i], cell.CenterX, header.Y + header.SizePt, header.SizePt);
        }
    }

    private static void DrawDailyPage(Sheet sheet, Manifest m, int run)
    {
        WeeksRemainingSlot strip = m.Slots.WeeksRemaining;
        sheet.Text("Weeks Remaining In Quarter", strip.LabelRect.X, strip.BaselineY, strip.SizePt);

        int count = strip.NumberX.Count;
        for (int i = 0; i < count; i++)
        {
            string number = (count - i).ToString(CultureInfo.InvariantCulture);
            sheet.Text(number, strip.NumberX[i], strip.BaselineY, strip.SizePt);
        }

        // As printed: week k circles the number k (the numbers count down from the left).
        int circled = Math.Clamp(run, 1, count);
        int circledIndex = count - circled;
        string circledText = circled.ToString(CultureInfo.InvariantCulture);
        double centerX = strip.NumberX[circledIndex] + (sheet.TextWidth(circledText, strip.SizePt) / 2);
        sheet.Circle(centerX, strip.CircleCenterY, strip.CircleDiameterPt, m.Colors.Ink);
        sheet.Text(circledText, strip.NumberX[circledIndex], strip.BaselineY, strip.SizePt, "#FFFFFF");

        foreach ((string text, double x, double baselineY) in DailyLabels)
        {
            sheet.Text(text, x, baselineY, m.Slots.Date.SizePt);
        }

        TimeBlocksSlot blocks = m.Slots.TimeBlocks;
        for (int row = 0; row < blocks.PrintedRows; row++)
        {
            string label = (((row + 11) % 12) + 1).ToString(CultureInfo.InvariantCulture);
            double baseline = blocks.Top + (blocks.PrintedPitch * row) + blocks.PrintedLabelSizePt;
            sheet.TextRight(label, blocks.LabelRightX, baseline, blocks.PrintedLabelSizePt);
            double ruleY = blocks.Top + (blocks.PrintedPitch * (row + 1));
            sheet.Line(blocks.LabelRightX + 4, ruleY, blocks.ColumnRight, ruleY, blocks.RuleWidthPt);
        }
    }

    /// <summary>A page builder that takes top-left PDF points and the manifest's hex colors.</summary>
    private sealed class Sheet
    {
        private readonly PdfPageBuilder page;
        private readonly PdfDocumentBuilder.AddedFont font;
        private readonly double height;
        private readonly string ink;

        public Sheet(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, Manifest manifest)
        {
            this.page = page;
            this.font = font;
            height = manifest.PageHeightPt;
            ink = manifest.Colors.Ink;
        }

        public void Fill(RectPt rect, string hex)
        {
            (byte r, byte g, byte b) = Rgb(hex);
            page.SetStrokeColor(r, g, b);
            page.SetTextAndFillColor(r, g, b);
            page.DrawRectangle(new PdfPoint(rect.X, height - rect.Bottom), rect.W, rect.H, 0, fill: true);
        }

        public void Text(string text, double x, double baselineY, double sizePt, string? hex = null)
        {
            (byte r, byte g, byte b) = Rgb(hex ?? ink);
            page.SetTextAndFillColor(r, g, b);
            page.AddText(text, sizePt, new PdfPoint(x, height - baselineY), font);
        }

        public void TextCentered(string text, double centerX, double baselineY, double sizePt) =>
            Text(text, centerX - (TextWidth(text, sizePt) / 2), baselineY, sizePt);

        public void TextRight(string text, double rightX, double baselineY, double sizePt) =>
            Text(text, rightX - TextWidth(text, sizePt), baselineY, sizePt);

        public double TextWidth(string text, double sizePt)
        {
            IReadOnlyList<UglyToad.PdfPig.Content.Letter> letters = page.MeasureText(text, sizePt, new PdfPoint(0, 0), font);
            return letters.Count == 0 ? 0 : letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
        }

        public void Line(double x1, double y1, double x2, double y2, double widthPt)
        {
            (byte r, byte g, byte b) = Rgb(ink);
            page.SetStrokeColor(r, g, b);
            page.DrawLine(new PdfPoint(x1, height - y1), new PdfPoint(x2, height - y2), widthPt);
        }

        public void Circle(double cx, double cy, double diameterPt, string hex)
        {
            (byte r, byte g, byte b) = Rgb(hex);
            page.SetStrokeColor(r, g, b);
            page.SetTextAndFillColor(r, g, b);
            page.DrawCircle(new PdfPoint(cx, height - cy), diameterPt, 0, fill: true);
        }

        private static (byte R, byte G, byte B) Rgb(string hex)
        {
            if (hex.Length != 7 || hex[0] != '#')
            {
                throw new FormatException("Expected #RRGGBB, got '" + hex + "'.");
            }

            return (
                byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }
}
