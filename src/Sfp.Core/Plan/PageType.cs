namespace Sfp.Core.Plan;

/// <summary>Every kind of page the notebook can hold. The manifest's page type names map to these.</summary>
public enum PageType
{
    Cover,
    Contents,
    Setup,
    Annual,
    Objective,
    Calendar,
    WeeklyOverview,
    Day,
    WeeklyReflection,
    Scorecard,
    MonthlyReview,
    QuarterlyReview,
    Notes,

    /// <summary>An empty parent page (the "Objective Details" page that holds the eight subpages).</summary>
    Blank,
}
