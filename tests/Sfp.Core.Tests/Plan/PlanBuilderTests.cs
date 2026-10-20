using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using static Sfp.Core.Tests.Plan.PlanFixture;

namespace Sfp.Core.Tests.Plan;

/// <summary>M1.6: the plan builder walks the manifest notebook template into sections, pages, keys, titles, levels, PDF pages, dates and tokens.</summary>
public class PlanBuilderTests
{
    [Fact]
    public void Q1_2027_has_19_sections_and_144_pages_named_as_the_PRD_table()
    {
        NotebookPlan plan = Build(Q1_2027);

        plan.Name.Should().Be("SFP Q1 2027");
        plan.Sections.Should().HaveCount(19);
        plan.Pages.Should().HaveCount(144);
        plan.Sections.Select(s => s.Name).Should().Equal(
            "Quarter", "Objectives", "Calendar",
            "Week 01 (Jan 1 to Jan 3)", "Week 02 (Jan 4 to Jan 10)", "Week 03 (Jan 11 to Jan 17)", "Week 04 (Jan 18 to Jan 24)",
            "Week 05 (Jan 25 to Jan 31)", "Week 06 (Feb 1 to Feb 7)", "Week 07 (Feb 8 to Feb 14)", "Week 08 (Feb 15 to Feb 21)",
            "Week 09 (Feb 22 to Feb 28)", "Week 10 (Mar 1 to Mar 7)", "Week 11 (Mar 8 to Mar 14)", "Week 12 (Mar 15 to Mar 21)",
            "Week 13 (Mar 22 to Mar 28)", "Week 14 (Mar 29 to Mar 31)",
            "Reviews", "Notes");
    }

