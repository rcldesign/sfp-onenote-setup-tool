using System.Globalization;
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

        // The disc on 13 (index 1) is centered on the number's ink and is wider than it, so 13 reaches the disc's rim.
        double r = strip.CircleDiameterPt / 2;
        double cx = x[1] + InkCenter("13", strip.SizePt);
        double Left(int i) => i == 1 ? cx - r - 0.3 : x[i] - 0.6;
        double Right(int i) => i == 1 ? cx + r + 0.3 : x[i] + f.Fonts.TextWidth(numbers[i], strip.SizePt) + 0.6;

        // Every number has ink inside its own box and the gap before the next one is clear.
        for (int i = 0; i < 14; i++)
        {
            double w = f.Fonts.TextWidth(numbers[i], strip.SizePt);
            RectPt box = new(x[i], strip.BaselineY - cap, w, cap);
            f.HasInk(sheet, box).Should().BeTrue($"number {numbers[i]} is drawn at x {x[i]:0.0}");
            if (i < 13)
            {
                double gapLeft = Right(i);
                double gapRight = Left(i + 1);
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
        double cy = strip.CircleCenterY;
        sheet.GetPixel(Px(cx - r + 1.5), Px(cy)).Should().Be(f.Ink, "inside the rim of the disc is ink");
        sheet.GetPixel(Px(cx), Px(cy - r + 1.5)).Should().Be(f.Ink);
        Pixels.HasWhite(sheet, new RectPt(cx - r, cy - r, 2 * r, 2 * r), RenderFixture.Dpi, f.Background).Should().BeTrue("the circled number is drawn in white");
        sheet.GetPixel(Px(cx), Px(cy + r + 2.5)).Should().Be(f.Background, "outside the disc is bare sheet");
        sheet.GetPixel(Px(x[0] + 2), Px(cy)).Should().NotBe(f.Ink, "14 is not circled");
        ColorParser.MaxChannelDistance(Pixels.Darkest(sheet, new RectPt(x[0], strip.BaselineY - cap, f.Fonts.TextWidth("14", strip.SizePt), cap), RenderFixture.Dpi), f.Ink).Should().BeLessThanOrEqualTo(8, "14 is drawn in solid ink");
    }

    /// <summary>
    /// Wave 3 review: with the 13.4 pt disc the white number must sit in the middle of it and clear of the rim. The
    /// disc used to be centered on the number's advance box, and the side bearings of "1" put the white "14" 0.65 pt
    /// right of center with 0.39 pt left to the rim at 220 DPI; it is now centered on the number's ink.
    /// </summary>
    [Theory]
    [InlineData(1, "14")]
    [InlineData(2, "13")]
    [InlineData(3, "12")]
    [InlineData(4, "11")]
    [InlineData(5, "10")]
    [InlineData(13, "2")]
    public void The_circled_number_sits_in_the_middle_of_the_disc_clear_of_its_rim(int week, string circled)
    {
        PageKey key = PageKey.Day(week == 1 ? new DateOnly(2027, 1, 1) : new DateOnly(2027, 1, 4).AddDays(7 * (week - 2)));
        using SKBitmap sheet = RenderFixture.Decode(f.Render(key));
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        string[] numbers = DateSlots.StripNumbers(14);
        IReadOnlyList<double> x = DateSlots.StripPositions(strip, numbers, t => f.Fonts.TextWidth(t, strip.SizePt));
        int index = Array.IndexOf(numbers, circled);

        DiscFit disc = Pixels.Disc(sheet, strip.Erase, RenderFixture.Dpi, f.Background);

        string fit = string.Create(CultureInfo.InvariantCulture, $"disc center ({disc.CenterX:0.00}, {disc.CenterY:0.00}) r {disc.Radius:0.00}, white center ({disc.WhiteCenterX:0.00}, {disc.WhiteCenterY:0.00}), rim clearance {disc.RimClearance:0.00} pt");
        disc.CenterX.Should().BeInRange(x[index], x[index] + f.Fonts.TextWidth(circled, strip.SizePt), "the disc is on " + circled + ": " + fit);
        disc.Radius.Should().BeApproximately(strip.CircleDiameterPt / 2, 0.35, fit);
        (disc.WhiteCenterX - disc.CenterX).Should().BeInRange(-0.35, 0.35, "the white number is centered across the disc: " + fit);
        (disc.WhiteCenterY - disc.CenterY).Should().BeInRange(-0.35, 0.35, "and down it: " + fit);
        disc.RimClearance.Should().BeGreaterThanOrEqualTo(0.6, "no white digit grazes the rim: " + fit);
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
    public void Week_2_Monday_strip_keeps_more_air_between_numbers_than_inside_them()
    {
        // Measured on the pixels, independent of StripPositions: with the even pitch the two-digit numbers ran together
        // ("14", the disc on 13, "12 11 10" had 1.6 to 3.3 pt of air between them, less than the 2.6 pt inside "11").
        using SKBitmap sheet = RenderFixture.Decode(f.Render(MonJan4));
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        string[] numbers = DateSlots.StripNumbers(14);
        double cap = f.Fonts.TextWidth("0", strip.SizePt) * 1.4;
        RectPt band = new(strip.NumberSpan.From - 4, strip.BaselineY - cap, strip.NumberSpan.To - strip.NumberSpan.From + 10, cap + 1);
        IReadOnlyList<InkCluster> clusters = Pixels.InkClusters(sheet, band, RenderFixture.Dpi, f.Background, mergeGapPt: 0.3);

        // One cluster per digit glyph, the circled 13 (index 1) one disc: group the clusters back into numbers.
        int[] glyphs = numbers.Select((n, i) => i == 1 ? 1 : n.Length).ToArray();
        clusters.Should().HaveCount(glyphs.Sum(), "the digits and the disc are separate: " + string.Join(" ", clusters.Select(c => $"{c.LeftPt:0.0}-{c.RightPt:0.0}")));
        List<double> between = new();
        List<double> inside = new();
        int next = 0;
        for (int i = 0; i < numbers.Length; i++)
        {
            for (int g = 1; g < glyphs[i]; g++)
            {
                inside.Add(clusters[next + g].LeftPt - clusters[next + g - 1].RightPt);
            }

            next += glyphs[i];
            if (i < numbers.Length - 1)
            {
                between.Add(clusters[next].LeftPt - clusters[next - 1].RightPt);
            }
        }

        between.Should().HaveCount(13);
        between.Min().Should().BeGreaterThan(4.0, "every pair of neighbors has clear air: " + string.Join(" ", between.Select(g => g.ToString("0.0", CultureInfo.InvariantCulture))));
        between.Min().Should().BeGreaterThan(inside.Max(), "the space between two numbers is wider than the space inside one, so 14 13 12 11 10 read apart");
    }

    [Fact]
    public void Thirteen_numbers_use_the_printed_positions_exactly()
    {
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        string[] thirteen = DateSlots.StripNumbers(13);

        DateSlots.StripPositions(strip, thirteen, t => f.Fonts.TextWidth(t, strip.SizePt)).Should().Equal(strip.NumberX);
        DateSlots.StripPositions(strip, thirteen, t => 3.0 * t.Length).Should().Equal(strip.NumberX, "the printed left edges win whatever the face's digit widths");
    }

    [Fact]
    public void Fourteen_numbers_have_equal_gaps_from_the_span_start_to_the_span_end()
    {
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        Func<string, double> width = t => f.Fonts.TextWidth(t, strip.SizePt);
        string[] numbers = DateSlots.StripNumbers(14);
        numbers.Should().StartWith(new[] { "14", "13" }).And.EndWith("1");

        IReadOnlyList<double> x = DateSlots.StripPositions(strip, numbers, width);

        x.Should().HaveCount(14);
        x[0].Should().Be(strip.NumberSpan.From, "the first number starts at numberSpan.from");
        (x[^1] + width(numbers[^1])).Should().BeApproximately(strip.NumberSpan.To, 0.001, "the last number ends at numberSpan.to");
        double[] gaps = Enumerable.Range(0, 13).Select(i => x[i + 1] - (x[i] + width(numbers[i]))).ToArray();
        gaps.Should().OnlyContain(g => g > 0, "neighbors never touch");
        (gaps.Max() - gaps.Min()).Should().BeLessThanOrEqualTo(0.1, "the air between neighbors is equal: " + string.Join(" ", gaps.Select(g => g.ToString("0.00", CultureInfo.InvariantCulture))));
        double expectedGap = (strip.NumberSpan.To - strip.NumberSpan.From - numbers.Sum(width)) / 13;
        gaps.Should().OnlyContain(g => Math.Abs(g - expectedGap) < 1e-9);
        ((x[^1] - x[0]) / 13).Should().BeInRange(13.5, 14.2, "the left edges still average the PRD's 'about 13.8 pt'");
        (x[1] - x[0]).Should().BeGreaterThan(x[^1] - x[^2], "a two-digit number takes a wider pitch than a one-digit one");
    }

    [Fact]
    public void Equal_gaps_follow_the_widths_of_the_numbers()
    {
        // Bookman's measured tabular digits (6.2 pt at 10 pt): 19 digits leave (186 - 117.8) / 13 pt between neighbors.
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining with { NumberSpan = new Span(537, 723) };
        string[] numbers = DateSlots.StripNumbers(14);
        IReadOnlyList<double> x = DateSlots.StripPositions(strip, numbers, t => 6.2 * t.Length);
        double gap = (186 - 117.8) / 13;
        double[] expected = new double[14];
        double before = 0;
        for (int k = 0; k < 14; k++)
        {
            expected[k] = 537 + before + (k * gap);
            before += 6.2 * numbers[k].Length;
        }

        x.Should().HaveCount(14);
        for (int k = 0; k < 14; k++)
        {
            x[k].Should().BeApproximately(expected[k], 1e-9, $"number {numbers[k]} starts after the widths of the numbers before it and {k} gaps");
        }

        x[1].Should().BeApproximately(537 + 12.4 + gap, 1e-9);
        x[^1].Should().BeApproximately(723 - 6.2, 1e-9);

        // A proportional face (a narrow "1") still gets equal air; only the pitch changes.
        Func<string, double> proportional = t => t.Sum(ch => ch == '1' ? 4.0 : 6.2);
        IReadOnlyList<double> p = DateSlots.StripPositions(strip, numbers, proportional);
        double[] gaps = Enumerable.Range(0, 13).Select(i => p[i + 1] - (p[i] + proportional(numbers[i]))).ToArray();
        gaps.Should().OnlyContain(g => Math.Abs(g - gaps[0]) < 1e-9);
        (p[^1] + proportional("1")).Should().BeApproximately(723, 1e-9);
    }

    [Fact]
    public void Numbers_too_wide_for_the_span_are_reported_against_the_manifest_key()
    {
        // 6.25 pt per digit is exact in binary: the 19 digits of 14 down to 1 are 118.75 pt wide.
        string[] numbers = DateSlots.StripNumbers(14);
        IReadOnlyList<double> Layout(double to) =>
            DateSlots.StripPositions(M.Slots.WeeksRemaining with { NumberSpan = new Span(537, to) }, numbers, t => 6.25 * t.Length);

        FluentActions.Invoking(() => Layout(650)).Should().Throw<InvalidOperationException>()
            .WithMessage("*slots.weeksRemaining.numberSpan (537 to 650, 113 pt) is too narrow for 14 numbers at 10 pt*118.8 pt wide*");
        FluentActions.Invoking(() => Layout(537 + 118.75)).Should().Throw<InvalidOperationException>("touching numbers leave no space between them");
        Layout(537 + 119).Should().HaveCount(14, "a quarter point spread over 13 gaps still separates the numbers");
    }

    [Fact]
    public void One_number_starts_at_the_span_start_and_none_gives_no_positions()
    {
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;

        DateSlots.StripPositions(strip, DateSlots.StripNumbers(1), t => 6.2 * t.Length).Should().Equal(strip.NumberSpan.From);
        DateSlots.StripPositions(strip, DateSlots.StripNumbers(0), t => 6.2 * t.Length).Should().BeEmpty();
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

    /// <summary>The horizontal middle of a text's ink in points from its origin (where the renderer centers the disc).</summary>
    private double InkCenter(string text, double sizePt)
    {
        using SKFont font = f.Fonts.Font(sizePt);
        font.MeasureText(text, out SKRect bounds);
        return (bounds.Left + bounds.Right) / 2;
    }
}
