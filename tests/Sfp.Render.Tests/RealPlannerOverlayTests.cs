using System.Globalization;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render.Pages;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>
/// Wave 3 review: the overlays checked on the real planner PDF (SFP_PDF_PATH, else docs/SFP No Dates.pdf), not only on
/// the synthetic template, which moves with the manifest. Fri Jan 1 is the PRD's acceptance page: week 1 of 14, so its
/// strip reads 14 down to 1 with the disc on 14. The erase checks of that page (the date slashes under the date link,
/// the strip, the time-block column) look at the printed extents (slots.date.slashPrinted, slots.weeksRemaining.printed,
/// slots.timeBlocks.printed: the ink bounds of the printed art, measured on the PDF), not at the erase rectangles, which
/// the renderer paints whole: inside each extent no pixel that is ink on the raw rasterization of the same PDF page may
/// be unchanged in the render outside the glyph, rule and disc boxes drawn back into it, so an erase rectangle shrunk by
/// a few points leaves ink these checks see. Each check also runs with the raw page in place of the render to show it
/// sees the printed art, and one test proves each extent bounds the art the PDF prints around it. Inside each erase
/// rectangle nothing may be inked but the overlays drawn back into it.
/// </summary>
[Collection(RenderCollection.Name)]
public sealed class RealPlannerOverlayTests
{
    /// <summary>Allowance around a drawn glyph or rule box for antialiasing and glyph overshoot, in points.</summary>
    private const double Margin = 0.8;

    /// <summary>How far antialiasing may carry printed ink past its measured extent on the raw page, in points.</summary>
    private const double PrintedSpread = 0.5;
    private const int Dpi = RenderFixture.Dpi;
    private static readonly PageKey FriJan1 = PageKey.Day(new DateOnly(2027, 1, 1));
    private readonly RenderFixture f;

    public RealPlannerOverlayTests(RenderFixture fixture)
    {
        f = fixture;
    }

    private Manifest M => f.Manifest;

    [SkippableFact]
    public void Fri_Jan_1_strip_shows_14_numbers_with_the_disc_on_14()
    {
        string pdf = SfpPdf.RequireOrSkip();
        using SKBitmap sheet = RenderFixture.Decode(RenderReal(pdf, FriJan1));
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        string[] numbers = DateSlots.StripNumbers(14);
        IReadOnlyList<double> x = DateSlots.StripPositions(strip, numbers, t => f.Fonts.TextWidth(t, strip.SizePt));

        // One cluster per number: the digits of a number are closer together than the air between two numbers. The band
        // spans the printed strip's measured extent, so printed ink an erase missed lands in it, and every box drawn.
        RectPt band = Union(StripBoxes(strip, count: 14, circledIndex: 0).Append(strip.Printed));
        IReadOnlyList<InkCluster> clusters = Pixels.InkClusters(sheet, band, Dpi, f.Background, mergeGapPt: 3.5);
        clusters.Should().HaveCount(14, "14 numbers count down from 14: " + Describe(clusters));

        InkCluster disc = clusters[0];
        disc.WidthPt.Should().BeApproximately(strip.CircleDiameterPt, 0.7, "the first cluster is the disc");
        clusters.Skip(1).Should().OnlyContain(c => c.WidthPt < strip.CircleDiameterPt - 2, "only one number is circled");
        disc.CenterPt.Should().BeInRange(x[0], x[0] + f.Fonts.TextWidth("14", strip.SizePt), "the disc sits on 14");
        Pixels.HasWhite(sheet, new RectPt(disc.LeftPt, strip.CircleCenterY - 3, disc.WidthPt, 6), Dpi, f.Background).Should().BeTrue("14 is drawn in white on the disc");
        for (int i = 1; i < 14; i++)
        {
            double w = f.Fonts.TextWidth(numbers[i], strip.SizePt);
            clusters[i].LeftPt.Should().BeGreaterThanOrEqualTo(x[i] - 0.6, $"{numbers[i]} starts inside its own box");
            clusters[i].RightPt.Should().BeLessThanOrEqualTo(x[i] + w + 0.6, $"{numbers[i]} ends inside its own box");
        }
    }

    [SkippableFact]
    public void Fri_Jan_1_with_links_off_reads_01_01_27_on_the_printed_slashes()
    {
        string pdf = SfpPdf.RequireOrSkip();
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(RenderReal(pdf, FriJan1, o => o with { Links = false }));
        DateSlot d = M.Slots.Date;
        double left = d.Fields[0].X - 22;
        double right = d.Fields[^1].X + 24;

        string text = RenderFixture.Read(sheet, new RectPt(left, d.BaselineY - 14, right - left, 20));
        RenderFixture.Digits(text).Should().Be("010127", "the PRD's overlay acceptance reads the first daily page's date; OCR read '" + text + "'");
    }

