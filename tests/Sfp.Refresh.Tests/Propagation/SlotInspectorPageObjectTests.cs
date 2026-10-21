using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Propagation;

/// <summary>Round 2 refresh-5: a picture, attached file or media file placed on the page over a slot is a user object.</summary>
public class SlotInspectorPageObjectTests
{
    private const string PageId = "{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{1}{E19512345678901234567890123456789012345678}";

    private static readonly Manifest M = RefreshManifest.Shipped;

    [Theory]
    [InlineData("Image", "a picture")]
    [InlineData("InsertedFile", "an attached file")]
    [InlineData("MediaFile", "a media file")]
    public void A_page_level_object_over_the_legend_band_blocks_the_slot(string kind, string described)
    {
        RectPt legend = M.Slots.ObjectiveLegend.Rect;
        PageXmlFixture fixture = new PageXmlFixture(PageId, "day:2027-01-04", "Mon Jan 4")
            .Placed(new RectPt(legend.X + 20, legend.Y - 4, 300, 30), kind);
        PageXml page = fixture.Build();

        SlotState state = SlotInspector.Inspect(page, legend, null, Array.Empty<string>(), M.PageWidthPt);

        state.IsBlocked.Should().BeTrue();
        state.Intruders.Should().ContainSingle().Which.Should().Be(new Intruder(fixture.LastObjectId, described));
        Propagator.Decide(PageKey.Day(new DateOnly(2027, 1, 4)), "objectiveLegend", "O1 Ship the connector", state)!.Reason
            .Should().Be("A user object overlaps the slot: " + described + ".");
    }

    [Fact]
    public void A_page_level_picture_elsewhere_on_the_page_does_not_block_the_slot()
    {
        RectPt legend = M.Slots.ObjectiveLegend.Rect;
        PageXml page = new PageXmlFixture(PageId, "day:2027-01-04", "Mon Jan 4")
            .Placed(new RectPt(legend.X, legend.Bottom + 200, 300, 30))
            .Build();

        SlotInspector.Inspect(page, legend, null, Array.Empty<string>(), M.PageWidthPt).IsBlocked.Should().BeFalse();
    }
}
