using System.Buffers.Binary;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.3: PNG output is 24-bit RGB with no alpha, and byte-for-byte stable.</summary>
public class PngEncoderTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly SKColor Bg = new(0xDE, 0xD4, 0xC8);
    private static readonly SKColor Ink = new(0x1E, 0x1E, 0x20);

    [Fact]
    public void Encodes_an_opaque_24_bit_rgb_png_without_alpha()
    {
        using SKBitmap bitmap = Sample();
        byte[] png = PngEncoder.Encode(bitmap);

        png.AsSpan(0, 8).ToArray().Should().Equal(PngSignature);
        BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)).Should().Be(40, "IHDR width");
        BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)).Should().Be(30, "IHDR height");
        png[24].Should().Be(8, "IHDR bit depth");
        png[25].Should().Be(2, "IHDR color type 2 is truecolor RGB without alpha");

        using SKBitmap decoded = SKBitmap.Decode(png);
        decoded.GetPixel(0, 0).Should().Be(Bg);
        decoded.GetPixel(3, 4).Should().Be(Ink);
        decoded.GetPixel(3, 4).Alpha.Should().Be(255);
    }

    [Fact]
    public void Encoding_is_deterministic_and_changes_with_the_pixels()
    {
        using SKBitmap bitmap = Sample();
        byte[] first = PngEncoder.Encode(bitmap);
        byte[] second = PngEncoder.Encode(bitmap);
        first.Should().Equal(second);

        bitmap.SetPixel(10, 10, SKColors.Red);
        PngEncoder.Encode(bitmap).Should().NotEqual(first);
    }

    [Fact]
    public void Rgb888x_input_is_encoded_directly_with_the_same_result()
    {
        using SKBitmap bitmap = Sample();
        using SKBitmap rgb = PngEncoder.ToRgb888x(bitmap);
        rgb.ColorType.Should().Be(SKColorType.Rgb888x);
        rgb.GetPixel(3, 4).Should().Be(Ink);
        PngEncoder.Encode(rgb).Should().Equal(PngEncoder.Encode(bitmap));
    }

    private static SKBitmap Sample()
    {
        SKBitmap bitmap = new(new SKImageInfo(40, 30, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(Bg);
        bitmap.SetPixel(3, 4, Ink);
        return bitmap;
    }
}
