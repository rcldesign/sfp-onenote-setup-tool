using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

public class PlacementEngineTests
{
    /// <summary>Mon Jan 4, 2027, the first full day of Week 02 in Q1 2027.</summary>
    private static readonly DateOnly Day = new(2027, 1, 4);
    private static readonly PageKey DayPage = PageKey.Day(Day);
    private static readonly PageKey Overview = PageKey.WeekOverview(2);
    private static readonly PageKey Calendar = PageKey.Calendar(2027, 1);

    private static readonly PlacementTargets Targets = new(
        d => PageKey.Day(d),
        d => Overview,
        d => PageKey.Calendar(d.Year, d.Month));

    /// <summary>Monospace estimate: 0.55 em per character.</summary>
    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private static PlacementEngine Engine(TimeBlockWindow? window = null, PlacementOptions? options = null) =>
        new(TestManifest.Load(), window ?? TimeBlockWindow.Default, Mono, options);

    private static CalendarEvent Timed(string title, int startHour, int startMinute, int endHour, int endMinute, DateOnly? date = null)
    {
        DateOnly d = date ?? Day;
        DateTime start = new(d.Year, d.Month, d.Day, startHour, startMinute, 0);
        DateTime end = new(d.Year, d.Month, d.Day, endHour, endMinute, 0);
        return new CalendarEvent(title.ToUpperInvariant() + "@" + start.ToString("s", System.Globalization.CultureInfo.InvariantCulture), title, start, end, false, false, false, "Work", null);
    }

    private static CalendarEvent AllDay(string title, DateOnly? date = null, int days = 1)
    {
        DateOnly d = date ?? Day;
        DateTime start = d.ToDateTime(TimeOnly.MinValue);
        return new CalendarEvent(title.ToUpperInvariant() + "@" + d.ToString("O"), title, start, start.AddDays(days), true, false, false, "Holidays", null);
    }

    /// <summary>The four synthetic events of the card: a one-hour meeting, a three-hour workshop, an early run before the window and an all-day holiday.</summary>
    private static IReadOnlyList<CalendarEvent> FourEvents() => new[]
    {
        Timed("Staff meeting", 9, 0, 10, 0),
        Timed("Workshop", 13, 0, 16, 0),
        Timed("Early run", 5, 30, 6, 15),
        AllDay("Company holiday"),
    };

    private static string TextOf(IReadOnlyList<CalendarSlotText> slots, PageKey page, string slotId) =>
        slots.Single(s => s.Page == page && s.SlotId == slotId).Text;

    [Fact]
    public void Synthetic_day_with_four_events_fills_every_slot_as_the_placement_table_says()
    {
        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, FourEvents(), Targets);

        slots.Should().HaveCount(16 + 1 + 1 + 1, "16 hour rows, the all-day band, one overview column and one calendar cell");
        slots.Should().AllSatisfy(s => s.Date.Should().Be(Day));

        TextOf(slots, DayPage, "timeBlockEvent:6").Should().Be("5:30 Early run", "events outside the window are listed in the first row with their time");
        TextOf(slots, DayPage, "timeBlockEvent:7").Should().BeEmpty();
        TextOf(slots, DayPage, "timeBlockEvent:8").Should().BeEmpty();
        TextOf(slots, DayPage, "timeBlockEvent:9").Should().Be("9:00 Staff meeting");
        TextOf(slots, DayPage, "timeBlockEvent:10").Should().BeEmpty("a one-hour event has no continuation");
        TextOf(slots, DayPage, "timeBlockEvent:11").Should().BeEmpty();
        TextOf(slots, DayPage, "timeBlockEvent:12").Should().BeEmpty();
        TextOf(slots, DayPage, "timeBlockEvent:13").Should().Be("13:00 Workshop");
        TextOf(slots, DayPage, "timeBlockEvent:14").Should().Be("... Workshop");
        TextOf(slots, DayPage, "timeBlockEvent:15").Should().Be("... Workshop");
        TextOf(slots, DayPage, "timeBlockEvent:16").Should().BeEmpty("the workshop ends at 16:00");
        foreach (int hour in Enumerable.Range(17, 5))
        {
            TextOf(slots, DayPage, CalendarSlots.TimeBlockEvent(hour)).Should().BeEmpty();
        }

