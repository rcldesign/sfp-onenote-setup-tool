using Sfp.Core;
using Sfp.Core.Manifests;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;
using Sfp.Render.Overlay;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.3: overlay primitives in points on a blank sheet at 72 and 220 DPI, checked pixel by pixel.</summary>
public sealed class SheetCanvasTests : IDisposable
{
    private static readonly Manifest M = TestManifest.Shipped;
    private static readonly SKColor Bg = ColorParser.Parse(M.Colors.Background);
    private static readonly SKColor Ink = ColorParser.Parse(M.Colors.Ink);

    private readonly FontService fonts = FontService.Load(M.Font, TestPaths.TemplatesDir, Array.Empty<string>());

    public void Dispose() => fonts.Dispose();

    [Theory]
    [InlineData(72)]
    [InlineData(220)]
    public void Blank_sheet_has_the_page_size_in_pixels_and_the_background_everywhere(int dpi)
    {
        using SheetCanvas sheet = SheetCanvas.Blank(M.PageWidthPt, M.PageHeightPt, dpi, Bg);
        sheet.Bitmap.Width.Should().Be(Rasterizer.PixelsFor(M.PageWidthPt, dpi));
        sheet.Bitmap.Height.Should().Be(Rasterizer.PixelsFor(M.PageHeightPt, dpi));
        sheet.Scale.Should().BeApproximately(dpi / 72f, 0.0001f);
        sheet.WidthPt.Should().BeApproximately(M.PageWidthPt, 0.5);
        sheet.PixelAt(0, 0).Should().Be(Bg);
        sheet.PixelAt(M.PageWidthPt - 1, M.PageHeightPt - 1).Should().Be(Bg);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(220)]
    public void Erase_paints_background_over_a_region_and_fill_paints_a_color(int dpi)
    {
        using SheetCanvas sheet = SheetCanvas.Blank(M.PageWidthPt, M.PageHeightPt, dpi, Bg);
        RectPt strip = M.Slots.WeeksRemaining.Erase;

        sheet.FillRect(strip, Ink);
        sheet.PixelAt(strip.CenterX, strip.CenterY).Should().Be(Ink);
        sheet.PixelAt(strip.X + 1, strip.Y + 1).Should().Be(Ink);
        sheet.PixelAt(strip.X - 2, strip.CenterY).Should().Be(Bg, "outside the rectangle stays untouched");

        sheet.Erase(strip, Bg);
        sheet.PixelAt(strip.CenterX, strip.CenterY).Should().Be(Bg);
        sheet.PixelAt(strip.X + 1, strip.Y + 1).Should().Be(Bg);
        sheet.MeanColor(strip).Should().Be(Bg);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(220)]
    public void Text_is_inked_at_the_baseline_and_anchored_by_alignment(int dpi)
    {
        using SheetCanvas sheet = SheetCanvas.Blank(M.PageWidthPt, M.PageHeightPt, dpi, Bg);
        DateSlot date = M.Slots.Date;
        using SKFont font = fonts.Font(date.SizePt, sheet.Scale);
        double cap = sheet.Measure(font).CapHeight;
        double width = sheet.TextWidth("04", font);
        width.Should().BeApproximately(fonts.TextWidth("04", date.SizePt), 0.05, "TextWidth reports points");

        DateField month = date.Fields.Single(f => f.Name == "month");
        DateField day = date.Fields.Single(f => f.Name == "day");
        DateField year = date.Fields.Single(f => f.Name == "year");
        sheet.Text("01", month.X, date.BaselineY, month.Align, font, Ink);
        sheet.Text("04", day.X, date.BaselineY, day.Align, font, Ink);
        sheet.Text("27", year.X, date.BaselineY, year.Align, font, Ink);

        double top = date.BaselineY - cap;
        HasInk(sheet, new RectPt(month.X - width, top, width, cap)).Should().BeTrue("right-aligned text ends at its x");
        HasInk(sheet, new RectPt(month.X + 1, top, 4, cap)).Should().BeFalse("nothing is drawn right of a right-aligned x");
        HasInk(sheet, new RectPt(day.X - (width / 2), top, width / 2, cap)).Should().BeTrue("centered text has ink left of center");
        HasInk(sheet, new RectPt(day.X, top, width / 2, cap)).Should().BeTrue("centered text has ink right of center");
        HasInk(sheet, new RectPt(year.X, top, width, cap)).Should().BeTrue("left-aligned text starts at its x");
        HasInk(sheet, new RectPt(year.X - 5, top, 4, cap)).Should().BeFalse("nothing is drawn left of a left-aligned x");
        HasInk(sheet, new RectPt(year.X, date.BaselineY + 1, width, 6)).Should().BeFalse("digits do not descend below the baseline");

        SKColor darkest = Pixels.Darkest(sheet.Bitmap, Pixels.ToPixels(new RectPt(year.X, top, width, cap), dpi, sheet.Bitmap));
        ColorParser.MaxChannelDistance(darkest, Ink).Should().BeLessThanOrEqualTo(6, "solid glyph pixels are template ink");
    }

