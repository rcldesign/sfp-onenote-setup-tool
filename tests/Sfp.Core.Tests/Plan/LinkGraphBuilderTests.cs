using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using static Sfp.Core.Tests.Plan.PlanFixture;

namespace Sfp.Core.Tests.Plan;

/// <summary>M1.7: the link graph per the PRD link map, navigation, scorecard, reviews and notes, with the exact AnchorIds the renderer joins on.</summary>
public class LinkGraphBuilderTests
{
    private static readonly Lazy<NotebookPlan> PlanLazy = new(() => Build(Q1_2027));

    private static NotebookPlan Plan => PlanLazy.Value;

    private static ILookup<string, LinkSpec> ByType(NotebookPlan plan) => plan.Links.ToLookup(l => l.AnchorType, StringComparer.Ordinal);

    private static LinkSpec Link(NotebookPlan plan, PageKey from, string anchorId) =>
        plan.LinksFrom(from).Single(l => l.AnchorId == anchorId);

    private static int CalendarWeekRowsExpected(QuarterSpec spec)
    {
        // Rows across the three month grids whose Monday cell starts a week that holds at least one quarter day.
        Quarter q = Engine.Build(spec);
        HashSet<DateOnly> mondays = Engine.Weeks(q).Select(w => w.MondayStart).ToHashSet();
        return q.Months.Sum(ym => Engine.Grid(ym.Year, ym.Month, q).Rows.Count(row => mondays.Contains(row[0].Date)));
    }

    [Fact]
    public void Q1_2027_anchor_counts_match_the_PRD_link_map_and_navigation()
    {
        ILookup<string, LinkSpec> links = ByType(Plan);

        links["annualObjective"].Should().HaveCount(8);
        links["objectiveBack"].Should().HaveCount(8);
        links["calendarDay"].Should().HaveCount(90);
        links["calendarWeek"].Should().HaveCount(15);
        links["dailyDate"].Should().HaveCount(90);
        links["overviewDayHeader"].Should().HaveCount(90);
        (links["annualObjective"].Count() + links["objectiveBack"].Count()).Should().Be(16, "16 objective links in all");

        links["prev"].Count(l => Plan.Page(l.From).Type == PageType.Day).Should().Be(89);
        links["next"].Count(l => Plan.Page(l.From).Type == PageType.Day).Should().Be(89);
        links["prev"].Count(l => Plan.Page(l.From).Type == PageType.WeeklyOverview).Should().Be(13);
        links["next"].Count(l => Plan.Page(l.From).Type == PageType.WeeklyOverview).Should().Be(13);
        links["prev"].Count(l => Plan.Page(l.From).Type == PageType.WeeklyReflection).Should().Be(13);
        links["next"].Count(l => Plan.Page(l.From).Type == PageType.WeeklyReflection).Should().Be(13);
        links["prev"].Should().HaveCount(89 + 13 + 13);
        links["next"].Should().HaveCount(89 + 13 + 13);

        links["reflectionNext"].Should().HaveCount(13, "week 14 has no next week");
        links["progressScorecard"].Should().HaveCount(14);
        links["reflectionQuarterReview"].Should().HaveCount(1);
        links["prevCalendar"].Should().HaveCount(2);
        links["nextCalendar"].Should().HaveCount(2);
        links["calendarReview"].Should().HaveCount(3);
        links["scorecardWeek"].Should().HaveCount(14);
        links["quarterReviewScorecard"].Should().HaveCount(1);
        links["notesContents"].Should().HaveCount(4);
        links["nextQuarter"].Should().BeEmpty("the slot is reserved for a later Generate, not a link in this notebook");

        Plan.Links.Should().HaveCount(8 + 8 + 90 + 15 + 90 + 90 + (2 * 89) + (2 * 13) + (2 * 13) + 13 + 14 + 1 + 2 + 2 + 3 + 14 + 1 + 4);
        links.Select(g => g.Key).Should().BeEquivalentTo(new[]
        {
            "annualObjective", "objectiveBack", "calendarDay", "calendarWeek", "dailyDate", "overviewDayHeader", "prev", "next",
            "reflectionNext", "progressScorecard", "reflectionQuarterReview", "prevCalendar", "nextCalendar", "calendarReview",
            "scorecardWeek", "quarterReviewScorecard", "notesContents",
        });
    }

