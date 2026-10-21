using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>Round 2 refresh-5: Refresh never writes on top of a picture the user placed on the page over a slot.</summary>
public class PagePictureTests
{
    [Fact]
    public void A_picture_pasted_onto_the_page_over_the_legend_band_and_a_time_block_row_blocks_both_slots()
    {
        FixedCalendarFactory calendars = new();
        RunHarness h = new(calendars: calendars);
        Manifest m = h.Notebook.Manifest;
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        h.Type(h.Setup.CalendarSources[0], "Work | https://example.com/cal.ics");
        calendars.Events = new[] { FixedCalendarFactory.Event("Staff meeting", RunHarness.Jan4, 9) };
        RectPt legend = m.Slots.ObjectiveLegend.Rect;
        string overLegend = h.Notebook.AddPagePicture(RunHarness.MonJan4, new RectPt(legend.X + 20, legend.Y - 4, 300, 30));
        h.Notebook.AddPagePicture(RunHarness.MonJan4, new RectPt(m.Slots.TimeBlocks.EventTextX, 150, 180, 200));

        RefreshResult result = h.Run(RunHarness.Request(calendar: true));

        result.Success.Should().BeTrue(result.Error);
        List<PlannedChange> monday = result.Summary!.Changes.Where(c => c.Page == RunHarness.MonJan4).ToList();
        monday.Single(c => c.SlotId == "objectiveLegend").Should().Match<PlannedChange>(c => c.Blocked && c.Reason == "A user object overlaps the slot: a picture.");
        monday.Single(c => c.SlotId == "timeBlockEvent:9").Should().Match<PlannedChange>(c => c.Blocked && c.Reason == "A user object overlaps the slot: a picture.");
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the connector").Should().BeEmpty("nothing is written over the picture");
        h.OutlinesWithText(RunHarness.MonJan4, "9:00 Staff meeting").Should().BeEmpty();
        h.Notebook.Read(RunHarness.MonJan4).PageObjects.Should().HaveCount(2).And.Contain(o => o.ObjectId == overLegend);
        h.OutlinesWithText(PageKey.Day(new DateOnly(2027, 1, 5)), "O1 Ship the connector").Should().ContainSingle("a page without a picture over the band gets its legend");
        result.Summary.Skipped.Should().Be(2);
    }
}