        TextOf(slots, DayPage, CalendarSlots.AllDayBand).Should().Be("Company holiday");
        TextOf(slots, Overview, "overviewDay:0").Should().Be("Company holiday\n13:00 Workshop\n09:00 Staff meeting\n05:30 Early run", "all-day first, then newest first with zero-padded times");
        TextOf(slots, Calendar, "calendarEvents:2027-01-04").Should().Be("Company holiday", "monthly cells list all-day events and holidays only");
    }

    [Fact]
    public void Slot_ids_follow_the_documented_forms()
    {
        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, FourEvents(), Targets);

        slots.Where(s => s.Page == DayPage).Select(s => s.SlotId).Should().Equal(
            Enumerable.Range(6, 16).Select(CalendarSlots.TimeBlockEvent).Append("allDayBand"));
        slots.Single(s => s.Page == Overview).SlotId.Should().Be("overviewDay:0");
        slots.Single(s => s.Page == Calendar).SlotId.Should().Be("calendarEvents:2027-01-04");
    }

    [Fact]
    public void Two_events_in_one_hour_are_two_lines_with_primaries_before_continuations()
    {
        CalendarEvent[] events = { Timed("Planning", 9, 0, 11, 0), Timed("Call with Ann", 10, 15, 10, 45) };

        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, events, Targets);

        TextOf(slots, DayPage, "timeBlockEvent:9").Should().Be("9:00 Planning");
        TextOf(slots, DayPage, "timeBlockEvent:10").Should().Be("10:15 Call with Ann\n... Planning");
        TextOf(slots, DayPage, "timeBlockEvent:11").Should().BeEmpty();
    }

    [Fact]
    public void An_event_ending_part_way_through_an_hour_still_covers_that_hour()
    {
        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, new[] { Timed("Review", 9, 30, 11, 30) }, Targets);

        TextOf(slots, DayPage, "timeBlockEvent:9").Should().Be("9:30 Review");
        TextOf(slots, DayPage, "timeBlockEvent:10").Should().Be("... Review");
        TextOf(slots, DayPage, "timeBlockEvent:11").Should().Be("... Review");
        TextOf(slots, DayPage, "timeBlockEvent:12").Should().BeEmpty();
    }

    [Fact]
    public void Events_after_the_window_end_are_listed_in_the_first_row_without_continuations()
    {
        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, new[] { Timed("Late call", 22, 30, 23, 30) }, Targets);

        TextOf(slots, DayPage, "timeBlockEvent:6").Should().Be("22:30 Late call");
        slots.Where(s => s.Page == DayPage && s.SlotId != "timeBlockEvent:6").Should().AllSatisfy(s => s.Text.Should().BeEmpty());
    }

    [Fact]
    public void Continuations_stop_at_the_window_end()
    {
        IReadOnlyList<CalendarSlotText> slots = Engine(new TimeBlockWindow(6, 10)).Place(Day, Day, new[] { Timed("Offsite", 8, 0, 12, 0) }, Targets);

        TextOf(slots, DayPage, "timeBlockEvent:8").Should().Be("8:00 Offsite");
        TextOf(slots, DayPage, "timeBlockEvent:9").Should().Be("... Offsite");
        slots.Where(s => s.Page == DayPage).Should().HaveCount(4 + 1);
    }

    [Fact]
    public void Long_title_is_cut_to_the_time_block_column_width_with_an_ellipsis()
    {
        Manifest m = TestManifest.Load();
        double width = m.Slots.TimeBlocks.ColumnRight - m.Slots.TimeBlocks.EventTextX;
        string title = "Quarterly planning session with the whole leadership team and guests";
        Mono("9:00 " + title, m.Slots.TimeBlocks.EventSizePt).Should().BeGreaterThan(width, "the title must need cutting for the test to mean anything");

        string line = TextOf(Engine().Place(Day, Day, new[] { Timed(title, 9, 0, 10, 0) }, Targets), DayPage, "timeBlockEvent:9");

        line.Should().StartWith("9:00 Quarterly").And.EndWith("…");
        line.Length.Should().BeLessThan(("9:00 " + title).Length);
        Mono(line, m.Slots.TimeBlocks.EventSizePt).Should().BeLessThanOrEqualTo(width);
        Mono(line[..^1] + "x" + "…", m.Slots.TimeBlocks.EventSizePt).Should().BeGreaterThan(width, "the cut keeps as much of the title as fits");
    }

    [Fact]
    public void Overview_column_holds_at_most_maxLines_and_ends_with_a_more_line()
    {
        int maxLines = TestManifest.Load().Slots.OverviewDayColumns.MaxLines;
        List<CalendarEvent> events = Enumerable.Range(0, maxLines + 2)
            .Select(i => Timed("Slot " + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), 6 + (i % 16), 0, 7 + (i % 16), 0))
            .ToList();

        string[] lines = TextOf(Engine().Place(Day, Day, events, Targets), Overview, "overviewDay:0").Split('\n');

        lines.Should().HaveCount(maxLines);
        lines[^1].Should().Be("+3 more");
    }

    [Fact]
    public void Overview_column_shows_all_events_when_they_fit_exactly()
    {
        int maxLines = TestManifest.Load().Slots.OverviewDayColumns.MaxLines;
        List<CalendarEvent> events = Enumerable.Range(0, maxLines)
            .Select(i => Timed("Slot " + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), 6 + (i % 16), i % 60, 7 + (i % 16), 0))
            .ToList();

        string[] lines = TextOf(Engine().Place(Day, Day, events, Targets), Overview, "overviewDay:0").Split('\n');

        lines.Should().HaveCount(maxLines);
        lines.Should().NotContain(l => l.EndsWith("more", StringComparison.Ordinal));
    }

    [Fact]
    public void Overview_can_be_ordered_oldest_first()
    {
        PlacementOptions chronological = new() { OverviewNewestFirst = false };

        string text = TextOf(Engine(options: chronological).Place(Day, Day, FourEvents(), Targets), Overview, "overviewDay:0");

        text.Should().Be("Company holiday\n05:30 Early run\n09:00 Staff meeting\n13:00 Workshop");
    }

    [Fact]
    public void Monthly_cell_caps_all_day_lines_at_maxLines_with_a_more_line()
    {
        CalendarEvent[] three = { AllDay("Alpha"), AllDay("Beta"), AllDay("Gamma") };
        CalendarEvent[] two = { AllDay("Alpha"), AllDay("Beta") };

        TextOf(Engine().Place(Day, Day, three, Targets), Calendar, "calendarEvents:2027-01-04").Should().Be("Alpha\n+2 more");
        TextOf(Engine().Place(Day, Day, two, Targets), Calendar, "calendarEvents:2027-01-04").Should().Be("Alpha\nBeta");
    }

    [Fact]
    public void Monthly_cell_ignores_timed_events()
    {
        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, new[] { Timed("Staff meeting", 9, 0, 10, 0) }, Targets);

        TextOf(slots, Calendar, "calendarEvents:2027-01-04").Should().BeEmpty();
        TextOf(slots, Overview, "overviewDay:0").Should().Be("09:00 Staff meeting");
    }

    [Fact]
    public void All_day_band_joins_several_entries_with_the_manifest_separator()
    {
        string separator = TestManifest.Load().Slots.AllDayBand.Separator;

        string band = TextOf(Engine().Place(Day, Day, new[] { AllDay("Holiday"), AllDay("Birthday") }, Targets), DayPage, "allDayBand");

        band.Should().Be("Birthday" + separator + "Holiday", "all-day entries are ordered by title");
    }

    [Fact]
    public void Cancelled_events_are_left_out_everywhere()
    {
        CalendarEvent cancelled = Timed("Staff meeting", 9, 0, 10, 0) with { Cancelled = true };

        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, new[] { cancelled, AllDay("Holiday") with { Cancelled = true } }, Targets);

        slots.Should().AllSatisfy(s => s.Text.Should().BeEmpty());
    }

    [Fact]
    public void Multi_day_all_day_event_appears_on_each_day_it_covers_and_timed_events_only_on_their_start_day()
    {
        DateOnly tue = Day.AddDays(1);
        DateOnly wed = Day.AddDays(2);
        CalendarEvent[] events = { AllDay("Offsite", Day, days: 2), Timed("Night shift", 23, 0, 23, 59) };

        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, wed, events, Targets);

        TextOf(slots, DayPage, "allDayBand").Should().Be("Offsite");
        TextOf(slots, PageKey.Day(tue), "allDayBand").Should().Be("Offsite");
        TextOf(slots, PageKey.Day(wed), "allDayBand").Should().BeEmpty();
        TextOf(slots, Overview, "overviewDay:0").Should().Be("Offsite\n23:00 Night shift");
        TextOf(slots, Overview, "overviewDay:1").Should().Be("Offsite");
        TextOf(slots, Overview, "overviewDay:2").Should().BeEmpty();
        TextOf(slots, Calendar, "calendarEvents:2027-01-05").Should().Be("Offsite");
    }

    [Fact]
    public void Days_without_a_daily_page_still_get_overview_and_calendar_slots()
    {
        PlacementTargets noDays = Targets with { DayPage = _ => null };

        IReadOnlyList<CalendarSlotText> slots = Engine().Place(Day, Day, FourEvents(), noDays);

        slots.Select(s => s.Page).Should().Equal(Overview, Calendar);
    }

    [Fact]
    public void PlaceChanges_returns_only_the_non_empty_slots_as_Create_changes()
    {
        IReadOnlyList<PlannedChange> changes = Engine().PlaceChanges(Day, Day, FourEvents(), Targets);

        changes.Select(c => c.SlotId).Should().Equal(
            "timeBlockEvent:6", "timeBlockEvent:9", "timeBlockEvent:13", "timeBlockEvent:14", "timeBlockEvent:15",
            "allDayBand", "overviewDay:0", "calendarEvents:2027-01-04");
        changes.Should().AllSatisfy(c =>
        {
            c.Kind.Should().Be(ChangeKind.Create);
            c.OldText.Should().BeNull();
            c.Blocked.Should().BeFalse();
            c.NewText.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void An_invalid_window_is_rejected_at_construction()
    {
        Action act = () => new PlacementEngine(TestManifest.Load(), new TimeBlockWindow(10, 6), Mono);

        act.Should().Throw<ArgumentException>().WithMessage("*10 to 6*");
    }

    [Fact]
    public void Targets_from_a_plan_resolve_day_overview_and_calendar_pages_by_key_and_date()
    {
        Dictionary<string, string> noTokens = new();
        Quarter q = new(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31), 14, "Q1 2027");
        PageSpec overview = new(Overview, PageType.WeeklyOverview, "Weekly Overview", 1, 16, Day, noTokens);
        PageSpec monday = new(DayPage, PageType.Day, "Mon Jan 4", 2, 17, Day, noTokens);
        PageSpec calendar = new(Calendar, PageType.Calendar, "January 2027", 1, 13, new DateOnly(2027, 1, 1), noTokens);
        NotebookPlan plan = new("SFP Q1 2027", q, new[]
        {
            new SectionSpec("Calendar", null, new[] { calendar }),
            new SectionSpec("Week 02 (Jan 4 to Jan 10)", null, new[] { overview, monday }),
        }, Array.Empty<LinkSpec>());

        PlacementTargets targets = PlacementTargets.FromPlan(plan);

        targets.DayPage(Day).Should().Be(DayPage);
        targets.DayPage(Day.AddDays(1)).Should().BeNull("Tue Jan 5 is not in this tiny plan");
        targets.OverviewPage(Day.AddDays(6)).Should().Be(Overview, "Sun Jan 10 is in week 02");
        targets.OverviewPage(Day.AddDays(7)).Should().BeNull();
        targets.CalendarPage(new DateOnly(2027, 1, 20)).Should().Be(Calendar);
        targets.CalendarPage(new DateOnly(2027, 2, 1)).Should().BeNull();
    }
}