    [SkippableFact]
    public void The_printed_date_slashes_under_the_date_link_are_erased_completely()
    {
        string pdf = SfpPdf.RequireOrSkip();
        RenderedPage page = RenderReal(pdf, FriJan1);
        page.Anchors.Should().Contain(a => a.AnchorId == DateSlots.DailyDateAnchor, "the date of a daily page is a link");
        using SKBitmap sheet = RenderFixture.Decode(page);
        using SKBitmap raw = Raw(pdf, FriJan1);
        DateSlot d = M.Slots.Date;

        Pixels.SurvivingPrint(raw, raw, d.SlashPrinted, Array.Empty<RectPt>(), Dpi, f.Background).Count.Should().BePositive("the planner prints the date slashes there");
        (int count, IReadOnlyList<(double XPt, double YPt)> samples) = Pixels.SurvivingPrint(raw, sheet, d.SlashPrinted, Array.Empty<RectPt>(), Dpi, f.Background);
        count.Should().Be(0, "no printed slash survives anywhere in slots.date.slashPrinted: " + Format(samples));
        (int stray, IReadOnlyList<(double XPt, double YPt)> strays) = Pixels.StrayInk(sheet, d.SlashErase, Array.Empty<RectPt>(), Dpi, f.Background);
        stray.Should().Be(0, "nothing is drawn under the date link: " + Format(strays));
    }

    [SkippableFact]
    public void Inside_the_strip_erase_only_the_redrawn_numbers_and_the_disc_remain()
    {
        string pdf = SfpPdf.RequireOrSkip();
        using SKBitmap sheet = RenderFixture.Decode(RenderReal(pdf, FriJan1));
        using SKBitmap raw = Raw(pdf, FriJan1);
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        IReadOnlyList<RectPt> drawn = StripBoxes(strip, count: 14, circledIndex: 0);

        Pixels.SurvivingPrint(raw, raw, strip.Printed, drawn, Dpi, f.Background).Count.Should().BePositive("the 13 printed numbers and their disc sit elsewhere, so an unerased strip would show");
        (int count, IReadOnlyList<(double XPt, double YPt)> samples) = Pixels.SurvivingPrint(raw, sheet, strip.Printed, drawn, Dpi, f.Background);
        count.Should().Be(0, "no printed number or disc survives anywhere in slots.weeksRemaining.printed: " + Format(samples));
        (int stray, IReadOnlyList<(double XPt, double YPt)> strays) = Pixels.StrayInk(sheet, strip.Erase, drawn, Dpi, f.Background);
        stray.Should().Be(0, "inside the erase only the redrawn numbers and the disc are inked: " + Format(strays));
    }

    [SkippableFact]
    public void Inside_the_time_block_erase_only_the_rebuilt_rules_and_labels_remain()
    {
        string pdf = SfpPdf.RequireOrSkip();
        using SKBitmap sheet = RenderFixture.Decode(RenderReal(pdf, FriJan1));
        using SKBitmap raw = Raw(pdf, FriJan1);
        TimeBlocksSlot t = M.Slots.TimeBlocks;
        TimeBlockWindow window = f.Q1Options.TimeBlocks;
        int rows = window.RowCount;
        double pitch = t.PitchFor(rows);
        double cap = CapHeight(t.LabelSizePt);
        List<RectPt> drawn = new();
        for (int row = 0; row <= rows; row++)
        {
            double y = t.Top + (row * pitch);
            drawn.Add(new RectPt(t.ColumnX - Margin, y - (t.RuleWidthPt / 2) - Margin, t.ColumnRight - t.ColumnX + (2 * Margin), t.RuleWidthPt + (2 * Margin)));
            string label = TimeBlockColumn.HourLabel(window.StartHour + row);
            double w = f.Fonts.TextWidth(label, t.LabelSizePt);
            drawn.Add(GlyphBox(t.LabelRightX - w, y + (cap / 2), w, cap));
        }

        Pixels.SurvivingPrint(raw, raw, t.Printed, drawn, Dpi, f.Background).Count.Should().BePositive("the planner's 24 printed rows and 7 pt labels sit elsewhere");
        (int count, IReadOnlyList<(double XPt, double YPt)> samples) = Pixels.SurvivingPrint(raw, sheet, t.Printed, drawn, Dpi, f.Background);
        count.Should().Be(0, "no printed rule or label survives anywhere in slots.timeBlocks.printed: " + Format(samples));
        (int stray, IReadOnlyList<(double XPt, double YPt)> strays) = Pixels.StrayInk(sheet, t.Erase, drawn, Dpi, f.Background);
        stray.Should().Be(0, "only the rebuilt rules and labels are inside the erased column: " + Format(strays));
    }

