using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

public class CalendarDiffTests
{
    /// <summary>Day D: Wed Jan 6, 2027 (week 02 of Q1 2027).</summary>
    private static readonly DateOnly D = new(2027, 1, 6);
    private static readonly PageKey Overview = PageKey.WeekOverview(2);
    private static readonly IReadOnlyDictionary<SlotRef, string> Nothing = new Dictionary<SlotRef, string>();

    private static readonly PlacementTargets DaysOnly = new(d => PageKey.Day(d), _ => null, _ => null);
    private static readonly PlacementTargets DaysAndOverview = new(d => PageKey.Day(d), _ => Overview, _ => null);

    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private static PlacementEngine Engine() => new(TestManifest.Load(), TimeBlockWindow.Default, Mono);

    private static CalendarEvent Standup(DateOnly day, string title = "Standup", bool cancelled = false)
    {
        DateTime start = day.ToDateTime(new TimeOnly(9, 0));
        return new CalendarEvent("standup@" + day.ToString("O"), title, start, start.AddMinutes(30), false, false, cancelled, "Work", null);
    }

    private static SlotRef Nine(DateOnly day) => new(PageKey.Day(day), CalendarSlots.TimeBlockEvent(9));

    [Fact]
    public void Run_on_D_then_D_plus_1_with_one_cancelled_event_yields_exactly_one_Remove_and_no_writes_to_D_minus_1()
    {
        PlacementEngine engine = Engine();

        // Day D: the standup exists on D - 1, D and D + 1. Horizon 1 writes D and D + 1.
        CalendarEvent[] run1Events = { Standup(D.AddDays(-1)), Standup(D), Standup(D.AddDays(1)) };
        CalendarDiffResult run1 = CalendarDiff.Diff(D, 1, null, Nothing, engine.Place(D.AddDays(-1), D.AddDays(2), run1Events, DaysOnly));

        run1.Changes.Should().HaveCount(2).And.AllSatisfy(c => c.Kind.Should().Be(ChangeKind.Create));
        run1.Changes.Select(c => c.Page).Should().Equal(PageKey.Day(D), PageKey.Day(D.AddDays(1)));
        run1.FrozenThrough.Should().Be(D.AddDays(-1));
        IReadOnlyDictionary<SlotRef, string> written = CalendarDiff.Apply(Nothing, run1.Changes);

        // Day D + 1: the source changed yesterday's and today's titles (must be ignored) and cancelled tomorrow's standup.
        CalendarEvent[] run2Events =
        {
            Standup(D.AddDays(-1), "Standup (moved)"),
            Standup(D, "Standup (moved)"),
            Standup(D.AddDays(1), cancelled: true),
        };
        CalendarDiffResult run2 = CalendarDiff.Diff(D.AddDays(1), 1, run1.FrozenThrough, written, engine.Place(D.AddDays(-1), D.AddDays(3), run2Events, DaysOnly));

        PlannedChange only = run2.Changes.Should().ContainSingle().Subject;
        only.Kind.Should().Be(ChangeKind.Remove);
        only.Page.Should().Be(PageKey.Day(D.AddDays(1)));
        only.SlotId.Should().Be("timeBlockEvent:9");
        only.OldText.Should().Be("9:00 Standup");
        only.NewText.Should().BeEmpty();
        run2.Changes.Should().NotContain(c => c.Page == PageKey.Day(D.AddDays(-1)), "D - 1 is before today");
        run2.Changes.Should().NotContain(c => c.Page == PageKey.Day(D), "D is before today on the second run");
        run2.FrozenThrough.Should().Be(D);
    }

    [Fact]
    public void A_cancelled_event_also_empties_its_overview_column_line()
    {
        PlacementEngine engine = Engine();
        IReadOnlyDictionary<SlotRef, string> written = CalendarDiff.Apply(
            Nothing,
            CalendarDiff.Diff(D, 1, null, Nothing, engine.Place(D, D.AddDays(1), new[] { Standup(D), Standup(D.AddDays(1)) }, DaysAndOverview)).Changes);

        CalendarDiffResult run2 = CalendarDiff.Diff(D.AddDays(1), 1, D.AddDays(-1), written,
            engine.Place(D, D.AddDays(2), new[] { Standup(D), Standup(D.AddDays(1), cancelled: true) }, DaysAndOverview));

        run2.Changes.Select(c => (c.Page, c.SlotId, c.Kind)).Should().BeEquivalentTo(new[]
        {
            (PageKey.Day(D.AddDays(1)), "timeBlockEvent:9", ChangeKind.Remove),
            (Overview, "overviewDay:3", ChangeKind.Remove),
        });
    }