    [Fact]
    public void Calendar_week_count_equals_the_grid_rows_whose_Monday_week_holds_a_quarter_day()
    {
        int expected = CalendarWeekRowsExpected(Q1_2027);

        expected.Should().Be(15, "the PRD counts 15 week labels for Q1 2027: 5 rows in each of the three grids");
        ByType(Plan)["calendarWeek"].Should().HaveCount(expected);
        ByType(Build(Q2_2024))["calendarWeek"].Should().HaveCount(CalendarWeekRowsExpected(Q2_2024));
        ByType(Build(new QuarterSpec(2026, 10)))["calendarWeek"].Should().HaveCount(CalendarWeekRowsExpected(new QuarterSpec(2026, 10)));
    }

    [Fact]
    public void Every_link_joins_two_pages_of_the_plan_and_anchor_ids_are_unique_per_page()
    {
        HashSet<PageKey> keys = Plan.Pages.Select(p => p.Key).ToHashSet();

        Plan.Links.Should().OnlyContain(l => keys.Contains(l.From), "every From is a page in the plan");
        Plan.Links.Should().OnlyContain(l => keys.Contains(l.To), "every To resolves to a page in the plan");
        Plan.Links.Should().OnlyContain(l => l.From != l.To);
        Plan.Links.Select(l => (l.From, l.AnchorId)).Should().OnlyHaveUniqueItems();
        Plan.Links.Should().OnlyContain(l => l.Text.Length > 0);
    }

    [Fact]
    public void Objective_links_go_both_ways_between_the_annual_block_page_and_the_detail_page()
    {
        Link(Plan, PageKey.Annual(1), "annualObjective:1").Should().Be(new LinkSpec(PageKey.Annual(1), "annualObjective:1", PageKey.Objective(1), "Objective 1"));
        Link(Plan, PageKey.Annual(1), "annualObjective:4").To.Should().Be(PageKey.Objective(4));
        Link(Plan, PageKey.Annual(2), "annualObjective:5").Should().Be(new LinkSpec(PageKey.Annual(2), "annualObjective:5", PageKey.Objective(5), "Objective 5"));
        Link(Plan, PageKey.Annual(2), "annualObjective:8").To.Should().Be(PageKey.Objective(8));
        Plan.LinksFrom(PageKey.Annual(1)).Should().HaveCount(4);
        Plan.LinksFrom(PageKey.Annual(2)).Should().HaveCount(4);

        Link(Plan, PageKey.Objective(3), "objectiveBack").Should().Be(new LinkSpec(PageKey.Objective(3), "objectiveBack", PageKey.Annual(1), "Objective 3"));
        Link(Plan, PageKey.Objective(7), "objectiveBack").To.Should().Be(PageKey.Annual(2));
        Plan.LinksFrom(PageKey.ObjectiveDetails).Should().BeEmpty();
    }

