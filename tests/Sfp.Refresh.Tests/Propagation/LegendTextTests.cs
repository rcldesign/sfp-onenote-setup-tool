using Sfp.Core.Manifests;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Propagation;

/// <summary>M4.4: the objective legend and the habit legend texts.</summary>
public class LegendTextTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;

    /// <summary>Monospace estimate: 0.55 em per character.</summary>
    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    [Fact]
    public void Objective_legend_lists_the_filled_slots_in_order_with_their_numbers()
    {
        string?[] objectives = { "Ship the connector", null, "Hire two PMs", null, null, null, null, "Write the book" };

        LegendText.ObjectiveLegend(objectives, M.Slots.ObjectiveLegend, Mono).Should().Be("O1 Ship the connector  O3 Hire two PMs  O8 Write the book");
    }

    [Fact]
    public void Objective_legend_is_empty_when_no_objective_is_set()
    {
        LegendText.ObjectiveLegend(new string?[8], M.Slots.ObjectiveLegend, Mono).Should().BeEmpty();
    }

    [Fact]
    public void Objective_legend_cuts_the_long_entries_first_so_short_titles_survive_whole()
    {
        string?[] objectives =
        {
            "Short",
            new string('x', 120),
            "Also short",
            new string('y', 120),
            null, null, null, null,
        };

        string legend = LegendText.ObjectiveLegend(objectives, M.Slots.ObjectiveLegend, Mono);

        double width = M.Slots.ObjectiveLegend.MaxWidthPt ?? M.Slots.ObjectiveLegend.Rect.W;
        Mono(legend, M.Slots.ObjectiveLegend.SizePt).Should().BeLessThanOrEqualTo(width);
        string[] entries = legend.Split("  ");
        entries.Should().HaveCount(4);
        entries[0].Should().Be("O1 Short");
        entries[2].Should().Be("O3 Also short");
        entries[1].Should().StartWith("O2 xxx").And.EndWith(LegendText.Ellipsis);
        entries[3].Should().StartWith("O4 yyy").And.EndWith(LegendText.Ellipsis);
        entries[1].Length.Should().Be(entries[3].Length, "the two long entries share the remaining width equally");
    }

    [Fact]
    public void Habit_legend_numbers_the_filled_slots_and_wraps_at_the_column_count()
    {
        string?[] habits = new string?[16];
        for (int i = 0; i < 10; i++)
        {
            habits[i] = "H" + (i + 1);
        }

        habits[15] = "Last";

        string legend = LegendText.HabitLegend(habits, M.Slots.HabitLegend, Mono);

        string[] lines = legend.Split('\n');
        lines.Should().HaveCount(2, "11 entries at 8 per line");
        lines[0].Should().Be("1 H1  2 H2  3 H3  4 H4  5 H5  6 H6  7 H7  8 H8");
        lines[1].Should().Be("9 H9  10 H10  16 Last");
    }

    [Fact]
    public void Habit_legend_cuts_an_entry_to_its_column_width_and_is_empty_without_habits()
    {
        string?[] habits = new string?[16];
        habits[0] = new string('z', 80);

        string legend = LegendText.HabitLegend(habits, M.Slots.HabitLegend, Mono);

        double column = (M.Slots.HabitLegend.Rect.W / M.Slots.HabitLegend.Columns) - Mono(LegendText.HabitSeparator, M.Slots.HabitLegend.SizePt);
        legend.Should().StartWith("1 zzz").And.EndWith(LegendText.Ellipsis);
        Mono(legend, M.Slots.HabitLegend.SizePt).Should().BeLessThanOrEqualTo(column);
        LegendText.HabitLegend(new string?[16], M.Slots.HabitLegend, Mono).Should().BeEmpty();
    }

    [Fact]
    public void Fit_keeps_the_prefix_and_falls_back_to_the_ellipsis_alone()
    {
        LegendText.Fit("O1 ", "Hello", 100, 8, Mono).Should().Be("O1 Hello");
        LegendText.Fit("O1 ", "Hello world", 30, 8, Mono).Should().Be("O1 He" + LegendText.Ellipsis, "six characters of 4.4 pt fit in 30 pt");
        LegendText.Fit("O1 ", "Hello", 1, 8, Mono).Should().Be("O1 " + LegendText.Ellipsis);
    }
}
