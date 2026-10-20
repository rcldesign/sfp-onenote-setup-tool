using Sfp.Render.Colors;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

public class ColorParserTests
{
    [Theory]
    [InlineData("#DED4C8", 0xDE, 0xD4, 0xC8)]
    [InlineData("#1e1e20", 0x1E, 0x1E, 0x20)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    public void Parses_hash_rrggbb_in_either_case(string hex, int r, int g, int b)
    {
        ColorParser.Parse(hex).Should().Be(new SKColor((byte)r, (byte)g, (byte)b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DED4C8")]
    [InlineData("#DED4C")]
    [InlineData("#DED4C8FF")]
    [InlineData("#GGGGGG")]
    [InlineData("# ED4C8")]
    public void Rejects_anything_but_hash_rrggbb(string? hex)
    {
        ColorParser.TryParse(hex, out _).Should().BeFalse();
        Action act = () => ColorParser.Parse(hex!);
        act.Should().Throw<FormatException>().WithMessage("*#RRGGBB*");
    }

    [Fact]
    public void Manifest_colors_all_parse_and_round_trip_to_upper_case_hex()
    {
        Core.Manifests.ColorSpec colors = TestManifest.Shipped.Colors;
        foreach (string hex in new[] { colors.Background, colors.Ink, colors.DayBox }.Concat(colors.SectionTints))
        {
            ColorParser.ToHex(ColorParser.Parse(hex)).Should().Be(hex.ToUpperInvariant());
        }
    }

    [Theory]
    [InlineData(1.0, 255)]
    [InlineData(0.6, 153)]
    [InlineData(0.4, 102)]
    [InlineData(0.0, 0)]
    [InlineData(1.5, 255)]
    [InlineData(-1, 0)]
    public void Opacity_maps_to_alpha_and_clamps(double opacity, int alpha)
    {
        SKColor c = ColorParser.WithOpacity(new SKColor(10, 20, 30), opacity);
        c.Alpha.Should().Be((byte)alpha);
        (c.Red, c.Green, c.Blue).Should().Be(((byte)10, (byte)20, (byte)30));
    }

    [Fact]
    public void Channel_distance_is_the_largest_channel_difference()
    {
        ColorParser.MaxChannelDistance(new SKColor(10, 20, 30), new SKColor(12, 25, 30)).Should().Be(5);
        ColorParser.MaxChannelDistance(new SKColor(10, 20, 30), new SKColor(10, 20, 30).WithAlpha(0)).Should().Be(0);
    }
}
