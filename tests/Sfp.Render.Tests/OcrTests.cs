using Sfp.Core.Manifests;
using Sfp.Render.Colors;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>The OCR helper that later render tests use to read baked text back.</summary>
public class OcrTests
{
    private static readonly Manifest M = TestManifest.Shipped;

    [SkippableFact]
    public void Reads_the_day_name_from_a_rendered_synthetic_monday_page()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine (tesseract CLI or python3 with tesserocr) is available on this machine.");

        using Rasterizer rasterizer = new(ColorParser.Parse(M.Colors.Background));
        using SKBitmap page = rasterizer.Render(SyntheticTemplate.Build(), 17, 220);
        HeadingSpec heading = M.Generated.Heading;
        using SKBitmap crop = Ocr.Crop(page, new RectPt(heading.X, heading.BaselineY - heading.SizePt, 180, heading.SizePt * 1.3), 220, paddingPt: 8);

        Ocr.TryRead(crop, out string text).Should().BeTrue("engine " + Ocr.EngineName + " should read the crop");
        Ocr.Normalize(text).Should().Be("Monday");
    }

    [Fact]
    public void Normalize_collapses_whitespace_and_line_breaks()
    {
        Ocr.Normalize("  01 \n/  04\t/ 27 \r\n").Should().Be("01 / 04 / 27");
        Ocr.Normalize("").Should().BeEmpty();
    }

    [Fact]
    public void Crop_takes_the_padded_region_in_pixels_and_clips_to_the_bitmap()
    {
        using SKBitmap source = new(new SKImageInfo(1112, 770, SKColorType.Bgra8888, SKAlphaType.Premul));
        source.Erase(SKColors.White);
        source.SetPixel(100, 50, SKColors.Black);

        using SKBitmap crop = Ocr.Crop(source, new RectPt(90, 40, 20, 20), dpi: 72, paddingPt: 5);
        crop.Width.Should().Be(30);
        crop.Height.Should().Be(30);
        crop.ColorType.Should().Be(source.ColorType);
        crop.GetPixel(15, 15).Should().Be(SKColors.Black);
        for (int y = 13; y <= 17; y++)
        {
            for (int x = 13; x <= 17; x++)
            {
                crop.GetPixel(x, y).Should().Be(source.GetPixel(85 + x, 35 + y), "the crop is the source's pixels one to one");
            }
        }

        using SKBitmap edge = Ocr.Crop(source, new RectPt(1100, 760, 50, 50), dpi: 72, paddingPt: 0);
        edge.Width.Should().Be(12);
        edge.Height.Should().Be(10);
    }

    [Fact]
    public void TryRead_on_png_bytes_reports_false_without_an_engine_instead_of_throwing()
    {
        using SKBitmap bitmap = new(new SKImageInfo(10, 10));
        bitmap.Erase(SKColors.White);
        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        bool read = Ocr.TryRead(png.ToArray(), out string text);
        read.Should().Be(Ocr.IsAvailable);
        if (!read)
        {
            text.Should().BeEmpty();
        }
    }
}
