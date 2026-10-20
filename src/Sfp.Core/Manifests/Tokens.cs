namespace Sfp.Core.Manifests;

/// <summary>
/// The token set allowed in manifest name and title patterns. The TokenExpander (M1.2) expands them and rejects
/// anything else at manifest load time with the offending token named.
/// </summary>
public static class Tokens
{
    /// <summary>Two-digit week number, "02".</summary>
    public const string WeekNumber = "NN";

    /// <summary>Objective number, "3".</summary>
    public const string ObjectiveNumber = "n";

    /// <summary>In-quarter week start, "Jan 4".</summary>
    public const string WeekStart = "start";

    /// <summary>In-quarter week end, "Jan 10".</summary>
    public const string WeekEnd = "end";

    /// <summary>Three-letter day name, "Mon".</summary>
    public const string DayAbbrev = "Ddd";

    /// <summary>Three-letter month name, "Jan".</summary>
    public const string MonthAbbrev = "Mon";

    /// <summary>Day of month without padding, "4".</summary>
    public const string DayOfMonth = "D";

    /// <summary>Full month name, "January".</summary>
    public const string MonthName = "Month";

    /// <summary>Four-digit year, "2027".</summary>
    public const string Year = "YYYY";

    /// <summary>Quarter label without year, "Q1" or "Feb to Apr".</summary>
    public const string QuarterLabel = "Qn";

    /// <summary>Notes page name, "Backlog" (only valid with eachNote).</summary>
    public const string NoteName = "Note";

    /// <summary>Every token the manifest may use, in canonical order.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        WeekNumber, ObjectiveNumber, WeekStart, WeekEnd, DayAbbrev, MonthAbbrev, DayOfMonth, MonthName, Year, QuarterLabel, NoteName,
    };

    /// <summary>Extra per-page values the plan builder adds to PageSpec.Tokens for the renderer (not valid in patterns).</summary>
    public static class Extra
    {
        public const string Week = "week";
        public const string WeekCount = "weekCount";
        public const string WeeksRemaining = "weeksRemaining";
        public const string DayOfQuarter = "dayOfQuarter";
        public const string QuarterLength = "quarterLength";
        public const string WeekdayIndex = "weekdayIndex";
        public const string PdfPageName = "pdfPageName";
        public const string InQuarter = "inQuarter";
        public const string MonthIndex = "monthIndex";
        public const string ObjectiveIndex = "objective";
        public const string DateIso = "dateIso";
    }
}
