using System.Globalization;
using Sfp.Core.Plan;
using static Sfp.Core.Tests.Plan.PlanFixture;

namespace Sfp.Core.Tests.Plan;

/// <summary>Page keys are culture-invariant: Gregorian years and ASCII digits whatever the machine's culture.</summary>
public class PageKeyTests
{
    [Fact]
    public void Keys_use_the_invariant_culture_under_a_Buddhist_calendar_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo thai = new("th-TH");
            CultureInfo.CurrentCulture = thai;
            CultureInfo.CurrentUICulture = thai;
            D(2027, 1, 4).ToString("yyyy-MM-dd", thai).Should().Be("2570-01-04", "th-TH formats dates in the Thai Buddhist era, which is what the keys must not follow");

            PageKey.Day(D(2027, 1, 4)).Value.Should().Be("day:2027-01-04");
            PageKey.Calendar(2027, 1).Value.Should().Be("calendar:2027-01");
            PageKey.MonthlyReview(2027, 3).Value.Should().Be("review:2027-03");
            PageKey.WeekOverview(2).Value.Should().Be("week:02:overview");
            PageKey.WeekReflection(14).Value.Should().Be("week:14:reflection");
            PageKey.Annual(2).Value.Should().Be("annual:2");
            PageKey.Objective(8).Value.Should().Be("objective:8");
            Q1_2027.ToString().Should().Be("2027-01");
            Build(Q1_2027).Pages.Should().Contain(p => p.Key.Value == "day:2027-01-04")
                .And.NotContain(p => p.Key.Value.Contains("2570", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void Keys_have_the_documented_forms()
    {
        PageKey.Day(D(2027, 1, 4)).Value.Should().Be("day:2027-01-04");
        PageKey.Calendar(1900, 1).Value.Should().Be("calendar:1900-01");
        PageKey.MonthlyReview(2200, 2).Value.Should().Be("review:2200-02");
        PageKey.WeekOverview(1).Value.Should().Be("week:01:overview");
        PageKey.Notes("  Reading List ").Value.Should().Be("notes:reading-list");
        PageKey.QuarterlyReview.ToString().Should().Be("review:quarter");
    }
}
