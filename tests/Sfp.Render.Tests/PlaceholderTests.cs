namespace Sfp.Render.Tests;

/// <summary>Placeholder until M2.1 adds the rasterizer tests.</summary>
public class PlaceholderTests
{
    [Fact]
    public void Render_options_scale_is_dpi_over_72()
    {
        RenderOptions o = new("x.pdf", 220, new Core.Options.GenerateOptions { Start = new Core.Dates.QuarterSpec(2027, 1) });
        o.Scale.Should().BeApproximately(220f / 72f, 0.0001f);
    }
}
