using Sfp.Core;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render.Colors;
using Sfp.Render.Pages;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.4: the date slot (baked and link modes), the weeks-remaining strip and the day counter on the synthetic template.</summary>
[Collection(RenderCollection.Name)]
public sealed class DateAndStripTests
{
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
    private readonly RenderFixture f;

    public DateAndStripTests(RenderFixture fixture)
    {
        f = fixture;
    }

    private Manifest M => f.Manifest;

    /// <summary>The region holding the three date fields and the slashes.</summary>
    private RectPt DateRegion
    {
        get
        {
            DateSlot d = M.Slots.Date;
            double left = d.Fields[0].X - 22;
            double right = d.Fields[^1].X + 24;
            return new RectPt(left, d.BaselineY - 14, right - left, 20);
        }
    }

    [SkippableFact]
    public void Week_2_overview_bakes_the_Monday_date_01_04_27()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(PageKey.WeekOverview(2)));

        string text = RenderFixture.Read(sheet, DateRegion);
        RenderFixture.Digits(text).Should().Be("010427", "the overview is dated with the week's Monday; OCR read '" + text + "'");
        text.Count(ch => ch == '/').Should().Be(2, "the printed slashes stay");
    }

    [SkippableFact]
    public void Daily_page_with_links_off_bakes_the_date_01_04_27()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        RenderedPage page = f.Render(MonJan4, o => o with { Links = false });
        using SKBitmap sheet = RenderFixture.Decode(page);

        string text = RenderFixture.Read(sheet, DateRegion);
        RenderFixture.Digits(text).Should().Be("010427", "OCR read '" + text + "'");
        page.Anchors.Should().NotContain(a => a.Kind == AnchorKind.Link, "links are off");
    }

    [Fact]
    public void Daily_page_with_links_on_erases_the_slashes_and_places_the_date_link()
    {
        RenderedPage page = f.Render(MonJan4);
        using SKBitmap sheet = RenderFixture.Decode(page);

        RectPt erase = M.Slots.Date.SlashErase;
        Pixels.Mean(sheet, new RectPt(erase.X + 1, erase.Y + 1, erase.W - 2, erase.H - 2), RenderFixture.Dpi).Should().Be(f.Background, "the slashes and any baked date are gone");
        AnchorPlacement date = page.Anchors.Should().ContainSingle(a => a.AnchorId == "dailyDate").Subject;
        date.Kind.Should().Be(AnchorKind.Link);
        date.Text.Should().Be("01 / 04 / 27");
        date.Align.Should().Be(TextAlign.Right);
        date.X.Should().BeApproximately((float)M.Links.DailyDate.Rect.X, 0.01f);
        date.Y.Should().BeApproximately((float)M.Links.DailyDate.Y, 0.01f);
        date.SizePt.Should().Be((float)M.Links.DailyDate.SizePt);
    }

    [Fact]
    public void Week_2_Monday_strip_shows_14_numbers_with_the_circle_on_13()
    {
        using SKBitmap sheet = RenderFixture.Decode(f.Render(MonJan4));
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        string[] numbers = DateSlots.StripNumbers(14);
        IReadOnlyList<double> x = DateSlots.StripPositions(strip, numbers, t => f.Fonts.TextWidth(t, strip.SizePt));
        double cap = f.Fonts.TextWidth("0", strip.SizePt) * 1.4;

        // Every number has ink inside its own box and the gap before the next one is clear.
        for (int i = 0; i < 14; i++)
        {
            double w = f.Fonts.TextWidth(numbers[i], strip.SizePt);
            RectPt box = new(x[i], strip.BaselineY - cap, w, cap);
            f.HasInk(sheet, box).Should().BeTrue($"number {numbers[i]} is drawn at x {x[i]:0.0}");
            if (i < 13)
            {
                double gapLeft = x[i] + w + 0.6;
                double gapRight = x[i + 1] - 0.6;
                f.HasInk(sheet, new RectPt(gapLeft, strip.BaselineY - cap, gapRight - gapLeft, cap)).Should().BeFalse($"the gap between {numbers[i]} and {numbers[i + 1]} is empty");
            }
        }

        // Counting dark clusters along the baseline band, one per digit glyph (the circled number is one filled disc): 17 digits plus the disc.
        RectPt band = new(strip.NumberSpan.From - 4, strip.BaselineY - cap, strip.NumberSpan.To - strip.NumberSpan.From + 10, cap + 1);
        IReadOnlyList<InkCluster> clusters = Pixels.InkClusters(sheet, band, RenderFixture.Dpi, f.Background, mergeGapPt: 0.3);
        int expectedClusters = numbers.Select((n, i) => i == 1 ? 1 : n.Length).Sum();
        expectedClusters.Should().Be(18);
        clusters.Should().HaveCount(expectedClusters, "14 numbers counting down from 14 with 13 circled: " + string.Join(" ", clusters.Select(c => $"{c.LeftPt:0.0}-{c.RightPt:0.0}")));
        clusters.Max(c => c.WidthPt).Should().BeApproximately(strip.CircleDiameterPt, 1.0, "the widest cluster is the disc");

        // The circle sits on 13 (index 1): a filled disc with the digit in white inside it.
        double cx = x[1] + (f.Fonts.TextWidth("13", strip.SizePt) / 2);
        double cy = strip.CircleCenterY;
        double r = strip.CircleDiameterPt / 2;
        sheet.GetPixel(Px(cx - r + 1.5), Px(cy)).Should().Be(f.Ink, "inside the rim of the disc is ink");
        sheet.GetPixel(Px(cx), Px(cy - r + 1.5)).Should().Be(f.Ink);
        Pixels.HasWhite(sheet, new RectPt(cx - r, cy - r, 2 * r, 2 * r), RenderFixture.Dpi, f.Background).Should().BeTrue("the circled number is drawn in white");
        sheet.GetPixel(Px(cx), Px(cy + r + 2.5)).Should().Be(f.Background, "outside the disc is bare sheet");
        sheet.GetPixel(Px(x[0] + 2), Px(cy)).Should().NotBe(f.Ink, "14 is not circled");
        ColorParser.MaxChannelDistance(Pixels.Darkest(sheet, new RectPt(x[0], strip.BaselineY - cap, f.Fonts.TextWidth("14", strip.SizePt), cap), RenderFixture.Dpi), f.Ink).Should().BeLessThanOrEqualTo(8, "14 is drawn in solid ink");
    }

    [Fact]
    public void As_printed_mode_circles_the_week_number()
    {
        using SKBitmap sheet = RenderFixture.Decode(f.Render(MonJan4, o => o with { WeeksRemaining = WeeksRemainingMode.AsPrinted }));
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        string[] numbers = DateSlots.StripNumbers(14);
        IReadOnlyList<double> x = DateSlots.StripPositions(strip, numbers, t => f.Fonts.TextWidth(t, strip.SizePt));
        double r = strip.CircleDiameterPt / 2;

        double cx2 = x[12] + (f.Fonts.TextWidth("2", strip.SizePt) / 2);
        sheet.GetPixel(Px(cx2 - r + 1.5), Px(strip.CircleCenterY)).Should().Be(f.Ink, "week 2 circles 2 as printed");
        double cx13 = x[1] + (f.Fonts.TextWidth("13", strip.SizePt) / 2);
        sheet.GetPixel(Px(cx13 - r + 1.5), Px(strip.CircleCenterY)).Should().NotBe(f.Ink, "13 is not circled");
    }

    [Fact]
    public void Thirteen_numbers_use_the_printed_positions_and_fourteen_tighten_the_pitch()
    {
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        Func<string, double> width = t => f.Fonts.TextWidth(t, strip.SizePt);

        DateSlots.StripPositions(strip, DateSlots.StripNumbers(13), width).Should().Equal(strip.NumberX);

        IReadOnlyList<double> fourteen = DateSlots.StripPositions(strip, DateSlots.StripNumbers(14), width);
        fourteen.Should().HaveCount(14);
        fourteen[0].Should().Be(strip.NumberSpan.From);
        (fourteen[^1] + width("1")).Should().BeApproximately(strip.NumberSpan.To, 0.01, "the last number ends at the span end");
        (fourteen[1] - fourteen[0]).Should().BeInRange(13.5, 14.2, "the PRD expects a pitch of about 13.8 pt");
        DateSlots.StripNumbers(14).Should().StartWith(new[] { "14", "13" }).And.EndWith("1");
    }

    [SkippableFact]
    public void Day_counter_reads_Day_4_of_90()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(MonJan4));
        DayCounterSlot counter = M.Slots.DayCounter;

        string text = RenderFixture.Read(sheet, new RectPt(counter.RightX - 150, counter.BaselineY - 9, 152, 12), paddingPt: 4);
        text.Should().Contain("Day 4 of 90", "OCR read '" + text + "'");
        text.Should().Contain("Week 2 of 14");
    }

    [Fact]
    public void Day_counter_is_drawn_at_the_counter_opacity_right_aligned()
    {
        using SKBitmap sheet = RenderFixture.Decode(f.Render(MonJan4));
        DayCounterSlot counter = M.Slots.DayCounter;
        RectPt region = new(counter.RightX - 150, counter.BaselineY - 8, 150, 10);

        SKColor darkest = Pixels.Darkest(sheet, region, RenderFixture.Dpi);
        ((int)darkest.Red).Should().BeGreaterThan(f.Ink.Red + 40, "60% ink is lighter than full ink");
        ((int)darkest.Red).Should().BeLessThan(f.Background.Red - 60, "but clearly visible");
        f.HasInk(sheet, new RectPt(counter.RightX - 6, counter.BaselineY - 8, 6, 10)).Should().BeTrue("the text ends at the right edge");
        f.HasInk(sheet, new RectPt(counter.RightX + 1, counter.BaselineY - 8, 6, 10)).Should().BeFalse("nothing right of the edge");
    }

    [Fact]
    public void Out_of_quarter_day_is_dated_in_greyed_ink_without_a_counter_or_a_date_link()
    {
        PageKey dec28 = PageKey.Day(new DateOnly(2026, 12, 28));
        RenderedPage page = f.Render(dec28, o => o with { IncludeOutOfQuarterDays = true });
        using SKBitmap sheet = RenderFixture.Decode(page);
        DateSlot d = M.Slots.Date;

        page.Anchors.Should().NotContain(a => a.AnchorId == "dailyDate");
        RectPt year = new(d.Fields[^1].X, d.BaselineY - 11, 20, 12);
        SKColor darkest = Pixels.Darkest(sheet, year, RenderFixture.Dpi);
        ((int)darkest.Red).Should().BeGreaterThan(f.Ink.Red + 60, "40% ink");
        ((int)darkest.Red).Should().BeLessThan(f.Background.Red - 40, "but drawn");
        DayCounterSlot counter = M.Slots.DayCounter;
        f.HasInk(sheet, new RectPt(counter.RightX - 150, counter.BaselineY - 8, 150, 10)).Should().BeFalse("no day counter outside the quarter");
    }

    [Fact]
    public void Reflection_of_week_14_bakes_the_Sunday_after_the_quarter()
    {
        RenderedPage page = f.Render(PageKey.WeekReflection(14));
        using SKBitmap sheet = RenderFixture.Decode(page);
        DateSlot d = M.Slots.Date;

        f.HasInk(sheet, new RectPt(d.Fields[^1].X, d.BaselineY - 11, 20, 12)).Should().BeTrue("04 / 04 / 27 is baked");
        page.Anchors.Should().NotContain(a => a.AnchorId == "dailyDate");
        ColorParser.MaxChannelDistance(Pixels.Darkest(sheet, new RectPt(d.Fields[^1].X, d.BaselineY - 11, 20, 12), RenderFixture.Dpi), f.Ink).Should().BeLessThanOrEqualTo(8, "a reflection date is full ink even after the quarter end");
    }

    [Fact]
    public void Day_month_year_order_puts_the_day_first()
    {
        RenderedPage page = f.Render(MonJan4, o => o with { DateOrder = DateFieldOrder.DayMonthYear, PadDateFields = false });
        page.Anchors.Single(a => a.AnchorId == "dailyDate").Text.Should().Be("4 / 1 / 27");
    }

    [Fact]
    public void Two_renders_of_the_same_page_are_byte_identical()
    {
        RenderedPage a = f.Render(MonJan4);
        RenderedPage b = f.Render(MonJan4);
        a.Png.Should().Equal(b.Png);
        a.Anchors.Should().Equal(b.Anchors);
        a.WidthPx.Should().Be(3398);
        a.HeightPx.Should().Be(2353);
    }

    private static int Px(double pt) => (int)Math.Floor(pt * RenderFixture.Dpi / 72.0);
}
