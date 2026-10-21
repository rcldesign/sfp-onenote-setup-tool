using System.Globalization;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 3 refresh-7: an event title holding a character XML 1.0 does not allow (a vertical tab left by copy and paste)
/// is written with a space where the character was instead of failing the run every day, a title already on the page
/// compares equal, and a text that still holds such a character refuses its page before any call, naming the slot and
/// the character. The run skips that page, reports why, and writes the other pages.
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

    [Fact]
    public void A_page_whose_text_still_holds_such_a_character_is_skipped_and_reported_and_the_other_pages_are_written()
    {
        RunHarness h = new();
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        h.Type(h.Setup.StandingGoals[0], "Walk 30 min");
        PropagatorFactory real = h.Services.Propagators;
        h.Services = h.Services with { Propagators = (catalog, reader) => new OneSlotText(real(catalog, reader), RunHarness.MonJan4, "standingGoal:1", "Walk\u000B30 min") };
        string mondayId = h.Notebook.PageIds[RunHarness.MonJan4];
        int mondayUpdates = h.App.UpdateCount(mondayId);
        string reason = string.Format(CultureInfo.InvariantCulture, AnchorWriter.UnwritableTextFormat, "standingGoal:1", 0x0B);

        RefreshResult result = h.Run();

        result.ExitCode.Should().Be(0, result.Error);
        result.Success.Should().BeTrue();
        RefreshSummary summary = result.Summary!;
        summary.Changes.Where(c => c.Page == RunHarness.MonJan4).Should().HaveCount(2, "the objective legend and the standing goal")
            .And.OnlyContain(c => c.Blocked && c.Reason == reason, "the whole page is skipped, its clean change too, with the writer's reason");
        summary.Warnings.Should().Contain("Page Mon Jan 4 was not written: " + reason);
        h.App.UpdateCount(mondayId).Should().Be(mondayUpdates, "nothing is written to the skipped page");
        h.Saved().Pages[RunHarness.MonJan4.Value].Anchors.Should().NotContainKeys("objectiveLegend", "standingGoal:1");

        List<PlannedChange> others = summary.Changes.Where(c => c.Page != RunHarness.MonJan4).ToList();
        others.Should().NotBeEmpty().And.OnlyContain(c => !c.Blocked, "the run goes on with the other pages");
        summary.ValuesWritten.Should().Be(others.Count);
        h.Notebook.Read(PageKey.Day(RunHarness.Jan4.AddDays(1))).Outlines.Select(o => o.PlainText).Should().Contain(new[] { "O1 Ship the connector", "Walk 30 min" });

        h.Services = h.Services with { Propagators = real };
        RefreshResult next = h.Run();

        next.ExitCode.Should().Be(0, next.Error);
        next.Summary!.Changes.Should().HaveCount(2).And.OnlyContain(c => c.Page == RunHarness.MonJan4 && !c.Blocked, "the next run writes the skipped page and nothing else");
        h.Notebook.Read(RunHarness.MonJan4).Outlines.Select(o => o.PlainText).Should().Contain(new[] { "O1 Ship the connector", "Walk 30 min" });
    }

    /// <summary>
    /// Plans with the real propagator, then gives one slot's change another text: a text no normalization saw, as a value
    /// that reached the writer some other way would be.
    /// </summary>
    private sealed class OneSlotText : IPropagator
    {
        private readonly IPropagator inner;
        private readonly PageKey page;
        private readonly string slotId;
        private readonly string text;

        public OneSlotText(IPropagator inner, PageKey page, string slotId, string text)
        {
            this.inner = inner;
            this.page = page;
            this.slotId = slotId;
            this.text = text;
        }

        public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s) => Swap(inner.Plan(plan, v, s));

        public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s, bool writeInkedValues) => Swap(inner.Plan(plan, v, s, writeInkedValues));

        private IReadOnlyList<PlannedChange> Swap(IReadOnlyList<PlannedChange> changes) =>
            changes.Select(c => c.Page == page && c.SlotId == slotId ? c with { NewText = text } : c).ToList();
    }
}
