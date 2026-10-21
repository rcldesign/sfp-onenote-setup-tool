using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// refresh-6: during a partial calendar outage a slot that a live source changes keeps the dead source's lines; the run
/// only adds, says so truthfully, and changes nothing more while the outage lasts or once the source is back.
/// </summary>
public class CalendarOutageTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly PageKey TueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));
    private static readonly PageKey Week02 = PageKey.WeekOverview(2);

    private static CalendarEvent Event(string uid, string title, int hour, string label)
    {
        DateTime start = new(2027, 1, 5, hour, 0, 0);
        return new CalendarEvent(uid, title, start, start.AddHours(1), false, false, false, label, null);
    }

    [Fact]
    public void A_dead_sources_line_stays_in_a_slot_the_live_source_adds_to()
    {
        bool workDown = false;
        IReadOnlyList<CalendarEvent> work = new[] { Event("w1", "Staff meeting", 9, "Work") };
        IReadOnlyList<CalendarEvent> personal = new[] { Event("p1", "Lunch with Ana", 12, "Personal") };
        LabelledSources calendars = new LabelledSources()
            .Add("Work", () => workDown ? throw new TimeoutException("The request timed out after 30 s.") : work)
            .Add("Personal", () => personal);
        RefreshRun h = new(calendars);
        h.Type(S.CalendarSources[0], "Work | https://example.com/work.ics");
        h.Type(S.CalendarSources[1], "Personal | https://example.com/me.ics");
        h.Run().Summary!.EventsWritten.Should().Be(3, "the 9 and 12 rows and the overview column");
        h.SlotText(Week02, "overviewDay:1").Should().Be("12:00 Lunch with Ana\n09:00 Staff meeting");

        workDown = true;
        personal = new[] { Event("p1", "Lunch with Ana", 12, "Personal"), Event("p2", "Dentist", 15, "Personal") };
        RefreshResult outage = h.Run();

        outage.ExitCode.Should().Be(0, outage.Error);
        outage.Summary!.FailedSources.Should().Equal("Work: The request timed out after 30 s.");
        outage.Summary.Warnings.Should().Contain("Some calendar sources failed; every event line already on the pages was kept, and new events were added only where a slot had room beside them.");
        outage.Summary.Changes.Select(c => (c.Page.Value, c.SlotId, c.Kind, c.Blocked, c.NewText)).Should().BeEquivalentTo(new[]
        {
            (TueJan5.Value, "timeBlockEvent:15", ChangeKind.Create, false, "15:00 Dentist"),
            (Week02.Value, "overviewDay:1", ChangeKind.Update, false, "15:00 Dentist\n12:00 Lunch with Ana\n09:00 Staff meeting"),
        });
        h.SlotText(Week02, "overviewDay:1").Should().Be("15:00 Dentist\n12:00 Lunch with Ana\n09:00 Staff meeting", "the dead source's meeting stays and the dentist is added");
        h.SlotText(TueJan5, "timeBlockEvent:9").Should().Be("9:00 Staff meeting");

        h.Run().Summary!.Changes.Should().BeEmpty("nothing changed while the outage lasts");
        workDown = false;
        h.Run().Summary!.Changes.Should().BeEmpty("with Work back, the column already holds every event");
    }
}
