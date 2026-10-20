using System.Globalization;
using Sfp.Refresh;

namespace Sfp.App.ViewModels.Refresh;

/// <summary>
/// Turns Setup values into the lines of the run summary and works out which planned changes carry an inked value.
/// A Setup slot id in <see cref="SetupValues.InkedSlots"/> is "{kind}:{n}" with kind objective, standingGoal, metricName
/// or habit and n 1-based; a change is marked inked when its new text holds the text of such a slot.
/// </summary>
public static class SetupValueText
{
    public const string InkedMark = " (inked)";

    /// <summary>"Objective 1: Ship the connector (inked)", "Standing goal 2: ...", "Metric 3: ...", "Habit 1: ...", "Source: Work (icsUrl)", "Time blocks: 6 to 22".</summary>
    public static IReadOnlyList<string> Lines(SetupValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        HashSet<string> inked = new(values.InkedSlots, StringComparer.OrdinalIgnoreCase);
        List<string> lines = new();
        AddGroup(lines, "Objective", "objective", values.Objectives, inked);
        AddGroup(lines, "Standing goal", "standingGoal", values.StandingGoals, inked);
        AddGroup(lines, "Metric", "metricName", values.MetricNames, inked);
        AddGroup(lines, "Habit", "habit", values.Habits, inked);
        foreach (CalendarSource source in values.Sources)
        {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "Source: {0} ({1})", source.Label, KindName(source.Kind)));
        }

        if (values.TimeBlockWindow is { } window)
        {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "Time blocks: {0} to {1}", window.Start, window.End));
        }

        if (lines.Count == 0)
        {
            lines.Add("Nothing is filled in on the Setup page yet.");
        }

        return lines;
    }

    /// <summary>The texts of the Setup slots that came from ink, for marking changes.</summary>
    public static IReadOnlySet<string> InkedTexts(SetupValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        HashSet<string> texts = new(StringComparer.Ordinal);
        foreach (string id in values.InkedSlots)
        {
            string? text = SlotText(values, id);
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text.Trim());
            }
        }

        return texts;
    }

    /// <summary>True when the change writes text that holds an inked Setup value.</summary>
    public static bool IsInked(PlannedChange change, IReadOnlySet<string> inkedTexts)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(inkedTexts);
        if (inkedTexts.Count == 0 || string.IsNullOrEmpty(change.NewText))
        {
            return false;
        }

        return inkedTexts.Any(t => change.NewText.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>The value of a Setup slot by id ("objective:3"), or null when the id is unknown or the slot is empty.</summary>
    public static string? SlotText(SetupValues values, string slotId)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(slotId);
        int colon = slotId.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || !int.TryParse(slotId[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
        {
            return null;
        }

        string kind = slotId[..colon];
        IReadOnlyList<string?>? list = kind.ToUpperInvariant() switch
        {
            "OBJECTIVE" or "OBJECTIVES" => values.Objectives,
            "STANDINGGOAL" or "STANDINGGOALS" => values.StandingGoals,
            "METRICNAME" or "METRICNAMES" or "METRIC" => values.MetricNames,
            "HABIT" or "HABITS" => values.Habits,
            _ => null,
        };
        return list is not null && n <= list.Count ? list[n - 1] : null;
    }

    private static void AddGroup(List<string> lines, string label, string kind, IReadOnlyList<string?> slots, HashSet<string> inked)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            string? text = slots[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            string id = kind + ":" + (i + 1).ToString(CultureInfo.InvariantCulture);
            string mark = inked.Contains(id) ? InkedMark : "";
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2}{3}", label, i + 1, text.Trim(), mark));
        }
    }

    private static string KindName(CalendarSourceKind kind) => kind switch
    {
        CalendarSourceKind.IcsUrl => "ics link",
        CalendarSourceKind.IcsFile => "ics file",
        CalendarSourceKind.Outlook => "Outlook",
        CalendarSourceKind.Holidays => "holidays",
        _ => kind.ToString(),
    };
}