    [Fact]
    public void Text_overload_with_the_font_service_matches_an_explicit_font_at_the_sheet_scale()
    {
        using SheetCanvas a = SheetCanvas.Blank(200, 60, 220, Bg);
        using SheetCanvas b = SheetCanvas.Blank(200, 60, 220, Bg);
        using SKFont font = fonts.Font(10, b.Scale);
        a.Text("13", 20, 40, TextAlign.Left, fonts, 10, Ink);
        b.Text("13", 20, 40, TextAlign.Left, font, Ink);
        a.Bitmap.Bytes.Should().Equal(b.Bitmap.Bytes);
    }

    [Fact]
    public void Opacity_lightens_text_toward_the_background()
    {
        using SheetCanvas sheet = SheetCanvas.Blank(300, 80, 220, Bg);
        using SKFont font = fonts.Font(15, sheet.Scale);
        sheet.Text("Day", 20, 50, TextAlign.Left, font, Ink, opacity: 0.4);

        SKColor darkest = Pixels.Darkest(sheet.Bitmap, Pixels.ToPixels(new RectPt(20, 30, 60, 22), 220, sheet.Bitmap));
        ((int)darkest.Red).Should().BeGreaterThan(Ink.Red + 60, "40% ink is well lighter than full ink");
        ((int)darkest.Red).Should().BeLessThan(Bg.Red - 40, "but still visible against the background");
    }

    [Fact]
    public void Empty_text_draws_nothing()
    {
        using SheetCanvas sheet = SheetCanvas.Blank(100, 50, 72, Bg);
        using SKFont font = fonts.Font(10, sheet.Scale);
        sheet.Text("", 10, 30, TextAlign.Left, font, Ink);
        sheet.TextWidth("", font).Should().Be(0);
        sheet.MeanColor(new RectPt(0, 0, 100, 50)).Should().Be(Bg);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(220)]
    public void Circle_is_filled_or_outlined_around_its_center(int dpi)
    {
        using SheetCanvas sheet = SheetCanvas.Blank(M.PageWidthPt, M.PageHeightPt, dpi, Bg);
        WeeksRemainingSlot strip = M.Slots.WeeksRemaining;
        double d = strip.CircleDiameterPt;
        double cy = strip.CircleCenterY;

        sheet.Circle(600, cy, d, Ink, filled: true);
        sheet.PixelAt(600, cy).Should().Be(Ink);
        sheet.PixelAt(600 + (d / 2) - 1.5, cy).Should().Be(Ink, "inside the rim");
        sheet.PixelAt(600, cy + (d / 2) + 2).Should().Be(Bg, "outside the circle");

        sheet.Circle(650, cy, d, Ink, filled: false, outlineWidthPt: 1);
        sheet.PixelAt(650, cy).Should().Be(Bg, "an outline leaves the center empty");
        HasInk(sheet, new RectPt(650 + (d / 2) - 1.5, cy - 1, 3, 2)).Should().BeTrue("the rim is inked");
    }

