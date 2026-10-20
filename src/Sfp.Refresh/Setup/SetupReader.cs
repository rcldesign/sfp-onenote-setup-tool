using System.Globalization;
using System.Text.RegularExpressions;
using Sfp.Core.Manifests;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Setup;

/// <summary>Slot ids of the Setup page, as reported in <see cref="SetupValues.InkedSlots"/> and the preview.</summary>
public static class SetupSlots
{
    public const string TimeBlockStart = "timeBlockStart";
    public const string TimeBlockEnd = "timeBlockEnd";

    /// <summary>"objective:3" for the third objective slot (1-based).</summary>
    public static string Objective(int n) => Id("objective", n);

    public static string StandingGoal(int n) => Id("standingGoal", n);

    public static string MetricName(int n) => Id("metricName", n);

    public static string Habit(int n) => Id("habit", n);

    public static string CalendarSource(int n) => Id("calendarSource", n);

    private static string Id(string prefix, int n) => prefix + ":" + n.ToString(CultureInfo.InvariantCulture);
}

/// <summary>What one Setup slot held: the text and whether it came from ink recognition.</summary>
/// <param name="Text">The slot text (whitespace collapsed, trimmed), or null when the slot is empty.</param>
/// <param name="Inked">True when the text is the recognized text of ink words (no typed text was present).</param>
public readonly record struct SlotReading(string? Text, bool Inked)
{
    public static SlotReading Empty { get; } = new(null, false);

    public bool IsEmpty => Text is null;
}

/// <summary>
/// The PRD fallback for objective titles: when a Setup objective slot is empty, the typed text in the matching Annual
/// Objectives header row is used instead. The production implementation reads the annual pages through the run's page
/// reader (<see cref="AnnualPageFallback"/>); tests use a stub.
/// </summary>
public interface IAnnualFallback
{
    /// <summary>The typed text in the Annual Objectives header row of objective <paramref name="n"/> (1-based), or null when there is none.</summary>
    string? ObjectiveTitle(int n);
}

/// <summary>
/// Production <see cref="ISetupReader"/>: maps the manifest's Setup slot rectangles (generated.setup) to values. A slot's
/// value is the plain text of every outline overlapping its rectangle (sheet coordinates, the sheet outline's origin
/// subtracted), joined and trimmed; when no typed text is present the recognized text of the ink words in those
/// outlines is used and the slot is marked inked. Calendar source lines go through <see cref="CalendarSourceLineParser"/>;
/// the time-block window is set when both boxes hold integers. An optional <see cref="IAnnualFallback"/> fills empty
/// objective slots from the Annual Objectives pages.
/// </summary>
public sealed partial class SetupReader : ISetupReader
{
    private readonly IAnnualFallback? fallback;

    /// <param name="fallback">Fills empty objective slots from the Annual Objectives header rows, or null for none.</param>
    public SetupReader(IAnnualFallback? fallback = null)
    {
        this.fallback = fallback;
    }

    /// <inheritdoc />
    public SetupValues Read(PageXml setupPage, Manifest m)
    {
        ArgumentNullException.ThrowIfNull(setupPage);
        ArgumentNullException.ThrowIfNull(m);
        SetupPageSpec spec = m.Generated.Setup;
        List<string> inked = new();

        string?[] objectives = ReadList(setupPage, spec.Objectives, SetupSlots.Objective, inked);
        string?[] goals = ReadList(setupPage, spec.StandingGoals, SetupSlots.StandingGoal, inked);
        string?[] metrics = ReadList(setupPage, spec.MetricNames, SetupSlots.MetricName, inked);
        string?[] habits = ReadList(setupPage, spec.Habits, SetupSlots.Habit, inked);
        string?[] sourceLines = ReadList(setupPage, spec.CalendarSources, SetupSlots.CalendarSource, inked);

        List<string> problems = new();
        IReadOnlyList<CalendarSource> sources = CalendarSourceLineParser.ParseAll(sourceLines, problems);

        SlotReading start = ReadSlot(setupPage, spec.TimeBlockStart);
        SlotReading end = ReadSlot(setupPage, spec.TimeBlockEnd);
        (int, int)? window = null;
        if (TryHour(start.Text, out int startHour) && TryHour(end.Text, out int endHour))
        {
            window = (startHour, endHour);
            MarkInked(inked, SetupSlots.TimeBlockStart, start);
            MarkInked(inked, SetupSlots.TimeBlockEnd, end);
        }

        SetupValues values = new(objectives, goals, metrics, habits, sources, inked.Count > 0)
        {
            TimeBlockWindow = window,
            InkedSlots = inked,
            SourceProblems = problems,
        };
        return fallback is null ? values : ApplyFallback(values, fallback);
    }

    /// <summary>Fills every empty objective slot from the fallback; untouched when the fallback has nothing.</summary>
    public static SetupValues ApplyFallback(SetupValues values, IAnnualFallback fallback)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(fallback);
        string?[] objectives = values.Objectives.ToArray();
        bool changed = false;
        for (int i = 0; i < objectives.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(objectives[i]))
            {
                continue;
            }

            string? title = Normalize(fallback.ObjectiveTitle(i + 1));
            if (title is not null)
            {
                objectives[i] = title;
                changed = true;
            }
        }

        return changed ? values with { Objectives = objectives } : values;
    }

    /// <summary>Reads one slot rectangle (sheet coordinates): typed text wins over ink.</summary>
    public static SlotReading ReadSlot(PageXml page, RectPt sheetRect)
    {
        ArgumentNullException.ThrowIfNull(page);
        List<OutlineInfo> outlines = page.OutlinesIn(sheetRect).ToList();
        string? typed = Normalize(string.Join(" ", outlines.Select(o => o.PlainText)));
        if (typed is not null)
        {
            return new SlotReading(typed, false);
        }

        string? ink = Normalize(string.Join(" ", outlines.Select(o => o.InkText)));
        return ink is null ? SlotReading.Empty : new SlotReading(ink, true);
    }

    /// <summary>Whitespace runs (line breaks included) collapsed to one space and the ends trimmed; null when nothing remains.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string collapsed = Whitespace().Replace(text, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string?[] ReadList(PageXml page, IReadOnlyList<RectPt> rects, Func<int, string> slotId, List<string> inked)
    {
        string?[] values = new string?[rects.Count];
        for (int i = 0; i < rects.Count; i++)
        {
            SlotReading reading = ReadSlot(page, rects[i]);
            values[i] = reading.Text;
            MarkInked(inked, slotId(i + 1), reading);
        }

        return values;
    }

    private static void MarkInked(List<string> inked, string slotId, SlotReading reading)
    {
        if (reading.Inked && !inked.Contains(slotId, StringComparer.Ordinal))
        {
            inked.Add(slotId);
        }
    }

    private static bool TryHour(string? text, out int hour) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
