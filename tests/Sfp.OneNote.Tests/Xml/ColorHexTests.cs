using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Xml;

public class ColorHexTests
{
    [Fact]
    public void Ink_at_sixty_percent_over_the_page_beige_is_a_fixed_hex()
    {
        ColorHex.Blend("#1E1E20", "#DED4C8", 0.6).Should().Be("#6B6763");
    }

    [Theory]
    [InlineData(1.0, "#1E1E20")]
    [InlineData(0.0, "#DED4C8")]
    [InlineData(0.4, "#918B85")]
    [InlineData(0.7, "#585552")]
    public void Blend_moves_from_background_to_ink_as_opacity_rises(double opacity, string expected)
    {
        ColorHex.Blend("#1e1e20", "#ded4c8", opacity).Should().Be(expected);
    }

    [Fact]
    public void Opacity_outside_zero_to_one_is_clamped()
    {
        ColorHex.Blend("#1E1E20", "#DED4C8", 1.5).Should().Be("#1E1E20");
        ColorHex.Blend("#1E1E20", "#DED4C8", -1).Should().Be("#DED4C8");
    }

    [Theory]
    [InlineData("1E1E20")]
    [InlineData("#1E1E2")]
    [InlineData("#GGGGGG")]
    [InlineData("automatic")]
    public void Anything_but_hash_rrggbb_is_rejected(string bad)
    {
        Action act = () => ColorHex.Parse(bad);
        act.Should().Throw<FormatException>();
    }
}