    [Fact]
    public void Unchanged_text_yields_no_change_so_a_second_run_is_empty()
    {
        PlacementEngine engine = Engine();
        IReadOnlyList<CalendarSlotText> desired = engine.Place(D, D.AddDays(3), new[] { Standup(D), Standup(D.AddDays(2)) }, DaysAndOverview);
        IReadOnlyDictionary<SlotRef, string> written = CalendarDiff.Apply(Nothing, CalendarDiff.Diff(D, 3, null, Nothing, desired).Changes);

        CalendarDiffResult again = CalendarDiff.Diff(D, 3, D.AddDays(-1), written, desired);

        again.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Changed_text_yields_Update_with_the_old_text_recorded()
    {
        IReadOnlyDictionary<SlotRef, string> previous = new Dictionary<SlotRef, string> { [Nine(D)] = "9:00 Standup" };

        CalendarDiffResult result = CalendarDiff.Diff(D, 0, null, previous, Engine().Place(D, D, new[] { Standup(D, "Standup, room 4") }, DaysOnly));

        PlannedChange change = result.Changes.Should().ContainSingle().Subject;
        change.Kind.Should().Be(ChangeKind.Update);
        change.OldText.Should().Be("9:00 Standup");
        change.NewText.Should().Be("9:00 Standup, room 4");
    }

    [Fact]
    public void Days_past_the_horizon_are_not_written()
    {
        CalendarEvent[] events = { Standup(D), Standup(D.AddDays(5)) };

        CalendarDiffResult result = CalendarDiff.Diff(D, 2, null, Nothing, Engine().Place(D, D.AddDays(5), events, DaysOnly));

        result.Changes.Should().ContainSingle().Which.Page.Should().Be(PageKey.Day(D));
        result.LastWritableDay.Should().Be(D.AddDays(2));
    }

    [Fact]
    public void A_frozen_through_later_than_yesterday_is_kept_so_a_clock_going_backwards_never_unfreezes_a_day()
    {
        DateOnly frozenAhead = D.AddDays(3);

        CalendarDiffResult result = CalendarDiff.Diff(D, 7, frozenAhead, Nothing, Engine().Place(D, D.AddDays(7), new[] { Standup(D.AddDays(2)), Standup(D.AddDays(5)) }, DaysOnly));

        result.FrozenThrough.Should().Be(frozenAhead);
        result.FirstWritableDay.Should().Be(frozenAhead.AddDays(1));
        result.Changes.Should().ContainSingle().Which.Page.Should().Be(PageKey.Day(D.AddDays(5)));
    }

    [Fact]
    public void A_slot_placed_twice_is_a_programming_error()
    {
        CalendarSlotText slot = new(PageKey.Day(D), "allDayBand", D, "x");

        Action act = () => CalendarDiff.Diff(D, 1, null, Nothing, new[] { slot, slot });

        act.Should().Throw<ArgumentException>().WithMessage("*placed twice*");
    }

    [Fact]
    public void Apply_skips_blocked_changes_and_removes_emptied_slots()
    {
        IReadOnlyDictionary<SlotRef, string> previous = new Dictionary<SlotRef, string> { [Nine(D)] = "9:00 Standup", [Nine(D.AddDays(1))] = "9:00 Standup" };
        PlannedChange[] changes =
        {
            new(PageKey.Day(D), "timeBlockEvent:9", "9:00 Standup", "", ChangeKind.Remove, false, null),
            new(PageKey.Day(D.AddDays(1)), "timeBlockEvent:9", "9:00 Standup", "9:00 Moved", ChangeKind.Update, true, "user outline in the slot"),
            new(PageKey.Day(D.AddDays(2)), "allDayBand", null, "Holiday", ChangeKind.Create, false, null),
        };

        IReadOnlyDictionary<SlotRef, string> next = CalendarDiff.Apply(previous, changes);

        next.Should().HaveCount(2);
        next.Should().NotContainKey(Nine(D));
        next[Nine(D.AddDays(1))].Should().Be("9:00 Standup", "a blocked change was not written");
        next[new SlotRef(PageKey.Day(D.AddDays(2)), "allDayBand")].Should().Be("Holiday");
    }

    [Fact]
    public void PreviousFrom_builds_the_map_from_page_key_values()
    {
        IReadOnlyDictionary<SlotRef, string> map = CalendarDiff.PreviousFrom(new[] { ("day:2027-01-06", "allDayBand", "Holiday") });

        map[new SlotRef(PageKey.Day(D), "allDayBand")].Should().Be("Holiday");
    }
}
