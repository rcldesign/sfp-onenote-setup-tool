using System.Globalization;

namespace Sfp.Core.Plan;

/// <summary>
/// Stable identity of a page across runs, notebook moves and ID changes. Written into every page as one:Meta sfp.key.
/// Forms: "cover", "contents", "setup", "annual:1", "annual:2", "objectives", "objective:3", "calendar:2027-01",
/// "week:02:overview", "day:2027-01-04", "week:02:reflection", "scorecard", "review:2027-01", "review:quarter", "notes:backlog".
/// Every number and date is formatted with <see cref="CultureInfo.InvariantCulture"/> (Gregorian, ASCII digits), so a
/// key never depends on the machine's culture: under th-TH a culture-formatted date would read "day:2570-01-04".
/// </summary>
public sealed record PageKey(string Value)
{
    public static PageKey Cover { get; } = new("cover");
    public static PageKey Contents { get; } = new("contents");
    public static PageKey Setup { get; } = new("setup");
    public static PageKey ObjectiveDetails { get; } = new("objectives");
    public static PageKey Scorecard { get; } = new("scorecard");
    public static PageKey QuarterlyReview { get; } = new("review:quarter");

    public static PageKey Annual(int index1) => new("annual:" + Number(index1, "0"));
    public static PageKey Objective(int n) => new("objective:" + Number(n, "0"));
    public static PageKey Calendar(int year, int month) => new("calendar:" + YearMonth(year, month));
    public static PageKey WeekOverview(int week) => new("week:" + Number(week, "00") + ":overview");
    public static PageKey WeekReflection(int week) => new("week:" + Number(week, "00") + ":reflection");
    public static PageKey Day(DateOnly date) => new("day:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    public static PageKey MonthlyReview(int year, int month) => new("review:" + YearMonth(year, month));
    public static PageKey Notes(string name) => new($"notes:{name.Trim().ToLowerInvariant().Replace(' ', '-')}");

    public override string ToString() => Value;

    private static string YearMonth(int year, int month) => Number(year, "0000") + "-" + Number(month, "00");

    private static string Number(int value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
