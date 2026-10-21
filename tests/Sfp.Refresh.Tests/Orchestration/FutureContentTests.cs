using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 3 refresh-2: content the 2013 API can only hand back as one:FutureObject (made in a later OneNote, on the iPad
/// say) or an Outlook task flag, inside a tool outline or on the page over a slot, is never deleted or written over.
/// </summary>
public class FutureContentTests
{
    private static readonly PageKey TueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));

    [Fact]
    public void Clearing_a_standing_goal_leaves_the_pre_fill_holding_a_future_object_and_reports_it()
    {
        RunHarness h = new();
        h.Type(h.Setup.StandingGoals[0], "Walk 30 min");
        h.Run().Success.Should().BeTrue();
        string mon = h.Saved().Pages[RunHarness.MonJan4.Value].Anchors["standingGoal:1"];
        h.Notebook.AddInsideOutline(RunHarness.MonJan4, mon, one => new XElement(one + "FutureObject", new XAttribute("objectID", "{FUTURE-1}{1}{B0}")));
        string setupGoal = h.Notebook.Read(PageKey.Setup).Outlines.Single(o => o.PlainText == "Walk 30 min").ObjectId;
        h.App.DeletePageContent(h.Notebook.PageIds[PageKey.Setup], setupGoal, DateTime.MinValue);
        int deletes = h.App.CallCount("DeletePageContent");

        RefreshResult cleared = h.Run();

        cleared.Success.Should().BeTrue(cleared.Error);
        cleared.Summary!.Changes.Single(c => c.Page == RunHarness.MonJan4 && c.SlotId == "standingGoal:1")
            .Should().Match<PlannedChange>(c => c.Kind == ChangeKind.Remove && c.Blocked && c.Reason == Propagator.EditedReason);
        h.App.Calls.Should().NotContain(c => c.Call == "DeletePageContent" && c.Detail.EndsWith(" " + mon, StringComparison.Ordinal));
        h.Notebook.Read(RunHarness.MonJan4).Outlines.Single(o => o.ObjectId == mon).Element.Descendants().Should().Contain(e => e.Name.LocalName == "FutureObject");
        cleared.Summary.Changes.Count(c => c.SlotId == "standingGoal:1" && c.Kind == ChangeKind.Remove && !c.Blocked).Should().Be(89, "the other pre-fills hold the tool's text alone and go");
        (h.App.CallCount("DeletePageContent") - deletes).Should().Be(89, "one delete per pre-fill that goes, none for the one holding the future object");
    }

    [Fact]
    public void A_cancelled_meeting_leaves_its_line_flagged_as_an_outlook_task_and_reports_it()
    {
        FixedCalendarFactory calendars = new();
        RunHarness h = new(calendars: calendars);
        h.Type(h.Setup.CalendarSources[0], "Work | https://example.com/cal.ics");
        calendars.Events = new[] { FixedCalendarFactory.Event("Staff meeting", new DateOnly(2027, 1, 5), 9) };
        h.Run(RunHarness.Request(calendar: true)).Success.Should().BeTrue();
        string line = h.Saved().Pages[TueJan5.Value].Anchors["timeBlockEvent:9"];
        FlagAsOutlookTask(h, TueJan5, line);
        calendars.Events = Array.Empty<CalendarEvent>();

        RefreshResult cancelled = h.Run(RunHarness.Request(calendar: true));

        cancelled.Success.Should().BeTrue(cancelled.Error);
        cancelled.Summary!.Changes.Single(c => c.Page == TueJan5 && c.SlotId == "timeBlockEvent:9")
            .Should().Match<PlannedChange>(c => c.Blocked && c.Reason == Propagator.EditedReason);
        h.App.Calls.Should().NotContain(c => c.Call == "DeletePageContent" && c.Detail.EndsWith(" " + line, StringComparison.Ordinal));
        OutlineInfo kept = h.Notebook.Read(TueJan5).Outlines.Single(o => o.ObjectId == line);
        kept.PlainText.Should().Be("9:00 Staff meeting");
        kept.Element.Descendants().Should().Contain(e => e.Name.LocalName == "OutlookTask");
    }

    [Fact]
    public void A_future_object_on_the_page_over_the_legend_band_blocks_the_slot()
    {
        RunHarness h = new();
        RectPt legend = h.Notebook.Manifest.Slots.ObjectiveLegend.Rect;
        string future = AddPageFutureObject(h, RunHarness.MonJan4, new RectPt(legend.X + 20, legend.Y - 4, 300, 30));
        h.Type(h.Setup.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.Changes.Single(c => c.Page == RunHarness.MonJan4 && c.SlotId == "objectiveLegend")
            .Should().Match<PlannedChange>(c => c.Blocked && c.Reason == "A user object overlaps the slot: an object.");
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the connector").Should().BeEmpty("nothing is written over it");
        h.Notebook.Read(RunHarness.MonJan4).PageObjects.Should().ContainSingle().Which.Should().Match<PageObjectInfo>(o => o.ObjectId == future && o.Kind == "FutureObject");
        h.OutlinesWithText(TueJan5, "O1 Ship the connector").Should().ContainSingle("a page without it over the band gets its legend");
    }

    /// <summary>Flags a line as an Outlook task the way OneNote stores the flag: a one:OutlookTask first in the paragraph.</summary>
    private static void FlagAsOutlookTask(RunHarness h, PageKey key, string outlineId)
    {
        XElement root = h.Notebook.Read(key).Document.Root!;
        XNamespace one = root.Name.Namespace;
        XElement paragraph = root.Elements(one + "Outline").Single(o => (string?)o.Attribute("objectID") == outlineId).Descendants(one + "OE").First();
        paragraph.AddFirst(new XElement(
            one + "OutlookTask",
            new XAttribute("startDate", "2027-01-05T09:00:00.000Z"),
            new XAttribute("dueDate", "2027-01-05T17:00:00.000Z"),
            new XAttribute("guidTask", "{6E1B7B57-2C3A-4E0B-9F3D-1A2B3C4D5E6F}"),
            new XAttribute("completed", "false")));
        h.App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
    }

    /// <summary>Places a one:FutureObject on the page outside any outline at a sheet rectangle and returns its object ID.</summary>
    private static string AddPageFutureObject(RunHarness h, PageKey key, RectPt sheetRect)
    {
        const string id = "{FUTURE-2}{1}{B0}";
        PageXml page = h.Notebook.Read(key);
        XElement root = page.Document.Root!;
        XNamespace one = root.Name.Namespace;
        root.Add(new XElement(
            one + "FutureObject",
            new XAttribute("objectID", id),
            OneNoteXml.Position(one, sheetRect.X + (page.SheetOutline?.X ?? 0), sheetRect.Y + (page.SheetOutline?.Y ?? 0), 3),
            OneNoteXml.Size(one, sheetRect.W, sheetRect.H, true)));
        h.App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
        return id;
    }
}
