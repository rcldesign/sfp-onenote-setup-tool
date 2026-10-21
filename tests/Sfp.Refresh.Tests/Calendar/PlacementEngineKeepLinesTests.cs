using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>
/// refresh-6: when a calendar source failed, <see cref="PlacementEngine.Place"/> is given the texts the slots hold now and
/// keeps every one of their lines: the lines the events no longer give are merged back in the slot's order, the new lines
/// only fill the room the line cap leaves, and a full slot or an all-day band that would lose an entry stays as it is.
/// </summary>
public class PlacementEngineKeepLinesTests
{
    /// <summary>Tue Jan 5, 2027.</summary>
    private static readonly DateOnly Day = new(2027, 1, 5);
    private static readonly PageKey DayPage = PageKey.Day(Day);
    private static readonly PageKey Overview = PageKey.WeekOverview(2);
    private static readonly PageKey Calendar = PageKey.Calendar(2027, 1);
    private static readonly string OverviewSlot = CalendarSlots.OverviewDay(1);
    private static readonly string CellSlot = CalendarSlots.CalendarEvents(Day);

    private static readonly PlacementTargets Targets = new(
        d => PageKey.Day(d),
        d => Overview,
        d => PageKey.Calendar(d.Year, d.Month));

    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private static CalendarEvent Timed(string title, int hour, int minute = 0)
    {
        DateTime start = Day.ToDateTime(new TimeOnly(hour, minute));
        return new CalendarEvent(title + "@" + start.ToString("s", System.Globalization.CultureInfo.InvariantCulture), title, start, start.AddMinutes(30), false, false, false, "Personal", null);
    }

    private static CalendarEvent AllDay(string title)
    {
        DateTime start = Day.ToDateTime(TimeOnly.MinValue);
        return new CalendarEvent(title + "@" + Day.ToString("O"), title, start, start.AddDays(1), true, false, false, "Personal", null);
    }

    /// <summary>The text placed in one slot, keeping <paramref name="old"/> in it when given.</summary>
    private static string Place(PageKey page, string slotId, string? old, params CalendarEvent[] events)
    {
        Dictionary<SlotRef, string>? keep = old is null ? null : new() { [new SlotRef(page, slotId)] = old };
        PlacementEngine engine = new(TestManifest.Load(), TimeBlockWindow.Default, Mono);
        return engine.Place(Day, Day, events, Targets, keep).Single(s => s.Page == page && s.SlotId == slotId).Text;
    }

    [Fact]
    public void A_live_sources_new_event_joins_the_dead_sources_line_in_the_overview_order()
    {
        CalendarEvent lunch = Timed("Lunch with Ana", 12);
        CalendarEvent dentist = Timed("Dentist", 15);

        Place(Overview, OverviewSlot, null, lunch, dentist).Should().Be("15:00 Dentist\n12:00 Lunch with Ana", "without old lines to keep, the staff meeting of the dead source is gone");
        Place(Overview, OverviewSlot, "12:00 Lunch with Ana\n09:00 Staff meeting", lunch, dentist)
            .Should().Be("15:00 Dentist\n12:00 Lunch with Ana\n09:00 Staff meeting", "the old line stays, in the column's newest-first order");
    }

    [Fact]
    public void Placing_again_over_the_merged_text_changes_nothing()
    {
        CalendarEvent[] live = { Timed("Lunch with Ana", 12), Timed("Dentist", 15), AllDay("Team offsite") };
        string first = Place(Overview, OverviewSlot, "Company holiday\n12:00 Lunch with Ana\n09:00 Staff meeting\n07:30 Gym", live);

        first.Should().Be("Company holiday\nTeam offsite\n15:00 Dentist\n12:00 Lunch with Ana\n09:00 Staff meeting\n07:30 Gym", "all-day titles first, then timed lines newest first");
        Place(Overview, OverviewSlot, first, live).Should().Be(first);
    }

    [Fact]
    public void In_a_time_block_row_the_old_lines_keep_their_places_and_the_new_ones_wait_behind_a_more_line()
    {
        // The default window has 16 rows, so a row holds three lines.
        string row = CalendarSlots.TimeBlockEvent(9);

        Place(DayPage, row, "9:00 Standup\n9:20 Review", Timed("Call", 9, 10), Timed("Sync", 9, 30))
            .Should().Be("9:00 Standup\n9:20 Review\n+2 more", "both old lines stay visible; the two new ones are counted");
        Place(DayPage, row, "9:00 Standup\n9:20 Review", Timed("Call", 9, 10))
            .Should().Be("9:00 Standup\n9:10 Call\n9:20 Review", "a new line that fits goes in time order");
    }

    [Fact]
    public void A_full_slot_stays_as_it_is()
    {
        string row = CalendarSlots.TimeBlockEvent(9);

        Place(DayPage, row, "9:00 Standup\n9:10 Call\n9:20 Review", Timed("Sync", 9, 5)).Should().Be("9:00 Standup\n9:10 Call\n9:20 Review", "three old lines fill the row");
        Place(DayPage, row, "9:00 Standup\n9:10 Call\n+2 more", Timed("Sync", 9, 5)).Should().Be("9:00 Standup\n9:10 Call\n+2 more", "the events behind the count are unknown");
    }

    [Fact]
    public void A_continuation_line_stays_after_the_rows_start_times()
    {
        string row = CalendarSlots.TimeBlockEvent(10);

        Place(DayPage, row, "... Workshop", Timed("Call", 10, 30)).Should().Be("10:30 Call\n... Workshop");
    }

    [Fact]
    public void The_all_day_band_takes_a_new_entry_only_when_it_keeps_the_old_ones()
    {
        string separator = TestManifest.Load().Slots.AllDayBand.Separator;

        Place(DayPage, CalendarSlots.AllDayBand, "Company holiday", AllDay("Team offsite"))
            .Should().Be("Company holiday", "the band is one line, so the new entry waits rather than replace the old one");
        Place(DayPage, CalendarSlots.AllDayBand, "Company holiday", AllDay("Company holiday"), AllDay("Team offsite"))
            .Should().Be("Company holiday" + separator + "Team offsite");
    }

    [Fact]
    public void A_monthly_cell_keeps_the_old_title_beside_the_new_one()
    {
        Place(Calendar, CellSlot, "Company holiday", AllDay("Team offsite")).Should().Be("Company holiday\nTeam offsite");
    }

    [Fact]
    public void Slots_without_old_text_are_placed_as_usual()
    {
        Dictionary<SlotRef, string> keep = new() { [new SlotRef(Overview, OverviewSlot)] = "09:00 Staff meeting" };
        PlacementEngine engine = new(TestManifest.Load(), TimeBlockWindow.Default, Mono);

        IReadOnlyList<CalendarSlotText> kept = engine.Place(Day, Day, new[] { Timed("Dentist", 15) }, Targets, keep);
        IReadOnlyList<CalendarSlotText> plain = engine.Place(Day, Day, new[] { Timed("Dentist", 15) }, Targets);

        kept.Where(s => s.SlotId != OverviewSlot).Should().Equal(plain.Where(s => s.SlotId != OverviewSlot));
        kept.Single(s => s.SlotId == OverviewSlot).Text.Should().Be("15:00 Dentist\n09:00 Staff meeting");
    }
}
