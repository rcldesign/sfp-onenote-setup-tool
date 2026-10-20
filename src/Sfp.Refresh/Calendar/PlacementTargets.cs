using Sfp.Core.Plan;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// Where a day's events go: the daily page, the Weekly Overview of its week and the Monthly Calendar of its month.
/// Each function returns null when the notebook has no such page (a day outside the quarter, for example), and the
/// placement engine then emits nothing for it. Built from lambdas in tests or from a plan with <see cref="FromPlan"/>.
/// </summary>
/// <param name="DayPage">Daily page of a date.</param>
/// <param name="OverviewPage">Weekly Overview page of the week holding a date.</param>
/// <param name="CalendarPage">Monthly Calendar page of the month holding a date.</param>
public sealed record PlacementTargets(
    Func<DateOnly, PageKey?> DayPage,
    Func<DateOnly, PageKey?> OverviewPage,
    Func<DateOnly, PageKey?> CalendarPage)
{
    /// <summary>Targets that resolve against the pages of a plan by key, with overview pages matched by their Monday date.</summary>
    public static PlacementTargets FromPlan(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        HashSet<string> keys = plan.Pages.Select(p => p.Key.Value).ToHashSet(StringComparer.Ordinal);
        List<(DateOnly Monday, PageKey Key)> overviews = plan.Pages
            .Where(p => p.Type == PageType.WeeklyOverview && p.Date is not null)
            .Select(p => (p.Date!.Value, p.Key))
            .ToList();

        return new PlacementTargets(
            d => Lookup(keys, PageKey.Day(d)),
            d => OverviewFor(overviews, d),
            d => Lookup(keys, PageKey.Calendar(d.Year, d.Month)));
    }

    /// <summary>Targets with no pages at all (useful to place only a subset by composing with <c>with</c>).</summary>
    public static PlacementTargets None { get; } = new(_ => null, _ => null, _ => null);

    private static PageKey? Lookup(HashSet<string> keys, PageKey key) => keys.Contains(key.Value) ? key : null;

    private static PageKey? OverviewFor(List<(DateOnly Monday, PageKey Key)> overviews, DateOnly d)
    {
        foreach ((DateOnly monday, PageKey key) in overviews)
        {
            if (d >= monday && d <= monday.AddDays(6))
            {
                return key;
            }
        }

        return null;
    }
}