    [Fact]
    public void Page_keys_are_unique()
    {
        NotebookPlan plan = Build(Q1_2027);

        plan.Pages.Select(p => p.Key.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Quarter_section_holds_cover_contents_and_setup()
    {
        SectionSpec quarter = Section(Build(Q1_2027), "Quarter");

        quarter.ColorHex.Should().BeNull();
        quarter.GroupName.Should().BeNull();
        quarter.Pages.Select(p => (p.Title, p.Key.Value, p.Type, p.Level, p.PdfPage)).Should().Equal(
            ("Cover", "cover", PageType.Cover, 1, 1),
            ("Contents", "contents", PageType.Contents, 1, null),
            ("Setup", "setup", PageType.Setup, 1, null));
        quarter.Pages.Should().OnlyContain(p => p.Date == D(2027, 1, 1));
    }

    [Fact]
    public void Objectives_section_holds_two_annual_pages_the_blank_parent_and_eight_objective_subpages()
    {
        SectionSpec objectives = Section(Build(Q1_2027), "Objectives");

        objectives.Pages.Should().HaveCount(11);
        objectives.Pages[0].Should().BeEquivalentTo(new { Title = "Annual Objectives", Key = PageKey.Annual(1), Type = PageType.Annual, Level = 1, PdfPage = 3 });
        objectives.Pages[1].Should().BeEquivalentTo(new { Title = "Annual Objectives, continued", Key = PageKey.Annual(2), Type = PageType.Annual, Level = 2, PdfPage = 4 });
        objectives.Pages[2].Should().BeEquivalentTo(new { Title = "Objective Details", Key = PageKey.ObjectiveDetails, Type = PageType.Blank, Level = 1, PdfPage = (int?)null });
        for (int n = 1; n <= 8; n++)
        {
            PageSpec page = objectives.Pages[2 + n];
            page.Title.Should().Be($"Objective {n}");
            page.Key.Should().Be(PageKey.Objective(n));
            page.Type.Should().Be(PageType.Objective);
            page.Level.Should().Be(2);
            page.PdfPage.Should().Be(4 + n);
            page.Token(Tokens.ObjectiveNumber).Should().Be(n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            page.IntToken(Tokens.Extra.ObjectiveIndex).Should().Be(n);
        }
    }

    [Fact]
    public void Calendar_section_holds_the_three_quarter_months_on_pdf_pages_13_to_15_dated_the_first()
    {
        SectionSpec calendar = Section(Build(Q1_2027), "Calendar");

        calendar.Pages.Select(p => (p.Title, p.Key.Value, p.PdfPage, p.Date, p.Level)).Should().Equal(
            ("January 2027", "calendar:2027-01", 13, D(2027, 1, 1), 1),
            ("February 2027", "calendar:2027-02", 14, D(2027, 2, 1), 1),
            ("March 2027", "calendar:2027-03", 15, D(2027, 3, 1), 1));
        calendar.Pages.Select(p => p.IntToken(Tokens.Extra.MonthIndex)).Should().Equal(0, 1, 2);
        calendar.Pages.Should().OnlyContain(p => p.Type == PageType.Calendar);
    }

    [Fact]
    public void Week_01_holds_the_overview_three_days_and_the_reflection()
    {
        SectionSpec week1 = Section(Build(Q1_2027), "Week 01 (Jan 1 to Jan 3)");

        week1.Pages.Select(p => (p.Title, p.Level)).Should().Equal(
            ("Weekly Overview", 1), ("Fri Jan 1", 2), ("Sat Jan 2", 2), ("Sun Jan 3", 2), ("Weekly Reflection", 1));
        week1.Pages.Select(p => p.Key.Value).Should().Equal(
            "week:01:overview", "day:2027-01-01", "day:2027-01-02", "day:2027-01-03", "week:01:reflection");
        week1.Pages.Select(p => p.Type).Should().Equal(
            PageType.WeeklyOverview, PageType.Day, PageType.Day, PageType.Day, PageType.WeeklyReflection);
    }

    [Fact]
    public void Week_02_holds_seven_days_Mon_Jan_4_to_Sun_Jan_10()
    {
        SectionSpec week2 = Section(Build(Q1_2027), "Week 02 (Jan 4 to Jan 10)");

        week2.Pages.Should().HaveCount(9);
        week2.Pages.Where(p => p.Type == PageType.Day).Select(p => p.Title).Should().Equal(
            "Mon Jan 4", "Tue Jan 5", "Wed Jan 6", "Thu Jan 7", "Fri Jan 8", "Sat Jan 9", "Sun Jan 10");
    }

    [Fact]
    public void Reviews_section_holds_the_scorecard_three_monthly_reviews_and_the_quarterly_review()
    {
        SectionSpec reviews = Section(Build(Q1_2027), "Reviews");

        reviews.Pages.Select(p => (p.Title, p.Key.Value, p.Type, p.PdfPage, p.Date)).Should().Equal(
            ("Quarter scorecard", "scorecard", PageType.Scorecard, null, D(2027, 1, 1)),
            ("January Review", "review:2027-01", PageType.MonthlyReview, 132, D(2027, 1, 1)),
            ("February Review", "review:2027-02", PageType.MonthlyReview, 132, D(2027, 2, 1)),
            ("March Review", "review:2027-03", PageType.MonthlyReview, 132, D(2027, 3, 1)),
            ("Q1 2027 Review", "review:quarter", PageType.QuarterlyReview, 132, D(2027, 1, 1)));
        reviews.Pages.Should().OnlyContain(p => p.Level == 1);
    }

    [Fact]
    public void Notes_section_holds_the_default_four_pages_with_the_Note_token()
    {
        SectionSpec notes = Section(Build(Q1_2027), "Notes");

        notes.Pages.Select(p => (p.Title, p.Key.Value, p.PdfPage)).Should().Equal(
            ("Backlog", "notes:backlog", null), ("Ideas", "notes:ideas", null), ("Notes", "notes:notes", null), ("Contacts", "notes:contacts", null));
        notes.Pages.Should().OnlyContain(p => p.Type == PageType.Notes && p.Level == 1 && p.Date == D(2027, 1, 1));
        notes.Pages[0].Token(Tokens.NoteName).Should().Be("Backlog");
    }

    [Fact]
    public void Notes_pages_follow_the_option_list()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { NotesPages = new[] { "Reading list", "Scratch" } });

        Section(plan, "Notes").Pages.Select(p => (p.Title, p.Key.Value)).Should().Equal(
            ("Reading list", "notes:reading-list"), ("Scratch", "notes:scratch"));
        plan.Pages.Should().HaveCount(142);
    }

    [Fact]
    public void Q2_2024_has_18_sections_13_weeks_and_91_day_pages()
    {
        NotebookPlan plan = Build(Q2_2024);

        plan.Name.Should().Be("SFP Q2 2024");
        plan.Sections.Should().HaveCount(18);
        plan.Sections.Count(s => s.Name.StartsWith("Week ", StringComparison.Ordinal)).Should().Be(13);
        DayPages(plan).Should().HaveCount(91);
        plan.Pages.Should().HaveCount(3 + 11 + 3 + (13 * 2) + 91 + 5 + 4);
    }

    [Fact]
    public void Q1_2024_leap_year_has_91_day_pages_including_Feb_29()
    {
        NotebookPlan plan = Build(Q1_2024);

        DayPages(plan).Should().HaveCount(91);
        plan.FindPage(PageKey.Day(D(2024, 2, 29))).Should().NotBeNull().And.Match<PageSpec>(p => p.Title == "Thu Feb 29");
    }

    [Fact]
    public void Day_pages_use_the_weekday_page_of_their_week_run()
    {
        NotebookPlan plan = Build(Q1_2027);

        plan.Page(PageKey.Day(D(2027, 1, 3))).PdfPage.Should().Be(23, "Sun Jan 3 is the Sunday page of run 1");
        plan.Page(PageKey.Day(D(2027, 1, 1))).PdfPage.Should().Be(21, "Fri Jan 1 is the Friday page of run 1");
        plan.Page(PageKey.Day(D(2027, 1, 4))).PdfPage.Should().Be(26, "Mon Jan 4 is the Monday page of run 2");
        plan.Page(PageKey.WeekOverview(2)).PdfPage.Should().Be(25);
        plan.Page(PageKey.WeekReflection(2)).PdfPage.Should().Be(33);
        plan.Page(PageKey.WeekReflection(13)).PdfPage.Should().Be(132);
    }

    [Fact]
    public void Week_14_reuses_the_week_13_pdf_pages()
    {
        NotebookPlan plan = Build(Q1_2027);
        SectionSpec week13 = Section(plan, "Week 13 (Mar 22 to Mar 28)");
        SectionSpec week14 = Section(plan, "Week 14 (Mar 29 to Mar 31)");

        week14.Pages.Select(p => p.PdfPage).Should().Equal(124, 125, 126, 127, 132);
        week13.Pages.Select(p => p.PdfPage).Should().Equal(124, 125, 126, 127, 128, 129, 130, 131, 132);
        plan.Page(PageKey.Day(D(2027, 3, 29))).PdfPage.Should().Be(plan.Page(PageKey.Day(D(2027, 3, 22))).PdfPage);
    }

    [Fact]
    public void Day_pages_run_every_weekday_page_name_in_order()
    {
        NotebookPlan plan = Build(Q1_2027);

        Section(plan, "Week 02 (Jan 4 to Jan 10)").Pages.Where(p => p.Type == PageType.Day)
            .Select(p => p.Token(Tokens.Extra.PdfPageName)).Should().Equal(PageNames.Days);
    }

    [Fact]
    public void Overview_is_dated_the_Monday_and_reflection_the_Sunday_even_outside_the_quarter()
    {
        NotebookPlan plan = Build(Q1_2027);

        plan.Page(PageKey.WeekOverview(1)).Date.Should().Be(D(2026, 12, 28));
        plan.Page(PageKey.WeekReflection(1)).Date.Should().Be(D(2027, 1, 3));
        plan.Page(PageKey.WeekOverview(14)).Date.Should().Be(D(2027, 3, 29));
        plan.Page(PageKey.WeekReflection(14)).Date.Should().Be(D(2027, 4, 4));
        plan.Page(PageKey.Day(D(2027, 1, 4))).Date.Should().Be(D(2027, 1, 4));
    }

    [Fact]
    public void Reflection_date_follows_the_week_start_option()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { ReflectionDateIsWeekStart = true });

