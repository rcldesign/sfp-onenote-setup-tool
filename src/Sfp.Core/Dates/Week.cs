namespace Sfp.Core.Dates;

/// <summary>One Monday-to-Sunday week of the quarter.</summary>
/// <param name="Number">k, 1-based week number within the quarter.</param>
/// <param name="MondayStart">Week start, the Monday, even when it precedes the quarter.</param>
/// <param name="SundayEnd">Week end, the Sunday, even when it follows the quarter.</param>
/// <param name="QuarterDays">The days of this week that fall inside the quarter, in order (1 to 7 of them).</param>
/// <param name="WeeksRemaining">N - k + 1, what the corrected weeks-remaining strip circles.</param>
public sealed record Week(int Number, DateOnly MondayStart, DateOnly SundayEnd, IReadOnlyList<DateOnly> QuarterDays, int WeeksRemaining)
{
    /// <summary>All seven days of the week, Monday first, including days outside the quarter.</summary>
    public IReadOnlyList<DateOnly> AllDays => Enumerable.Range(0, 7).Select(MondayStart.AddDays).ToList();

    /// <summary>First in-quarter day (used for the section name span).</summary>
    public DateOnly FirstQuarterDay => QuarterDays[0];

    /// <summary>Last in-quarter day (used for the section name span).</summary>
    public DateOnly LastQuarterDay => QuarterDays[^1];

    /// <summary>True when every day of the week is inside the quarter.</summary>
    public bool IsFull => QuarterDays.Count == 7;
}
