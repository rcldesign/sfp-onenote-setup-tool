using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Setup;

/// <summary>Round 2 refresh-10: the Annual Objectives fallback reads the rows where a scaled sheet put them.</summary>
public class AnnualPageFallbackScaleTests
{
    private const string PageId = "{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{1}{E19512345678901234567890123456789012345678}";

    private static readonly Manifest M = RefreshManifest.Shipped;

    [Fact]
    public void A_title_typed_in_an_annual_row_of_a_page_placed_at_scale_0_8_is_read()
    {
        const double scale = 0.8;
        AnnualTitleSlot slot = M.Slots.AnnualTitle;
        RectPt row1 = slot.Rect(0);
        RectPt row2 = slot.Rect(1);
        PageXml annual = new PageXmlFixture(PageId, "annual:1", "Annual Objectives", sheetX: 0, sheetY: 36, sheetWidth: M.PageWidthPt * scale, sheetHeight: M.PageHeightPt * scale)
            .Typed(Scaled(row1, scale), "Grow revenue 20%")
            .Typed(Scaled(row2, scale), "Hire two PMs")
            .Build();
        AnnualPageFallback fallback = new(M, key => key == PageKey.Annual(1) ? annual : null, _ => Array.Empty<string>());

        fallback.ObjectiveTitle(1).Should().Be("Grow revenue 20%", "the row sits at its sheet rectangle times the placed scale, below the sheet origin");
        fallback.ObjectiveTitle(2).Should().Be("Hire two PMs");
    }

    /// <summary>A sheet rectangle as it sits on a sheet placed at <paramref name="scale"/> (the fixture adds the sheet origin).</summary>
    private static RectPt Scaled(RectPt r, double scale) => new(r.X * scale, r.Y * scale, r.W * scale, r.H * scale);
}
