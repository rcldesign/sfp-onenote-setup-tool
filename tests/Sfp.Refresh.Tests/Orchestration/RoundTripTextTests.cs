using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// refresh-11: text that changes on its way through the page XML is written once, not on every run: a title cut to fit
/// is cut between text elements (never inside an emoji, which the page would show as U+FFFD), and a desired text is
/// compared with the page's as the page reads it back (a no-break space reads back as a space).
/// </summary>
public class RoundTripTextTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
    private static readonly PageKey TueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));
    private const string Pizza = "\U0001F355";

    private static RefreshRun WithEvent(string title)
    {
        DateTime nine = new(2027, 1, 5, 9, 0, 0);
        CalendarEvent e = new("e1", title, nine, nine.AddHours(1), false, false, false, "Work", null);
        RefreshRun h = new(new LabelledSources().Add("Work", () => new[] { e }));
        h.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");
        return h;
    }

    [Fact]
    public void An_event_title_cut_inside_an_emoji_is_written_once()
    {
        // The Time Blocks column fits 41 characters of the monospace estimate: "9:00 " plus 35 title characters plus the
        // ellipsis, so a cut by UTF-16 index would end between the two halves of the emoji.
        RefreshRun h = WithEvent(new string('A', 34) + Pizza + " party planning with the whole team");

        h.Run().Summary!.EventsWritten.Should().Be(2, "the 9 row and the overview column");
        h.SlotText(TueJan5, "timeBlockEvent:9").Should().Be("9:00 " + new string('A', 34) + "…", "the cut falls before the emoji, never inside it");

        h.Run().Summary!.Changes.Should().BeEmpty("the page holds what the tool wanted");
    }

    [Fact]
    public void An_event_title_with_a_no_break_space_is_written_once()
    {
        RefreshRun h = WithEvent("Q1 planning");

        h.Run().Summary!.EventsWritten.Should().Be(2);
        h.SlotText(TueJan5, "timeBlockEvent:9").Should().Be("9:00 Q1 planning", "the page reads the no-break space back as a space");

        h.Run().Summary!.Changes.Should().BeEmpty("the page text reads the same as the desired text");
    }

    [Fact]
    public void An_objective_cut_inside_an_emoji_is_written_to_the_legends_once()
    {
        // The legend band fits floor(width / 4.4) characters: "O1 ", the cut title and the ellipsis. The emoji starts
        // one character before the last one a UTF-16 cut would keep.
        BandSlot band = RefreshManifest.Shipped.Slots.ObjectiveLegend;
        int kept = (int)Math.Floor((band.MaxWidthPt ?? band.Rect.W) / (band.SizePt * 0.55)) - 4 - 1;
        RefreshRun h = new();
        h.Type(S.Objectives[0], new string('A', kept) + Pizza + " launch");

        h.Run(calendar: false).Summary!.ValuesWritten.Should().BeGreaterThan(100, "every overview and daily page has a legend");
        h.SlotText(MonJan4, "objectiveLegend").Should().Be("O1 " + new string('A', kept) + "…");

        h.Run(calendar: false).Summary!.Changes.Should().BeEmpty("no legend is written again");
    }
}
