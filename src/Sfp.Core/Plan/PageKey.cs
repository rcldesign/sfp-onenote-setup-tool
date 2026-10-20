using System.Globalization;

namespace Sfp.Core.Plan;

/// <summary>
/// Stable identity of a page across runs, notebook moves and ID changes. Written into every page as one:Meta sfp.key.
/// Forms: "cover", "contents", "setup", "annual:1", "annual:2", "objectives", "objective:3", "calendar:2027-01",
/// "week:02:overview", "day:2027-01-04", "week:02:reflection", "scorecard", "review:2027-01", "review:quarter", "notes:backlog".
/// </summary>
public sealed record PageKey(string Value)
{
    public static PageKey Cover { get; } = new("cover");
    public static PageKey Contents { get; } = new("contents");
    public static PageKey Setup { get; } = new("setup");
    public static PageKey ObjectiveDetails { get; } = new("objectives");
    public static PageKey Scorecard { get; } = new("scorecard");
    public static PageKey QuarterlyReview { get; } = new("review:quarter");

    public static PageKey Annual(int index1) => new($"annual:{index1.ToString(CultureInfo.InvariantCulture)}");
    public static PageKey Objective(int n) => new($"objective:{n.ToString(CultureInfo.InvariantCulture)}");
    public static PageKey Calendar(int year, int month) => new($"calendar:{year:0000}-{month:00}");
    public static PageKey WeekOverview(int week) => new($"week:{week:00}:overview");
    public static PageKey WeekReflection(int week) => new($"week:{week:00}:reflection");
    public static PageKey Day(DateOnly date) => new($"day:{date:yyyy-MM-dd}");
    public static PageKey MonthlyReview(int year, int month) => new($"review:{year:0000}-{month:00}");
    public static PageKey Notes(string name) => new($"notes:{name.Trim().ToLowerInvariant().Replace(' ', '-')}");

    public override string ToString() => Value;
}
