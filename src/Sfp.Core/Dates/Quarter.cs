namespace Sfp.Core.Dates;

/// <summary>A quarter: three calendar months from the 1st of the first month through the last day of the third.</summary>
/// <param name="Start">S, the 1st of the first month.</param>
/// <param name="End">E, the last day of the third month.</param>
/// <param name="WeekCount">N, always 13 or 14.</param>
/// <param name="Label">"Q1 2027" for an on-cycle start, otherwise the month span such as "Feb to Apr 2027".</param>
public sealed record Quarter(DateOnly Start, DateOnly End, int WeekCount, string Label)
{
    /// <summary>
    /// L, the number of days in the quarter: 90, 91 or 92 for an on-cycle quarter, and 89 for an off-cycle start in
    /// February of a common year (28 + 31 + 30), so 89 to 92 in all.
    /// </summary>
    public int Length => End.DayNumber - Start.DayNumber + 1;

    /// <summary>The first month of the quarter as a spec.</summary>
    public QuarterSpec Spec => new(Start.Year, Start.Month);

    /// <summary>The three (year, month) pairs the quarter covers, in order.</summary>
    public IReadOnlyList<(int Year, int Month)> Months =>
        new[] { 0, 1, 2 }.Select(i => { DateOnly d = Start.AddMonths(i); return (d.Year, d.Month); }).ToList();

    /// <summary>The "Q1" or "Feb to Apr" part of the label, without the year.</summary>
    public string ShortLabel => Label.Length > 5 ? Label[..^5] : Label;

    /// <summary>True when the date lies inside the quarter.</summary>
    public bool Contains(DateOnly date) => date >= Start && date <= End;

    /// <summary>1-based day index within the quarter (S is day 1, E is day L). Throws <see cref="ArgumentOutOfRangeException"/> for a date outside the quarter.</summary>
    public int DayOfQuarter(DateOnly date) =>
        Contains(date)
            ? date.DayNumber - Start.DayNumber + 1
            : throw new ArgumentOutOfRangeException(nameof(date), date, OutsideMessage(this));

    /// <summary>The message for a date that is not inside the quarter, with ISO dates.</summary>
    internal static string OutsideMessage(Quarter q) =>
        "Date must be inside the quarter "
        + q.Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        + " to "
        + q.End.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        + ".";
}
