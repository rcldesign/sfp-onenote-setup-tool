using Sfp.Core.Manifests;
using Sfp.Render.Colors;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.1: PDFium rendering through the rasterizer, its pixel sizing and its page cache.</summary>
public class RasterizerTests
{
    private static readonly Manifest M = TestManifest.Shipped;
    private static readonly SKColor Background = ColorParser.Parse(M.Colors.Background);

    [Fact]
    public void Page_17_of_the_synthetic_template_at_220_dpi_is_3398_by_2353_with_the_background_color()
    {
        using Rasterizer rasterizer = new(Background);
        using SKBitmap page = rasterizer.Render(SyntheticTemplate.Build(), 17, 220);

        page.Width.Should().Be(3398);
        page.Height.Should().Be(2353);

        // A 200 x 200 px patch in an empty region (between the Plan column and the time blocks).
        SKColor mean = Pixels.Mean(page, new SKRectI(1800, 1500, 2000, 1700));
        ColorParser.MaxChannelDistance(mean, Background).Should().BeLessThanOrEqualTo(2, "the patch should be bare background, got " + ColorParser.ToHex(mean));
        ColorParser.MaxChannelDistance(Pixels.Dominant(page), Background).Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Rendered_page_carries_the_printed_art_as_ink()
    {
        using Rasterizer rasterizer = new(Background);
        using SKBitmap page = rasterizer.Render(SyntheticTemplate.Build(), 17, 220);
        HeadingSpec heading = M.Generated.Heading;
        SKColor ink = ColorParser.Parse(M.Colors.Ink);

        RectPt dayName = new(heading.X, heading.BaselineY - heading.SizePt, 120, heading.SizePt);
        Pixels.HasInk(page, dayName, 220, Background).Should().BeTrue("the day name is printed there");
        SKColor darkest = Pixels.Darkest(page, Pixels.ToPixels(dayName, 220, page));
        ColorParser.MaxChannelDistance(darkest, ink).Should().BeLessThanOrEqualTo(8, "solid glyph pixels are template ink");
    }

    [Theory]
    [InlineData(1112, 220, 3398)]
    [InlineData(770, 220, 2353)]
    [InlineData(1112, 72, 1112)]
    [InlineData(770, 72, 770)]
    [InlineData(1112, 144, 2224)]
    [InlineData(770, 144, 1540)]
    [InlineData(1112, 300, 4633)]
    [InlineData(770, 300, 3208)]
    public void Pixel_size_is_points_times_dpi_over_72_rounded(double pt, int dpi, int expectedPx)
    {
        Rasterizer.PixelsFor(pt, dpi).Should().Be(expectedPx);
        new PdfPageSize(pt, pt).PixelsAt(dpi).WidthPx.Should().Be(expectedPx);
    }

    [Fact]
    public void Render_at_72_dpi_gives_one_pixel_per_point()
    {
        using Rasterizer rasterizer = new(Background);
        using SKBitmap page = rasterizer.Render(SyntheticTemplate.Build(), 1, 72);
        (page.Width, page.Height).Should().Be((1112, 770));
    }

    [Fact]
    public void Page_count_and_page_size_come_from_the_document()
    {
        using Rasterizer rasterizer = new(Background);
        string pdf = SyntheticTemplate.Build();
        rasterizer.PageCount(pdf).Should().Be(M.PageCount);
        rasterizer.PageSize(pdf, 17).Should().Be(new PdfPageSize(M.PageWidthPt, M.PageHeightPt));
    }

    [Fact]
    public void Cached_page_is_handed_out_as_an_independent_copy()
    {
        using Rasterizer rasterizer = new(Background);
        string pdf = SyntheticTemplate.Build();

        using SKBitmap first = rasterizer.Render(pdf, 17, 72);
        first.Erase(SKColors.Red);

        using SKBitmap second = rasterizer.Render(pdf, 17, 72);
        second.GetPixel(600, 500).Should().Be(Background, "drawing on the first copy must not reach the cache");
        second.IsImmutable.Should().BeFalse("callers draw on their copy");
        rasterizer.CachedPages.Should().Be(1);
    }

    [Fact]
    public void Cache_keeps_the_most_recently_used_pages_up_to_its_capacity()
    {
        using Rasterizer rasterizer = new(Background, cacheCapacity: 2);
        string pdf = SyntheticTemplate.Build();
        rasterizer.Render(pdf, 1, 72).Dispose();
        rasterizer.Render(pdf, 2, 72).Dispose();
        rasterizer.Render(pdf, 3, 72).Dispose();
        rasterizer.CachedPages.Should().Be(2);

        rasterizer.ClearCache();
        rasterizer.CachedPages.Should().Be(0);
        rasterizer.Render(pdf, 1, 72).Dispose();
        rasterizer.CachedPages.Should().Be(1);
    }

    [Fact]
    public void Cache_can_be_switched_off()
    {
        using Rasterizer rasterizer = new(Background, cacheCapacity: 0);
        using SKBitmap page = rasterizer.Render(SyntheticTemplate.Build(), 1, 72);
        page.Width.Should().Be(1112);
        rasterizer.CachedPages.Should().Be(0);
    }

    [Fact]
    public void Different_dpis_of_the_same_page_are_cached_separately()
    {
        using Rasterizer rasterizer = new(Background);
        string pdf = SyntheticTemplate.Build();
        using SKBitmap small = rasterizer.Render(pdf, 17, 72);
        using SKBitmap large = rasterizer.Render(pdf, 17, 144);
        small.Width.Should().Be(1112);
        large.Width.Should().Be(2224);
        rasterizer.CachedPages.Should().Be(2);
    }

    [Fact]
    public void Page_number_outside_the_document_is_refused_with_the_page_count()
    {
        using Rasterizer rasterizer = new(Background);
        string pdf = SyntheticTemplate.Build();
        Action tooHigh = () => rasterizer.Render(pdf, M.PageCount + 1, 72);
        Action zero = () => rasterizer.Render(pdf, 0, 72);
        tooHigh.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*132 pages*1-based*");
        zero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Missing_pdf_is_reported_by_path()
    {
        using Rasterizer rasterizer = new(Background);
        string missing = Path.Combine(TestPaths.NewTempDir("missing"), "nope.pdf");
        Action act = () => rasterizer.Render(missing, 1, 72);
        act.Should().Throw<FileNotFoundException>().WithMessage("*nope.pdf*");
    }

    [Fact]
    public void Invalid_dpi_is_refused()
    {
        using Rasterizer rasterizer = new(Background);
        Action act = () => rasterizer.Render(SyntheticTemplate.Build(), 1, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Disposed_rasterizer_refuses_further_renders()
    {
        Rasterizer rasterizer = new(Background);
        string pdf = SyntheticTemplate.Build();
        rasterizer.Render(pdf, 1, 72).Dispose();
        rasterizer.Dispose();
        rasterizer.Dispose();
        Action act = () => rasterizer.Render(pdf, 1, 72);
        act.Should().Throw<ObjectDisposedException>();
    }

    [SkippableFact]
    public void Page_17_of_the_real_planner_at_220_dpi_is_3398_by_2353_on_the_beige_background()
    {
        string pdf = SfpPdf.RequireOrSkip();
        using Rasterizer rasterizer = new(Background);
        rasterizer.PageCount(pdf).Should().Be(M.PageCount);
        using SKBitmap page = rasterizer.Render(pdf, 17, 220);
        page.Width.Should().Be(3398);
        page.Height.Should().Be(2353);
        SKColor dominant = Pixels.Dominant(page);
        ColorParser.MaxChannelDistance(dominant, Background).Should().BeLessThanOrEqualTo(2, "the page background is " + M.Colors.Background + ", got " + ColorParser.ToHex(dominant));
    }
}