    [SkippableFact]
    public void Each_printed_extent_bounds_the_art_the_planner_prints_there()
    {
        string pdf = SfpPdf.RequireOrSkip();
        WeeklyPagesSpec weekly = M.PageTypes.Weekly;

        // The printed disc sits on 1 in the first week run and on 13 in the last, so these two Fridays reach both ends
        // of the strip; the slashes and the time-block column are the same on every daily page.
        using SKBitmap first = f.Rasterizer.Render(pdf, weekly.PdfPageFor(1, "fri"), Dpi);
        using SKBitmap last = f.Rasterizer.Render(pdf, weekly.PdfPageFor(weekly.Count, "fri"), Dpi);
        (string Key, RectPt Printed)[] extents =
        {
            ("slots.date.slashPrinted", M.Slots.Date.SlashPrinted),
            ("slots.weeksRemaining.printed", M.Slots.WeeksRemaining.Printed),
            ("slots.timeBlocks.printed", M.Slots.TimeBlocks.Printed),
        };

        foreach ((string key, RectPt printed) in extents)
        {
            // Nothing else is printed within 2 pt of these three, so all ink found there belongs to the art the extent bounds.
            RectPt around = new(printed.X - 2, printed.Y - 2, printed.W + 4, printed.H + 4);
            RectPt[] found = new[] { first, last }.Select(page => Pixels.InkBounds(page, around, Dpi, f.Background)).OfType<RectPt>().ToArray();
            found.Should().NotBeEmpty($"the planner prints the art of {key} there");
            RectPt ink = Union(found);
            string seen = $"{key} is {printed}, the printed ink {ink}";

            ink.X.Should().BeGreaterThanOrEqualTo(printed.X - PrintedSpread, "no printed ink lies left of " + seen);
            ink.Y.Should().BeGreaterThanOrEqualTo(printed.Y - PrintedSpread, "no printed ink lies above " + seen);
            ink.Right.Should().BeLessThanOrEqualTo(printed.Right + PrintedSpread, "no printed ink lies right of " + seen);
            ink.Bottom.Should().BeLessThanOrEqualTo(printed.Bottom + PrintedSpread, "no printed ink lies below " + seen);
            ink.X.Should().BeLessThanOrEqualTo(printed.X + 1, "the extent is the art's own bounds, not a loose box: " + seen);
            ink.Y.Should().BeLessThanOrEqualTo(printed.Y + 1, "the extent is the art's own bounds, not a loose box: " + seen);
            ink.Right.Should().BeGreaterThanOrEqualTo(printed.Right - 1, "the extent is the art's own bounds, not a loose box: " + seen);
            ink.Bottom.Should().BeGreaterThanOrEqualTo(printed.Bottom - 1, "the extent is the art's own bounds, not a loose box: " + seen);
        }
    }

    /// <summary>Boxes of the strip's redrawn numbers and of the disc around the circled one, grown by <see cref="Margin"/>.</summary>
    private IReadOnlyList<RectPt> StripBoxes(WeeksRemainingSlot strip, int count, int circledIndex)
    {
        string[] numbers = DateSlots.StripNumbers(count);
        IReadOnlyList<double> x = DateSlots.StripPositions(strip, numbers, t => f.Fonts.TextWidth(t, strip.SizePt));
        double cap = CapHeight(strip.SizePt);
        List<RectPt> boxes = numbers.Select((t, i) => GlyphBox(x[i], strip.BaselineY, f.Fonts.TextWidth(t, strip.SizePt), cap)).ToList();

        // The disc is centered on its number; a point either way covers centering on the advance or on the ink.
        double r = strip.CircleDiameterPt / 2;
        double cx = x[circledIndex] + (f.Fonts.TextWidth(numbers[circledIndex], strip.SizePt) / 2);
        boxes.Add(new RectPt(cx - r - 1 - Margin, strip.CircleCenterY - r - Margin, (2 * r) + 2 + (2 * Margin), (2 * r) + (2 * Margin)));
        return boxes;
    }

    /// <summary>The smallest rectangle holding all the given ones.</summary>
    private static RectPt Union(IEnumerable<RectPt> rects)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (RectPt r in rects)
        {
            left = Math.Min(left, r.X);
            top = Math.Min(top, r.Y);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }

        return new RectPt(left, top, right - left, bottom - top);
    }

    /// <summary>A glyph run's box from its left edge and baseline (digits stand on the baseline and reach the cap height), grown by <see cref="Margin"/>.</summary>
    private static RectPt GlyphBox(double left, double baseline, double width, double cap) =>
        new(left - Margin, baseline - cap - Margin, width + (2 * Margin), cap + (2 * Margin));

    private static string Describe(IEnumerable<InkCluster> clusters) =>
        string.Join(" ", clusters.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.LeftPt:0.0}-{c.RightPt:0.0}")));

    private static string Format(IEnumerable<(double XPt, double YPt)> samples) =>
        string.Join(", ", samples.Select(p => string.Create(CultureInfo.InvariantCulture, $"({p.XPt:0.0}, {p.YPt:0.0})")));

    private double CapHeight(double sizePt)
    {
        using SKFont font = f.Fonts.Font(sizePt);
        return font.Metrics.CapHeight;
    }

    private RenderedPage RenderReal(string pdf, PageKey key, Func<GenerateOptions, GenerateOptions>? adjust = null)
    {
        GenerateOptions options = adjust is null ? f.Q1Options : adjust(f.Q1Options);
        NotebookPlan plan = adjust is null ? f.Q1Plan : f.Plan(options);
        return f.Renderer.Render(plan.Page(key), plan, new RenderOptions(pdf, Dpi, options));
    }

    private SKBitmap Raw(string pdf, PageKey key) => f.Rasterizer.Render(pdf, f.Q1Plan.Page(key).PdfPage!.Value, Dpi);
}