    [Fact]
    public void Calendar_day_anchors_cover_the_in_month_days_and_link_to_the_day_pages()
    {
        IReadOnlyList<LinkSpec> january = Plan.LinksFrom(PageKey.Calendar(2027, 1)).Where(l => l.AnchorType == "calendarDay").ToList();
        IReadOnlyList<LinkSpec> february = Plan.LinksFrom(PageKey.Calendar(2027, 2)).Where(l => l.AnchorType == "calendarDay").ToList();
        IReadOnlyList<LinkSpec> march = Plan.LinksFrom(PageKey.Calendar(2027, 3)).Where(l => l.AnchorType == "calendarDay").ToList();

        january.Should().HaveCount(31);
        february.Should().HaveCount(28, "March 1 to 7 in the fifth row are out-of-month days, baked greyed per the PRD");
        march.Should().HaveCount(31, "April 1 to 4 are outside the quarter");
        january.Select(l => l.Text).Should().Equal(Enumerable.Range(1, 31).Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Link(Plan, PageKey.Calendar(2027, 1), "calendarDay:2027-01-04").Should().Be(new LinkSpec(PageKey.Calendar(2027, 1), "calendarDay:2027-01-04", PageKey.Day(D(2027, 1, 4)), "4"));
        Plan.LinksFrom(PageKey.Calendar(2027, 2)).Should().NotContain(l => l.AnchorId == "calendarDay:2027-03-01");
        Plan.LinksFrom(PageKey.Calendar(2027, 1)).Should().NotContain(l => l.AnchorId == "calendarDay:2026-12-28");
    }

    [Fact]
    public void Calendar_week_labels_sit_on_each_row_and_link_to_that_weeks_overview()
    {
        Plan.LinksFrom(PageKey.Calendar(2027, 1)).Where(l => l.AnchorType == "calendarWeek").Select(l => l.AnchorId)
            .Should().Equal("calendarWeek:01", "calendarWeek:02", "calendarWeek:03", "calendarWeek:04", "calendarWeek:05");
        Plan.LinksFrom(PageKey.Calendar(2027, 2)).Where(l => l.AnchorType == "calendarWeek").Select(l => l.AnchorId)
            .Should().Equal("calendarWeek:06", "calendarWeek:07", "calendarWeek:08", "calendarWeek:09", "calendarWeek:10");
        Plan.LinksFrom(PageKey.Calendar(2027, 3)).Where(l => l.AnchorType == "calendarWeek").Select(l => l.AnchorId)
            .Should().Equal("calendarWeek:10", "calendarWeek:11", "calendarWeek:12", "calendarWeek:13", "calendarWeek:14");
        Link(Plan, PageKey.Calendar(2027, 1), "calendarWeek:01").Should().Be(new LinkSpec(PageKey.Calendar(2027, 1), "calendarWeek:01", PageKey.WeekOverview(1), "Week 01"));
        Link(Plan, PageKey.Calendar(2027, 3), "calendarWeek:14").To.Should().Be(PageKey.WeekOverview(14));
    }

    [Fact]
    public void A_six_row_month_links_its_stacked_days_nowhere_but_keeps_the_row_labels()
    {
        NotebookPlan plan = Build(new QuarterSpec(2026, 10));
        IReadOnlyList<LinkSpec> november = plan.LinksFrom(PageKey.Calendar(2026, 11)).ToList();

        november.Where(l => l.AnchorType == "calendarDay").Should().HaveCount(29, "Nov 1 (a Sunday) to Nov 29 sit in the five rows and Nov 30 is stacked");
        november.Should().NotContain(l => l.AnchorId == "calendarDay:2026-11-30");
        november.Where(l => l.AnchorType == "calendarWeek").Should().HaveCount(5);
        plan.LinksFrom(PageKey.Calendar(2026, 11)).Select(l => l.AnchorId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Calendar_navigation_and_review_links()
    {
        PageKey jan = PageKey.Calendar(2027, 1);
        PageKey feb = PageKey.Calendar(2027, 2);
        PageKey mar = PageKey.Calendar(2027, 3);

        Plan.LinksFrom(jan).Should().NotContain(l => l.AnchorId == "prevCalendar");
        Link(Plan, jan, "nextCalendar").Should().Be(new LinkSpec(jan, "nextCalendar", feb, "February 2027 ›"));
        Link(Plan, feb, "prevCalendar").Should().Be(new LinkSpec(feb, "prevCalendar", jan, "‹ January 2027"));
        Link(Plan, feb, "nextCalendar").To.Should().Be(mar);
        Plan.LinksFrom(mar).Should().NotContain(l => l.AnchorId == "nextCalendar");
        Link(Plan, jan, "calendarReview").Should().Be(new LinkSpec(jan, "calendarReview", PageKey.MonthlyReview(2027, 1), "Review ›"));
        Link(Plan, mar, "calendarReview").To.Should().Be(PageKey.MonthlyReview(2027, 3));
    }

    [Fact]
    public void Daily_date_links_to_the_weeks_overview_with_the_date_slot_text()
    {
        Link(Plan, PageKey.Day(D(2027, 1, 4)), "dailyDate").Should().Be(new LinkSpec(PageKey.Day(D(2027, 1, 4)), "dailyDate", PageKey.WeekOverview(2), "01 / 04 / 27"));
        Link(Plan, PageKey.Day(D(2027, 1, 1)), "dailyDate").Should().Be(new LinkSpec(PageKey.Day(D(2027, 1, 1)), "dailyDate", PageKey.WeekOverview(1), "01 / 01 / 27"));
        Link(Plan, PageKey.Day(D(2027, 3, 31)), "dailyDate").To.Should().Be(PageKey.WeekOverview(14));
    }

    [Fact]
    public void Daily_date_text_follows_the_date_order_and_padding_options()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { DateOrder = DateFieldOrder.DayMonthYear, PadDateFields = false });

        Link(plan, PageKey.Day(D(2027, 1, 4)), "dailyDate").Text.Should().Be("4 / 1 / 27");
    }

    [Fact]
    public void Day_prev_and_next_chain_through_the_quarter_with_the_neighbor_titles()
    {
        PageKey jan4 = PageKey.Day(D(2027, 1, 4));

        Link(Plan, jan4, "prev").Should().Be(new LinkSpec(jan4, "prev", PageKey.Day(D(2027, 1, 3)), "‹ Sun Jan 3"));
        Link(Plan, jan4, "next").Should().Be(new LinkSpec(jan4, "next", PageKey.Day(D(2027, 1, 5)), "Tue Jan 5 ›"));
        Plan.LinksFrom(PageKey.Day(D(2027, 1, 1))).Should().NotContain(l => l.AnchorId == "prev");
        Plan.LinksFrom(PageKey.Day(D(2027, 3, 31))).Should().NotContain(l => l.AnchorId == "next");
        Link(Plan, PageKey.Day(D(2027, 1, 31)), "next").To.Should().Be(PageKey.Day(D(2027, 2, 1)), "the chain crosses month and week boundaries");
        Plan.LinksFrom(jan4).Select(l => l.AnchorId).Should().Equal("dailyDate", "prev", "next");
    }

    [Fact]
    public void Overview_day_headers_cover_the_quarter_days_of_the_week_with_full_day_names()
    {
        PageKey week1 = PageKey.WeekOverview(1);
        PageKey week2 = PageKey.WeekOverview(2);

        Plan.LinksFrom(week1).Where(l => l.AnchorType == "overviewDayHeader").Select(l => (l.AnchorId, l.Text, l.To.Value)).Should().Equal(
            ("overviewDayHeader:4", "Friday", "day:2027-01-01"),
            ("overviewDayHeader:5", "Saturday", "day:2027-01-02"),
            ("overviewDayHeader:6", "Sunday", "day:2027-01-03"));
        Plan.LinksFrom(week2).Where(l => l.AnchorType == "overviewDayHeader").Select(l => l.Text).Should().Equal(
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday");
        Link(Plan, week2, "overviewDayHeader:0").To.Should().Be(PageKey.Day(D(2027, 1, 4)));
        Plan.LinksFrom(PageKey.WeekOverview(14)).Where(l => l.AnchorType == "overviewDayHeader").Should().HaveCount(3);
    }

    [Fact]
    public void Overview_and_reflection_prev_next_use_the_week_label()
    {
        Plan.LinksFrom(PageKey.WeekOverview(1)).Should().NotContain(l => l.AnchorId == "prev");
        Link(Plan, PageKey.WeekOverview(1), "next").Should().Be(new LinkSpec(PageKey.WeekOverview(1), "next", PageKey.WeekOverview(2), "Week 02 ›"));
        Link(Plan, PageKey.WeekOverview(2), "prev").Should().Be(new LinkSpec(PageKey.WeekOverview(2), "prev", PageKey.WeekOverview(1), "‹ Week 01"));
        Plan.LinksFrom(PageKey.WeekOverview(14)).Should().NotContain(l => l.AnchorId == "next");

        Link(Plan, PageKey.WeekReflection(2), "prev").Should().Be(new LinkSpec(PageKey.WeekReflection(2), "prev", PageKey.WeekReflection(1), "‹ Week 01"));
        Link(Plan, PageKey.WeekReflection(2), "next").To.Should().Be(PageKey.WeekReflection(3));
        Plan.LinksFrom(PageKey.WeekReflection(14)).Should().NotContain(l => l.AnchorId == "next");
    }

    [Fact]
    public void Reflection_links_forward_to_the_next_overview_to_the_scorecard_and_at_the_end_to_the_quarterly_review()
    {
        PageKey week13 = PageKey.WeekReflection(13);
        PageKey week14 = PageKey.WeekReflection(14);

        Link(Plan, week13, "reflectionNext").Should().Be(new LinkSpec(week13, "reflectionNext", PageKey.WeekOverview(14), "Week 14 ›"));
        Link(Plan, week13, "progressScorecard").Should().Be(new LinkSpec(week13, "progressScorecard", PageKey.Scorecard, "Progress %"));
        Plan.LinksFrom(week13).Should().NotContain(l => l.AnchorId == "reflectionQuarterReview");

        Plan.LinksFrom(week14).Should().NotContain(l => l.AnchorId == "reflectionNext");
        Link(Plan, week14, "reflectionQuarterReview").Should().Be(new LinkSpec(week14, "reflectionQuarterReview", PageKey.QuarterlyReview, "Q1 2027 Review ›"));
        Plan.LinksFrom(week14).Select(l => l.AnchorId).Should().Equal("prev", "progressScorecard", "reflectionQuarterReview");
    }

    [Fact]
    public void Scorecard_columns_link_to_every_reflection_and_the_reviews_link_back()
    {
        Plan.LinksFrom(PageKey.Scorecard).Select(l => (l.AnchorId, l.Text, l.To.Value)).Should().StartWith(new[]
        {
            ("scorecardWeek:01", "01", "week:01:reflection"),
            ("scorecardWeek:02", "02", "week:02:reflection"),
        });
        Link(Plan, PageKey.Scorecard, "scorecardWeek:14").To.Should().Be(PageKey.WeekReflection(14));
        Plan.LinksFrom(PageKey.QuarterlyReview).Should().Equal(new LinkSpec(PageKey.QuarterlyReview, "quarterReviewScorecard", PageKey.Scorecard, "Scorecard ›"));
        Plan.LinksFrom(PageKey.MonthlyReview(2027, 1)).Should().BeEmpty();
    }

    [Fact]
    public void Notes_pages_link_back_to_contents()
    {
        foreach (string name in new[] { "Backlog", "Ideas", "Notes", "Contacts" })
        {
            Plan.LinksFrom(PageKey.Notes(name)).Should().Equal(new LinkSpec(PageKey.Notes(name), "notesContents", PageKey.Contents, "Contents ›"));
        }
    }

    [Fact]
    public void Pages_without_anchors_carry_no_links()
    {
        foreach (PageKey key in new[] { PageKey.Cover, PageKey.Contents, PageKey.Setup, PageKey.ObjectiveDetails })
        {
            Plan.LinksFrom(key).Should().BeEmpty();
        }
    }

    [Fact]
    public void Links_are_empty_when_the_option_is_off()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { Links = false });

        plan.Links.Should().BeEmpty();
        new LinkGraphBuilder(Engine).Build(Shipped, Engine.Build(Q1_2027), plan.Sections, new GenerateOptions { Start = Q1_2027, Links = false }).Should().BeEmpty();
        new LinkGraphBuilder(Engine).Build(Shipped, Engine.Build(Q1_2027), plan.Sections, new GenerateOptions { Start = Q1_2027 }).Should().HaveCount(Plan.Links.Count);
    }

