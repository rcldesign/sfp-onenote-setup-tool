using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.OneNote;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Propagation;

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
/// Production <see cref="ISetupReader"/>: maps the manifest's Setup slot rectangles (generated.setup) to values. Every
/// outline on the page belongs to one slot: the slot whose rectangle holds the outline's position (sheet coordinates,
/// the sheet outline's origin and placed scale applied), else the slot it overlaps most. An outline of several
/// paragraphs that reaches into the next slot of its list (the user typed one objective per line in one outline) hands
/// its paragraphs to that slot and the ones after it, in order. A slot's value is the plain text it was given, joined
/// and whitespace-collapsed; when no typed text is present the recognized text of the ink words is used and the slot is
/// marked inked. Calendar source lines go through <see cref="CalendarSourceLineParser"/>; the time-block window is set
/// when both boxes hold integers. An optional <see cref="IAnnualFallback"/> fills empty objective slots from the Annual
/// Objectives pages.
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

        SlotReading[][] read = Assign(
            setupPage,
            new[] { spec.Objectives, spec.StandingGoals, spec.MetricNames, spec.Habits, spec.CalendarSources, new[] { spec.TimeBlockStart }, new[] { spec.TimeBlockEnd } },
            m.PageWidthPt);
        string?[] objectives = Values(read[0], SetupSlots.Objective, inked);
        string?[] goals = Values(read[1], SetupSlots.StandingGoal, inked);
        string?[] metrics = Values(read[2], SetupSlots.MetricName, inked);
        string?[] habits = Values(read[3], SetupSlots.Habit, inked);
        string?[] sourceLines = Values(read[4], SetupSlots.CalendarSource, inked);

        List<string> problems = new();
        IReadOnlyList<CalendarSource> sources = CalendarSourceLineParser.ParseAll(sourceLines, problems);

        SlotReading start = read[5][0];
        SlotReading end = read[6][0];
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

    /// <summary>
    /// What every slot of the given lists holds (sheet rectangles; a list of one is a single slot). Each outline on the
    /// page goes to one slot: the one whose rectangle holds the outline's position, else the one it overlaps most (an
    /// outline overlapping none is not a value). An outline of several paragraphs that also reaches into the next slot
    /// of its list hands its paragraphs to that slot and the ones after it in order, the list's last slot taking any
    /// paragraphs left over; otherwise all its text goes to its one slot. In each slot typed text wins over ink.
    /// </summary>
    public static SlotReading[][] Assign(PageXml page, IReadOnlyList<IReadOnlyList<RectPt>> lists, double sheetWidthPt)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(lists);
        RectPt[][] rects = lists.Select(list => list.Select(r => SlotInspector.ToPageRect(page, r, sheetWidthPt)).ToArray()).ToArray();
        List<(string Typed, string Ink)>[][] given = rects.Select(list => list.Select(_ => new List<(string Typed, string Ink)>()).ToArray()).ToArray();
        foreach (OutlineInfo outline in page.Outlines.Where(o => !o.IsBackgroundImage))
        {
            if (Home(outline, rects) is not (int list, int slot))
            {
                continue;
            }

            List<(string Typed, string Ink)> paragraphs = Paragraphs(outline);
            bool reachesNext = slot + 1 < rects[list].Length && outline.Rect.Overlaps(rects[list][slot + 1]);
            if (paragraphs.Count < 2 || !reachesNext)
            {
                given[list][slot].Add((outline.PlainText, outline.InkText));
                continue;
            }

            for (int p = 0; p < paragraphs.Count; p++)
            {
                given[list][Math.Min(slot + p, rects[list].Length - 1)].Add(paragraphs[p]);
            }
        }

        return given.Select(list => list.Select(Reading).ToArray()).ToArray();
    }

    /// <summary>
    /// The text as a page can carry it, every character XML 1.0 does not allow (a C0 control from a numeric character
    /// reference, say) a space as in an event title (<see cref="CalendarDiff.XmlSafe"/>), then whitespace runs (line
    /// breaks included) collapsed to one space and the ends trimmed; null when nothing remains.
    /// </summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string collapsed = Whitespace().Replace(CalendarDiff.XmlSafe(text), " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string?[] Values(SlotReading[] readings, Func<int, string> slotId, List<string> inked)
    {
        string?[] values = new string?[readings.Length];
        for (int i = 0; i < readings.Length; i++)
        {
            values[i] = readings[i].Text;
            MarkInked(inked, slotId(i + 1), readings[i]);
        }

        return values;
    }

    /// <summary>The slot holding the outline's position, else the one it overlaps most, as (list, slot); null when it overlaps none.</summary>
    private static (int List, int Slot)? Home(OutlineInfo outline, RectPt[][] rects)
    {
        for (int list = 0; list < rects.Length; list++)
        {
            for (int slot = 0; slot < rects[list].Length; slot++)
            {
                if (rects[list][slot].Contains(outline.X, outline.Y))
                {
                    return (list, slot);
                }
            }
        }

        (int List, int Slot)? best = null;
        double bestArea = 0;
        for (int list = 0; list < rects.Length; list++)
        {
            for (int slot = 0; slot < rects[list].Length; slot++)
            {
                double area = OverlapArea(outline.Rect, rects[list][slot]);
                if (area > bestArea)
                {
                    best = (list, slot);
                    bestArea = area;
                }
            }
        }

        return best;
    }

    private static double OverlapArea(RectPt a, RectPt b) =>
        Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));

    /// <summary>The outline's paragraphs (one:OE in document order) that hold text: each one's typed runs and the recognized text of its own ink words.</summary>
    private static List<(string Typed, string Ink)> Paragraphs(OutlineInfo outline)
    {
        XNamespace one = outline.Element.Name.Namespace;
        List<(string Typed, string Ink)> paragraphs = new();
        foreach (XElement oe in outline.Element.Descendants(one + "OE"))
        {
            string typed = string.Join(" ", oe.Elements(one + "T").Select(t => PageXmlParser.PlainText(t.Value)).Where(t => t.Length > 0));
            string ink = string.Join(" ", oe.Descendants(one + "InkWord")
                .Where(w => w.Ancestors(one + "OE").First() == oe)
                .Select(w => (string?)w.Attribute("recognizedText") ?? "")
                .Where(t => !string.IsNullOrWhiteSpace(t)));
            if (typed.Length > 0 || ink.Length > 0)
            {
                paragraphs.Add((typed, ink));
            }
        }

        return paragraphs;
    }

    /// <summary>A slot's value from what it was given: typed text wins over ink.</summary>
    private static SlotReading Reading(List<(string Typed, string Ink)> given)
    {
        string? typed = Normalize(string.Join(" ", given.Select(g => g.Typed)));
        if (typed is not null)
        {
            return new SlotReading(typed, false);
        }

        string? ink = Normalize(string.Join(" ", given.Select(g => g.Ink)));
        return ink is null ? SlotReading.Empty : new SlotReading(ink, true);
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