    [Theory]
    [InlineData(72)]
    [InlineData(220)]
    public void Rules_ink_their_path_and_nothing_beside_it(int dpi)
    {
        using SheetCanvas sheet = SheetCanvas.Blank(M.PageWidthPt, M.PageHeightPt, dpi, Bg);
        LinePt slash = M.Slots.Date.Slashes[0];
        sheet.Rule(slash.X1, slash.Y1, slash.X2, slash.Y2, 1.0, Ink);
        double mx = (slash.X1 + slash.X2) / 2;
        double my = (slash.Y1 + slash.Y2) / 2;
        HasInk(sheet, new RectPt(mx - 1, my - 1, 2, 2)).Should().BeTrue("the slash passes through its midpoint");
        HasInk(sheet, new RectPt(mx + 6, my - 1, 2, 2)).Should().BeFalse("six points beside a 1 pt line is background");

        MonthRule rule = M.Slots.MonthName.Rule!;
        sheet.Rule(rule.X1, rule.Y, rule.X2, rule.Y, 0.12, Ink);
        HasInk(sheet, new RectPt(900, rule.Y - 1, 2, 2)).Should().BeTrue("a hairline is drawn at least one pixel wide");
        HasInk(sheet, new RectPt(900, rule.Y + 3, 2, 2)).Should().BeFalse();
        HasInk(sheet, new RectPt(rule.X1 - 6, rule.Y - 1, 4, 2)).Should().BeFalse("the rule starts at x1");
    }

    [Theory]
    [InlineData(72)]
    [InlineData(220)]
    public void Rect_outline_inks_the_edges_and_leaves_the_inside(int dpi)
    {
        using SheetCanvas sheet = SheetCanvas.Blank(M.PageWidthPt, M.PageHeightPt, dpi, Bg);
        RectPt box = M.Slots.DayGrid.Box(0, 0);
        sheet.RectOutline(box, 0.5, Ink);
        sheet.PixelAt(box.CenterX, box.CenterY).Should().Be(Bg);
        HasInk(sheet, new RectPt(box.CenterX - 1, box.Y - 1, 2, 2)).Should().BeTrue("top edge");
        HasInk(sheet, new RectPt(box.X - 1, box.CenterY - 1, 2, 2)).Should().BeTrue("left edge");
        HasInk(sheet, new RectPt(box.Right - 1, box.CenterY - 1, 2, 2)).Should().BeTrue("right edge");
        HasInk(sheet, new RectPt(box.CenterX - 1, box.Bottom - 1, 2, 2)).Should().BeTrue("bottom edge");
    }

    [Fact]
    public void Measure_reports_font_metrics_in_points_independent_of_dpi()
    {
        using SheetCanvas low = SheetCanvas.Blank(100, 100, 72, Bg);
        using SheetCanvas high = SheetCanvas.Blank(100, 100, 220, Bg);
        using SKFont lowFont = fonts.Font(10, low.Scale);
        using SKFont highFont = fonts.Font(10, high.Scale);

        TextMetricsPt a = low.Measure(lowFont);
        TextMetricsPt b = high.Measure(highFont);
        a.Ascent.Should().BePositive();
        a.Descent.Should().BePositive();
        a.CapHeight.Should().BeInRange(6, 8, "10 pt Bookman-style capitals are about 7 pt tall");
        b.CapHeight.Should().BeApproximately(a.CapHeight, 0.05);
        b.Ascent.Should().BeApproximately(a.Ascent, 0.05);
    }

    [Fact]
    public void Wrapping_a_bitmap_draws_into_it_and_leaves_ownership_with_the_caller()
    {
        using SKBitmap bitmap = new(new SKImageInfo(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(Bg);
        using (SheetCanvas sheet = new(bitmap, 72))
        {
            sheet.FillRect(new RectPt(10, 10, 20, 20), Ink);
        }

        bitmap.GetPixel(15, 15).Should().Be(Ink);
        bitmap.GetPixel(50, 50).Should().Be(Bg);
    }

    [Fact]
    public void Pixel_coordinates_scale_from_points()
    {
        using SheetCanvas sheet = SheetCanvas.Blank(100, 100, 220, Bg);
        sheet.Px(72).Should().BeApproximately(220f, 0.001f);
        sheet.Dpi.Should().Be(220);
        Action bad = () => SheetCanvas.Blank(10, 10, 0, Bg);
        bad.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static bool HasInk(SheetCanvas sheet, RectPt region) => Pixels.HasInk(sheet.Bitmap, region, sheet.Dpi, Bg);
}
