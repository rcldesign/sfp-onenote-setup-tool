using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Propagation;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>refresh-11: cuts between text elements, and the diff comparing texts as the page reads them back.</summary>
public class TextRoundTripTests
{
    private const string Pizza = "\U0001F355";
    private static readonly DateOnly Day = new(2027, 1, 5);
    private static readonly PageKey DayPage = PageKey.Day(Day);
    private static readonly PlacementTargets Targets = new(d => PageKey.Day(d), d => PageKey.WeekOverview(2), d => PageKey.Calendar(d.Year, d.Month));

    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private static bool HasLoneSurrogate(string text) =>
        text.Where((c, i) => char.IsHighSurrogate(c) ? i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]) : char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(text[i - 1]))).Any();

    [Fact]
    public void Prefix_lengths_end_between_text_elements_longest_first()
    {
        TextCut.PrefixLengths("ab" + Pizza + "c").Should().Equal(4, 2, 1);
        TextCut.PrefixLengths("éx").Should().Equal(2);
        TextCut.PrefixLengths("a").Should().BeEmpty();
        TextCut.PrefixLengths("").Should().BeEmpty();
    }

    [Fact]
    public void A_legend_entry_is_never_cut_inside_an_emoji()
    {
        // At 10 pt the estimate is 5.5 pt a character: 49.6 pt holds nine, "O1 ", five UTF-16 characters and the ellipsis,
        // so a cut by index would keep the emoji's first half.
        string fitted = LegendText.Fit("O1 ", "AAAA" + Pizza + "BB", 49.6, 10, Mono);

        fitted.Should().Be("O1 AAAA…");
        HasLoneSurrogate(fitted).Should().BeFalse();
    }

    [Fact]
    public void The_all_day_band_is_never_cut_inside_an_emoji()
    {
        Sfp.Core.Manifests.BandSlot band = TestManifest.Load().Slots.AllDayBand;
        int keep = (int)Math.Floor((band.MaxWidthPt ?? band.Rect.W) / (band.SizePt * 0.55)) - 1 - 1;
        DateTime start = Day.ToDateTime(TimeOnly.MinValue);
        CalendarEvent holiday = new("h@1", new string('A', keep) + Pizza + " observed", start, start.AddDays(1), true, false, false, "Holidays", null);

        string text = new PlacementEngine(TestManifest.Load(), TimeBlockWindow.Default, Mono).Place(Day, Day, new[] { holiday }, Targets)
            .Single(s => s.Page == DayPage && s.SlotId == CalendarSlots.AllDayBand).Text;

        text.Should().Be(new string('A', keep) + "…");
        HasLoneSurrogate(text).Should().BeFalse();
    }

    [Theory]
    [InlineData("9:00 Q1 planning", "9:00 Q1 planning")]
    [InlineData("O1 Ship  O2 Hire", "O1 Ship O2 Hire")]
    [InlineData(" 9:00 Staff\n12:00 Lunch ", "9:00 Staff\n12:00 Lunch")]
    public void A_desired_text_that_reads_back_as_the_page_text_is_not_written_again(string desired, string page) =>
        Diff(desired, page).Should().BeEmpty();

    [Fact]
    public void A_lone_surrogate_reads_back_as_the_replacement_character()
    {
        Diff("9:00 AAAA\uD83C…", "9:00 AAAA�…").Should().BeEmpty();
    }

    private static IReadOnlyList<PlannedChange> Diff(string desired, string page)
    {
        CalendarSlotText slot = new(DayPage, "timeBlockEvent:9", Day, desired);
        Dictionary<SlotRef, string> previous = new() { [slot.Ref] = page };
        return CalendarDiff.Diff(Day, 14, null, previous, new[] { slot }).Changes;
    }

    [Fact]
    public void A_real_change_is_still_written()
    {
        Diff("9:00 Q1 review", "9:00 Q1 planning").Should().ContainSingle().Which.Kind.Should().Be(ChangeKind.Update);
    }

    [Fact]
    public void Comparable_reads_text_as_the_page_gives_it_back()
    {
        CalendarDiff.Comparable("Q1 planning").Should().Be("Q1 planning");
        CalendarDiff.Comparable("A \t B\nC").Should().Be("A B C");
        CalendarDiff.Comparable("x\uD83C").Should().Be("x�");
        CalendarDiff.Comparable("\uDF55x").Should().Be("�x");
        CalendarDiff.Comparable("x" + Pizza).Should().Be("x" + Pizza, "a whole surrogate pair is kept");
    }

    [Fact]
    public void During_an_outage_a_line_that_differs_only_by_a_no_break_space_is_one_line()
    {
        DateTime nine = Day.ToDateTime(new TimeOnly(9, 0));
        CalendarEvent planning = new("p@1", "Q1 planning", nine, nine.AddMinutes(30), false, false, false, "Work", null);
        string row = CalendarSlots.TimeBlockEvent(9);
        Dictionary<SlotRef, string> keep = new() { [new SlotRef(DayPage, row)] = "9:00 Q1 planning" };

        string text = new PlacementEngine(TestManifest.Load(), TimeBlockWindow.Default, Mono).Place(Day, Day, new[] { planning }, Targets, keep)
            .Single(s => s.Page == DayPage && s.SlotId == row).Text;

        text.Should().Be("9:00 Q1 planning", "the page's line is the event's line, so nothing is added or doubled");
    }
}
