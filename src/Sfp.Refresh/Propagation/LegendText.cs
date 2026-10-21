using System.Globalization;
using Sfp.Core.Manifests;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Propagation;

/// <summary>
/// The two multi-value texts Refresh composes from Setup: the objective legend ("O1 Ship the connector  O2 Hire two PMs")
/// cut per objective to fit its band, and the habit legend ("1 Run  2 Read ...") wrapped to the band's columns. Widths
/// come from the injected measure so the real font decides what fits; nothing measured lives here.
/// </summary>
public static class LegendText
{
    /// <summary>Appended to a title cut to fit.</summary>
    public const string Ellipsis = "…";

    /// <summary>Separator between habit legend entries.</summary>
    public const string HabitSeparator = "  ";

    /// <summary>
    /// The objective legend in slot order, empty slots skipped but their numbers kept ("O1 ... O3 ..."). When the whole
    /// line is wider than the band, the longest entries are cut first (each entry gets an equal share of the width that
    /// the shorter entries leave over), so a short title is never cut to make room for a long one.
    /// </summary>
    public static string ObjectiveLegend(IReadOnlyList<string?> objectives, BandSlot band, TextWidthMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(measure);
        List<(string Prefix, string Title)> entries = new();
        for (int i = 0; i < objectives.Count; i++)
        {
            string? title = objectives[i];
            if (!string.IsNullOrWhiteSpace(title))
            {
                entries.Add(("O" + (i + 1).ToString(CultureInfo.InvariantCulture) + " ", title.Trim()));
            }
        }

        if (entries.Count == 0)
        {
            return "";
        }

        double width = band.MaxWidthPt ?? band.Rect.W;
        double size = band.SizePt;
        double separators = measure(band.Separator, size) * (entries.Count - 1);
        double available = Math.Max(0, width - separators);
        double[] widths = entries.Select(e => measure(e.Prefix + e.Title, size)).ToArray();
        double budget = widths.Sum() <= available ? double.MaxValue : Budget(widths, available);

        string[] texts = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            texts[i] = widths[i] <= budget ? entries[i].Prefix + entries[i].Title : Fit(entries[i].Prefix, entries[i].Title, budget, size, measure);
        }

        return string.Join(band.Separator, texts);
    }

    /// <summary>
    /// The habit legend: "{n} {habit}" per non-empty slot, <see cref="HabitLegendSlot.Columns"/> entries per line joined
    /// with two spaces, lines joined with a line break, each entry cut to fit its column width.
    /// </summary>
    public static string HabitLegend(IReadOnlyList<string?> habits, HabitLegendSlot slot, TextWidthMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(habits);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(measure);
        int columns = Math.Max(1, slot.Columns);
        double columnWidth = Math.Max(0, (slot.Rect.W / columns) - measure(HabitSeparator, slot.SizePt));
        List<string> entries = new();
        for (int i = 0; i < habits.Count; i++)
        {
            string? habit = habits[i];
            if (!string.IsNullOrWhiteSpace(habit))
            {
                entries.Add(Fit((i + 1).ToString(CultureInfo.InvariantCulture) + " ", habit.Trim(), columnWidth, slot.SizePt, measure));
            }
        }

        if (entries.Count == 0)
        {
            return "";
        }

        IEnumerable<string> lines = entries.Chunk(columns).Select(line => string.Join(HabitSeparator, line));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Prefix plus text, or prefix plus the longest prefix of the text plus the ellipsis that fits; the prefix always
    /// survives, and the text is cut only between text elements (<see cref="TextCut"/>).
    /// </summary>
    public static string Fit(string prefix, string text, double widthPt, double sizePt, TextWidthMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        string full = prefix + text;
        if (measure(full, sizePt) <= widthPt)
        {
            return full;
        }

        foreach (int length in TextCut.PrefixLengths(text))
        {
            string candidate = prefix + text[..length].TrimEnd() + Ellipsis;
            if (measure(candidate, sizePt) <= widthPt)
            {
                return candidate;
            }
        }

        return prefix + Ellipsis;
    }

    /// <summary>The per-entry width budget: entries that fit under an equal share keep their width, the rest share what is left equally.</summary>
    private static double Budget(double[] widths, double available)
    {
        double remaining = available;
        int count = widths.Length;
        double budget = remaining / count;
        foreach (double w in widths.OrderBy(w => w))
        {
            if (w > budget)
            {
                break;
            }

            remaining -= w;
            count--;
            budget = count > 0 ? remaining / count : 0;
        }

        return budget;
    }
}
