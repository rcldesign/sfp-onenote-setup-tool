using Sfp.Core.Plan;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 3 refresh-7: an event title holding a character XML 1.0 does not allow (a vertical tab left by copy and paste)
/// is written with a space where the character was instead of failing the run every day, a title already on the page
/// compares equal, and a text that still holds such a character refuses its page before any call, naming the slot and
/// the character.
/// </summary>
public class InvalidXmlCharacterTests
{
    [Fact]
    public void A_vertical_tab_in_an_event_title_is_written_as_a_space_and_the_next_run_changes_nothing()
    {
        FixedCalendarFactory calendars = new() { Events = new[] { FixedCalendarFactory.Event("Staff\u000Bmeeting", RunHarness.Jan4, 9) } };
        RunHarness h = new(calendars: calendars);
        h.Type(h.Setup.CalendarSources[0], "Work | https://example.com/work.ics");

        RefreshResult first = h.Run(RunHarness.Request(calendar: true));

        first.ExitCode.Should().Be(0, first.Error);
        first.Summary!.EventsWritten.Should().BeGreaterThan(0);
        List<string> texts = h.Notebook.Read(RunHarness.MonJan4).Outlines.Select(o => o.PlainText).ToList();
        texts.Should().Contain(t => t.Contains("Staff meeting", StringComparison.Ordinal));
        texts.Should().NotContain(t => t.Contains('\u000B'));

        RefreshResult second = h.Run(RunHarness.Request(calendar: true));

        second.ExitCode.Should().Be(0, second.Error);
        second.Summary!.Changes.Should().BeEmpty("the title on the page compares equal to the source's");
    }

    [Fact]
    public void Characters_xml_cannot_carry_become_spaces_where_events_enter_and_compare_as_spaces()
    {
        CalendarEvent e = new("u", "A\u0001B\u000BC￾D￿E\uD800F\tG\nH\rI\U0001F355", new DateTime(2027, 1, 4, 9, 0, 0), new DateTime(2027, 1, 4, 10, 0, 0), false, false, false, "Work", null);

        e.Title.Should().Be("A B C D E F\tG\nH\rI\U0001F355", "tab, line feed, carriage return and a surrogate pair are kept");
        (e with { Title = "x\u000Cy" }).Title.Should().Be("x y");
        CalendarDiff.Comparable("Staff\u000Bmeeting").Should().Be("Staff meeting");
        CalendarDiff.Comparable("a￾b￿").Should().Be("a b");
        CalendarDiff.Comparable("x\uD83C").Should().Be("x�", "a lone surrogate still compares as the U+FFFD the writer puts on the page");
    }

    [Fact]
    public void A_text_the_page_cannot_carry_refuses_the_page_before_any_call_naming_the_slot_and_the_character()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey monday = RunHarness.MonJan4;
        string pageId = nb.PageIds[monday];
        int updates = nb.App.UpdateCount(pageId);
        AnchorWrite write = new(
            new PlannedChange(monday, "standingGoal:1", null, "Walk\u000130 min", ChangeKind.Create, false, null),
            nb.Catalog.Slot(nb.Plan.Page(monday), "standingGoal:1"),
            null);

        Action act = () => new AnchorWriter(nb.App, nb.Options, new CapturingLog().Logger).Write(pageId, nb.Read(monday), new[] { write });

        act.Should().Throw<UnwritableTextException>().Which.Message.Should().Contain("standingGoal:1").And.Contain("U+0001");
        nb.App.UpdateCount(pageId).Should().Be(updates, "nothing is written to the page");
        nb.App.CallCount("DeletePageContent").Should().Be(0);
    }
}