    [Fact]
    public void Out_of_quarter_day_pages_join_the_prev_next_chain_but_get_no_date_or_header_links()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { IncludeOutOfQuarterDays = true });
        PageKey dec31 = PageKey.Day(D(2026, 12, 31));
        PageKey jan1 = PageKey.Day(D(2027, 1, 1));

        Link(plan, jan1, "prev").Should().Be(new LinkSpec(jan1, "prev", dec31, "‹ Thu Dec 31"));
        Link(plan, dec31, "next").To.Should().Be(jan1);
        plan.LinksFrom(PageKey.Day(D(2026, 12, 28))).Select(l => l.AnchorId).Should().Equal("next");
        plan.LinksFrom(dec31).Should().NotContain(l => l.AnchorId == "dailyDate");
        plan.LinksFrom(PageKey.WeekOverview(1)).Where(l => l.AnchorType == "overviewDayHeader").Should().HaveCount(3);
        plan.LinksFrom(PageKey.Calendar(2027, 1)).Should().NotContain(l => l.AnchorId == "calendarDay:2026-12-28");
        ILookup<string, LinkSpec> links = ByType(plan);
        links["dailyDate"].Should().HaveCount(90);
        links["prev"].Count(l => plan.Page(l.From).Type == PageType.Day).Should().Be(97);
        links["overviewDayHeader"].Should().HaveCount(90);
        plan.Links.Should().OnlyContain(l => plan.FindPage(l.To) != null);
    }

    [Fact]
    public void A_13_week_quarter_has_12_reflection_next_links_and_13_scorecard_columns()
    {
        NotebookPlan plan = Build(Q2_2024);
        ILookup<string, LinkSpec> links = ByType(plan);

        links["reflectionNext"].Should().HaveCount(12);
        links["scorecardWeek"].Should().HaveCount(13);
        links["progressScorecard"].Should().HaveCount(13);
        links["dailyDate"].Should().HaveCount(91);
        links["calendarDay"].Should().HaveCount(91);
        links["prev"].Count(l => plan.Page(l.From).Type == PageType.Day).Should().Be(90);
        links["reflectionQuarterReview"].Single().Should().Be(new LinkSpec(PageKey.WeekReflection(13), "reflectionQuarterReview", PageKey.QuarterlyReview, "Q2 2024 Review ›"));
    }

    [Fact]
    public void Links_drop_cleanly_when_the_manifest_leaves_a_target_page_out()
    {
        NotebookSpec notebook = Shipped.Notebook with
        {
            Sections = Shipped.Notebook.Sections.Where(s => s.Name != "Reviews" && s.Name != "Notes").ToList(),
        };
        Manifest trimmed = Shipped with { Notebook = notebook };

        NotebookPlan plan = new PlanBuilder(Engine).Build(trimmed, Engine.Build(Q1_2027), new GenerateOptions { Start = Q1_2027 });
        ILookup<string, LinkSpec> links = ByType(plan);

        plan.Sections.Should().HaveCount(17);
        links["progressScorecard"].Should().BeEmpty();
        links["reflectionQuarterReview"].Should().BeEmpty();
        links["calendarReview"].Should().BeEmpty();
        links["notesContents"].Should().BeEmpty();
        links["reflectionNext"].Should().HaveCount(13);
        plan.Links.Should().OnlyContain(l => plan.FindPage(l.To) != null);
    }

    [Fact]
    public void LinkText_fills_the_title_token_or_returns_the_title()
    {
        LinkGraphBuilder.LinkText(Shipped.Links.Prev, "Sun Jan 3").Should().Be("‹ Sun Jan 3");
        LinkGraphBuilder.LinkText(Shipped.Links.Next, "Tue Jan 5").Should().Be("Tue Jan 5 ›");
        LinkGraphBuilder.LinkText(Shipped.Links.NotesContents, "Contents").Should().Be("Contents ›");
        LinkGraphBuilder.LinkText(Shipped.Links.ObjectiveBack, "Objective 3").Should().Be("Objective 3", "an anchor without textFormat shows the title");
        LinkGraphBuilder.WeekLabel(2).Should().Be("Week 02");
        LinkGraphBuilder.ObjectiveLabel(8).Should().Be("Objective 8");
    }
}
