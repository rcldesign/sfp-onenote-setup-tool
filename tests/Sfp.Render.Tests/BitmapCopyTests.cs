using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>Wave 3 fix: pixel copies read straight from the source (no SKBitmap.Copy shader) keep the layout and the pixels.</summary>
public sealed class BitmapCopyTests
{
    private static readonly SKColor Bg = new(0xDE, 0xD4, 0xC8);
    private static readonly SKColor Ink = new(0x1E, 0x1E, 0x20);

    [Fact]
    public void Clone_is_an_independent_copy_with_the_same_layout_and_pixels()
    {
        using SKBitmap source = Sample();
        using SKBitmap clone = BitmapCopy.Clone(source);

        clone.Info.Should().Be(source.Info);
        clone.GetPixelSpan().ToArray().Should().Equal(source.GetPixelSpan().ToArray());
        clone.IsImmutable.Should().BeFalse();

        clone.Erase(SKColors.Red);
        source.GetPixel(3, 4).Should().Be(Ink, "drawing on the clone never reaches the source");
        source.GetPixel(0, 0).Should().Be(Bg);
    }

    [Fact]
    public void Clone_of_an_immutable_bitmap_is_mutable()
    {
        using SKBitmap source = Sample();
        source.SetImmutable();
        using SKBitmap clone = BitmapCopy.Clone(source);

        clone.IsImmutable.Should().BeFalse("the rasterizer hands its cached pages out as copies callers draw on");
        clone.GetPixel(3, 4).Should().Be(Ink);
    }

    [Fact]
    public void Convert_changes_the_layout_and_keeps_the_colors()
    {
        using SKBitmap source = Sample();
        using SKBitmap rgb = BitmapCopy.Convert(source, SKColorType.Rgb888x, SKAlphaType.Opaque);

        rgb.ColorType.Should().Be(SKColorType.Rgb888x);
        rgb.AlphaType.Should().Be(SKAlphaType.Opaque);
        (rgb.Width, rgb.Height).Should().Be((40, 30));
        rgb.GetPixel(0, 0).Should().Be(Bg);
        rgb.GetPixel(3, 4).Should().Be(Ink);
    }

    private static SKBitmap Sample()
    {
        SKBitmap bitmap = new(new SKImageInfo(40, 30, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(Bg);
        bitmap.SetPixel(3, 4, Ink);
        return bitmap;
    }
}