        plan.Page(PageKey.WeekReflection(1)).Date.Should().Be(D(2026, 12, 28));
        plan.Page(PageKey.WeekReflection(14)).Date.Should().Be(D(2027, 3, 29));
    }

    [Fact]
    public void Mon_Jan_4_carries_every_token_the_renderer_needs()
    {
        PageSpec page = Build(Q1_2027).Page(PageKey.Day(D(2027, 1, 4)));

        page.Tokens.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Qn"] = "Q1",
            ["YYYY"] = "2027",
            ["Ddd"] = "Mon",
            ["Mon"] = "Jan",
            ["D"] = "4",
            ["Month"] = "January",
            ["NN"] = "02",
            ["start"] = "Jan 4",
            ["end"] = "Jan 10",
            ["pdfPageName"] = "mon",
            ["dateIso"] = "2027-01-04",
            ["inQuarter"] = "true",
            ["quarterLength"] = "90",
            ["weekCount"] = "14",
            ["week"] = "2",
            ["weeksRemaining"] = "13",
            ["dayOfQuarter"] = "4",
            ["weekdayIndex"] = "0",
        });
    }

    [Fact]
    public void Tokens_on_non_day_pages_cover_the_quarter_the_date_and_the_pdf_page_name()
    {
        NotebookPlan plan = Build(Q1_2027);

        PageSpec overview = plan.Page(PageKey.WeekOverview(1));
        overview.Token(Tokens.Extra.PdfPageName).Should().Be("overview");
        overview.Token(Tokens.DayAbbrev).Should().Be("Mon");
        overview.Token(Tokens.Year).Should().Be("2026", "the Monday of week 1 is Dec 28, 2026");
        overview.IntToken(Tokens.Extra.WeeksRemaining).Should().Be(14);
        overview.Tokens.Should().NotContainKey(Tokens.Extra.DayOfQuarter);

        PageSpec cover = plan.Page(PageKey.Cover);
        cover.Token(Tokens.QuarterLabel).Should().Be("Q1");
        cover.Token(Tokens.Extra.PdfPageName).Should().Be("cover");
        cover.Token(Tokens.Extra.DateIso).Should().Be("2027-01-01");
        cover.Tokens.Should().NotContainKey(Tokens.WeekNumber);

        plan.Page(PageKey.QuarterlyReview).Token(Tokens.Extra.PdfPageName).Should().Be("quarterlyReview");
        plan.Page(PageKey.MonthlyReview(2027, 2)).Token(Tokens.MonthName).Should().Be("February");
        plan.Page(PageKey.Scorecard).Token(Tokens.Extra.PdfPageName).Should().Be("scorecard");
        plan.Page(PageKey.ObjectiveDetails).Token(Tokens.Extra.PdfPageName).Should().Be("blank");
        plan.Page(PageKey.Notes("Ideas")).Token(Tokens.Extra.PdfPageName).Should().Be("notes");
    }

    [Fact]
    public void Including_out_of_quarter_days_adds_four_to_week_1_and_four_to_week_14_dated_and_flagged()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { IncludeOutOfQuarterDays = true });

        DayPages(plan).Should().HaveCount(98);
        SectionSpec week1 = Section(plan, "Week 01 (Jan 1 to Jan 3)");
        week1.Pages.Where(p => p.Type == PageType.Day).Select(p => p.Title).Should().Equal(
            "Mon Dec 28", "Tue Dec 29", "Wed Dec 30", "Thu Dec 31", "Fri Jan 1", "Sat Jan 2", "Sun Jan 3");
        SectionSpec week14 = Section(plan, "Week 14 (Mar 29 to Mar 31)");
        week14.Pages.Where(p => p.Type == PageType.Day).Select(p => p.Title).Should().Equal(
            "Mon Mar 29", "Tue Mar 30", "Wed Mar 31", "Thu Apr 1", "Fri Apr 2", "Sat Apr 3", "Sun Apr 4");

        PageSpec dec28 = plan.Page(PageKey.Day(D(2026, 12, 28)));
        dec28.Date.Should().Be(D(2026, 12, 28));
        dec28.Token(Tokens.Extra.InQuarter).Should().Be("false");
        dec28.IntToken(Tokens.Extra.DayOfQuarter).Should().Be(-3);
        dec28.PdfPage.Should().Be(17, "the Monday page of run 1");
        dec28.Token(Tokens.Year).Should().Be("2026");

        PageSpec apr4 = plan.Page(PageKey.Day(D(2027, 4, 4)));
        apr4.Token(Tokens.Extra.InQuarter).Should().Be("false");
        apr4.IntToken(Tokens.Extra.DayOfQuarter).Should().Be(94);
        apr4.PdfPage.Should().Be(131, "the Sunday page of run 13");

        plan.Page(PageKey.Day(D(2027, 1, 1))).Token(Tokens.Extra.InQuarter).Should().Be("true");
        Section(plan, "Week 02 (Jan 4 to Jan 10)").Pages.Should().HaveCount(9, "full weeks are unchanged");
    }

    [Fact]
    public void Week_sections_are_tinted_by_the_month_of_their_first_quarter_day()
    {
        NotebookPlan plan = Build(Q1_2027);
        IReadOnlyList<string> tints = Shipped.Colors.SectionTints;

        Section(plan, "Week 01 (Jan 1 to Jan 3)").ColorHex.Should().Be(tints[0]);
        Section(plan, "Week 05 (Jan 25 to Jan 31)").ColorHex.Should().Be(tints[0]);
        Section(plan, "Week 06 (Feb 1 to Feb 7)").ColorHex.Should().Be(tints[1]);
        Section(plan, "Week 09 (Feb 22 to Feb 28)").ColorHex.Should().Be(tints[1]);
        Section(plan, "Week 10 (Mar 1 to Mar 7)").ColorHex.Should().Be(tints[2]);
        Section(plan, "Week 14 (Mar 29 to Mar 31)").ColorHex.Should().Be(tints[2]);
        plan.Sections.Where(s => !s.Name.StartsWith("Week ", StringComparison.Ordinal)).Should().OnlyContain(s => s.ColorHex == null);
    }

    [Fact]
    public void Section_tints_are_off_when_the_option_is_off()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { ColorSectionsByMonth = false });

        plan.Sections.Should().OnlyContain(s => s.ColorHex == null);
    }

    [Fact]
    public void Group_weeks_sets_the_group_name_on_the_week_sections_only()
    {
        NotebookPlan grouped = Build(Q1_2027, o => o with { GroupWeeks = true });
        NotebookPlan flat = Build(Q1_2027);

        grouped.Sections.Where(s => s.Name.StartsWith("Week ", StringComparison.Ordinal)).Should().HaveCount(14)
            .And.OnlyContain(s => s.GroupName == PlanBuilder.WeeksGroupName);
        grouped.Sections.Where(s => !s.Name.StartsWith("Week ", StringComparison.Ordinal)).Should().OnlyContain(s => s.GroupName == null);
        flat.Sections.Should().OnlyContain(s => s.GroupName == null);
        grouped.Sections.Select(s => s.Name).Should().Equal(flat.Sections.Select(s => s.Name));
        grouped.Pages.Should().HaveCount(flat.Pages.Count());
    }

    [Fact]
    public void A_custom_notebook_name_wins_over_the_pattern()
    {
        Build(Q1_2027, o => o with { NotebookName = "My planner" }).Name.Should().Be("My planner");
        Build(Q1_2027, o => o with { NotebookName = "  Padded  " }).Name.Should().Be("Padded");
        Build(Q1_2027, o => o with { NotebookName = "   " }).Name.Should().Be("SFP Q1 2027", "a blank override means no override");
        Build(new QuarterSpec(2027, 2)).Name.Should().Be("SFP Feb to Apr 2027");
    }

    [Fact]
    public void Off_cycle_quarter_titles_take_the_year_from_each_page_date()
    {
        NotebookPlan plan = Build(new QuarterSpec(2026, 12));

        plan.Name.Should().Be("SFP Dec to Feb 2026");
        plan.Page(PageKey.Calendar(2027, 2)).Title.Should().Be("February 2027");
        plan.Page(PageKey.QuarterlyReview).Title.Should().Be("Dec to Feb 2026 Review");
        plan.Page(PageKey.Day(D(2027, 1, 1))).Title.Should().Be("Fri Jan 1");
    }

    [Fact]
    public void Levels_come_from_the_template()
    {
        NotebookPlan plan = Build(Q1_2027);

        plan.Pages.Where(p => p.Type == PageType.Day).Should().OnlyContain(p => p.Level == 2);
        plan.Pages.Where(p => p.Type == PageType.Objective).Should().OnlyContain(p => p.Level == 2);
        plan.Page(PageKey.Annual(2)).Level.Should().Be(2);
        plan.Pages.Where(p => p.Type != PageType.Day && p.Type != PageType.Objective && p.Key != PageKey.Annual(2))
            .Should().OnlyContain(p => p.Level == 1);
    }

    [Fact]
    public void A_week_page_outside_an_each_week_section_is_a_manifest_error()
    {
        SectionTemplate loose = new()
        {
            Name = "Loose",
            Pages = new[] { new PageTemplate { Type = PageNames.Overview, Title = "Loose", Level = 1 } },
        };
        Manifest broken = Shipped with { Notebook = Shipped.Notebook with { Sections = new[] { loose } } };

        Action act = () => new PlanBuilder(Engine).Build(broken, Engine.Build(Q1_2027), new GenerateOptions { Start = Q1_2027 });

        act.Should().Throw<ManifestException>().WithMessage("*overview*eachWeek*");
    }

    [Fact]
    public void Links_are_empty_when_the_option_is_off()
    {
        Build(Q1_2027, o => o with { Links = false }).Links.Should().BeEmpty();
    }

    [Fact]
    public void Section_of_and_links_from_work_on_the_built_plan()
    {
        NotebookPlan plan = Build(Q1_2027);

        plan.SectionOf(PageKey.Day(D(2027, 1, 4))).Name.Should().Be("Week 02 (Jan 4 to Jan 10)");
        plan.SectionOf(PageKey.Scorecard).Name.Should().Be("Reviews");
    }
}
