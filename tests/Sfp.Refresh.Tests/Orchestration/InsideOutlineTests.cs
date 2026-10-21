using System.Xml.Linq;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Propagation;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>Round 2 refresh-3: a picture or ink the user put inside a tool outline survives a clear or a cancellation.</summary>
public class InsideOutlineTests
{
    private static readonly PageKey TueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));

    [Fact]
    public void Clearing_a_standing_goal_leaves_the_pre_fills_holding_a_picture_or_ink_and_reports_them()
    {
        RunHarness h = new();
        h.Type(h.Setup.StandingGoals[0], "Walk 30 min");
        h.Run().Success.Should().BeTrue();
        Sidecar written = h.Saved();
        string mon = written.Pages[RunHarness.MonJan4.Value].Anchors["standingGoal:1"];
        string tue = written.Pages[TueJan5.Value].Anchors["standingGoal:1"];
        h.Notebook.AddInsideOutline(RunHarness.MonJan4, mon, ToolShapeTests.Picture);
        h.Notebook.AddInsideOutline(TueJan5, tue, one => new XElement(one + "InkWord", new XAttribute("recognizedText", "done")));
        string setupGoal = h.Notebook.Read(PageKey.Setup).Outlines.Single(o => o.PlainText == "Walk 30 min").ObjectId;
        h.App.DeletePageContent(h.Notebook.PageIds[PageKey.Setup], setupGoal, DateTime.MinValue);

        RefreshResult cleared = h.Run();

        cleared.Success.Should().BeTrue(cleared.Error);
        foreach (PageKey day in new[] { RunHarness.MonJan4, TueJan5 })
        {
            cleared.Summary!.Changes.Single(c => c.Page == day && c.SlotId == "standingGoal:1")
                .Should().Match<PlannedChange>(c => c.Kind == ChangeKind.Remove && c.Blocked && c.Reason == Propagator.EditedReason);
        }

        h.App.Calls.Should().NotContain(c => c.Call == "DeletePageContent" && (c.Detail.EndsWith(" " + mon, StringComparison.Ordinal) || c.Detail.EndsWith(" " + tue, StringComparison.Ordinal)));
        h.Notebook.Read(RunHarness.MonJan4).Outlines.Single(o => o.ObjectId == mon).Element.Descendants().Should().Contain(e => e.Name.LocalName == "Image");
        h.Notebook.Read(TueJan5).Outlines.Single(o => o.ObjectId == tue).InkText.Should().Be("done");
        cleared.Summary!.Changes.Count(c => c.SlotId == "standingGoal:1" && c.Kind == ChangeKind.Remove && !c.Blocked).Should().Be(88, "the other pre-fills hold the tool's text alone and go");
    }

    [Fact]
    public void A_cancelled_meeting_leaves_its_line_holding_ink_and_reports_it()
    {
        FixedCalendarFactory calendars = new();
        RunHarness h = new(calendars: calendars);
        h.Type(h.Setup.CalendarSources[0], "Work | https://example.com/cal.ics");
        calendars.Events = new[] { FixedCalendarFactory.Event("Staff meeting", new DateOnly(2027, 1, 5), 9) };
        h.Run(RunHarness.Request(calendar: true)).Success.Should().BeTrue();
        string line = h.Saved().Pages[TueJan5.Value].Anchors["timeBlockEvent:9"];
        h.Notebook.AddInsideOutline(TueJan5, line, one => new XElement(one + "InkWord", new XAttribute("recognizedText", "moved to 10")));
        calendars.Events = Array.Empty<CalendarEvent>();

        RefreshResult cancelled = h.Run(RunHarness.Request(calendar: true));

        cancelled.Success.Should().BeTrue(cancelled.Error);
        cancelled.Summary!.Changes.Single(c => c.Page == TueJan5 && c.SlotId == "timeBlockEvent:9")
            .Should().Match<PlannedChange>(c => c.Blocked && c.Reason == Propagator.EditedReason);
        h.App.Calls.Should().NotContain(c => c.Call == "DeletePageContent" && c.Detail.EndsWith(" " + line, StringComparison.Ordinal));
        h.Notebook.Read(TueJan5).Outlines.Single(o => o.ObjectId == line).InkText.Should().Be("moved to 10");
    }
}
