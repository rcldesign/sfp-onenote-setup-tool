using Sfp.Core.Dates;
using Sfp.Core.Plan;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// A hand-built small notebook plan for the writer and orchestrator tests, standing in for the M1.6 plan builder: the
/// Quarter section (Cover, Contents, Setup), one week section with an overview, two days and a reflection, and a few
/// link specs (daily date, previous and next, overview day headers, reflection to the overview).
/// </summary>
public static class TestPlans
{
    public static readonly Quarter Q1 = new DateEngine().Build(new QuarterSpec(2027, 1));

    public const string WeekSectionName = "Week 02 (Jan 4 to Jan 10)";

    public static readonly PageKey Overview = PageKey.WeekOverview(2);
    public static readonly PageKey Monday = PageKey.Day(new DateOnly(2027, 1, 4));
    public static readonly PageKey Tuesday = PageKey.Day(new DateOnly(2027, 1, 5));
    public static readonly PageKey Reflection = PageKey.WeekReflection(2);

    /// <summary>The plan: 2 sections, 7 pages (one of them the Contents page), 6 links.</summary>
    public static NotebookPlan Small()
    {
        DateOnly start = Q1.Start;
        SectionSpec quarter = new("Quarter", null, new[]
        {
            Page(PageKey.Cover, PageType.Cover, "Cover", 1, 1, start),
            Page(PageKey.Contents, PageType.Contents, "Contents", 1, null, start),
            Page(PageKey.Setup, PageType.Setup, "Setup", 1, null, start),
        });
        SectionSpec week = new(WeekSectionName, "#F0E6D8", new[]
        {
            Page(Overview, PageType.WeeklyOverview, "Weekly Overview", 1, 25, new DateOnly(2027, 1, 4)),
            Page(Monday, PageType.Day, "Mon Jan 4", 2, 26, new DateOnly(2027, 1, 4)),
            Page(Tuesday, PageType.Day, "Tue Jan 5", 2, 27, new DateOnly(2027, 1, 5)),
            Page(Reflection, PageType.WeeklyReflection, "Weekly Reflection", 1, 33, new DateOnly(2027, 1, 10)),
        });
        LinkSpec[] links =
        {
            new(Monday, "dailyDate", Overview, "01 / 04 / 27"),
            new(Monday, "next", Tuesday, "Tue Jan 5 ›"),
            new(Tuesday, "prev", Monday, "‹ Mon Jan 4"),
            new(Overview, "overviewDayHeader:0", Monday, "Monday"),
            new(Overview, "overviewDayHeader:1", Tuesday, "Tuesday"),
            new(Reflection, "reflectionNext", Overview, "Weekly Overview ›"),
        };
        return new NotebookPlan("SFP Q1 2027", Q1, new[] { quarter, week }, links);
    }

    /// <summary>The pages the orchestrator renders: everything but the native Contents page.</summary>
    public static IReadOnlyList<PageSpec> Renderable(NotebookPlan plan) => plan.Pages.Where(p => p.Type != PageType.Contents).ToList();

    private static PageSpec Page(PageKey key, PageType type, string title, int level, int? pdfPage, DateOnly date) =>
        new(key, type, title, level, pdfPage, date, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["YYYY"] = "2027",
            ["Qn"] = "Q1",
        });
}
