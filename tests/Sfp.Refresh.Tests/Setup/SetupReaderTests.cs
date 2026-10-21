using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Setup;

/// <summary>M4.2: Setup slot rectangles to values; typed text wins over ink; AnyInked and InkedSlots; sources and the window.</summary>
public class SetupReaderTests
{
    private const string PageId = "{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{1}{E19512345678901234567890123456789012345678}";

    private static readonly Manifest M = RefreshManifest.Shipped;
    private static readonly SetupPageSpec S = M.Generated.Setup;

    private static PageXmlFixture Page() => new(PageId, "setup");

    private static SetupValues Read(PageXmlFixture page, IAnnualFallback? fallback = null) => new SetupReader(fallback).Read(page.Build(), M);

    [Fact]
    public void Typed_only_fixture_reads_every_slot_without_ink()
    {
        PageXmlFixture page = Page()
            .Typed(S.Objectives[0], "Ship the connector")
            .Typed(S.Objectives[7], "Write the book")
            .Typed(S.StandingGoals[0], "Walk 30 min")
            .Typed(S.MetricNames[1], "Calls made")
            .Typed(S.Habits[0], "Run")
            .Typed(S.Habits[15], "Read")
            .Typed(S.CalendarSources[0], "Work | https://example.com/cal.ics")
            .Typed(S.TimeBlockStart, "7")
            .Typed(S.TimeBlockEnd, "21");

        SetupValues v = Read(page);

        v.Objectives.Should().HaveCount(8);
        v.Objectives[0].Should().Be("Ship the connector");
        v.Objectives[7].Should().Be("Write the book");
        v.Objectives.Skip(1).Take(6).Should().AllSatisfy(o => o.Should().BeNull());
        v.StandingGoals.Should().Equal("Walk 30 min", null, null);
        v.MetricNames.Should().Equal(null, "Calls made", null);
        v.Habits.Should().HaveCount(16);
        v.Habits[0].Should().Be("Run");
        v.Habits[15].Should().Be("Read");
        v.Sources.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Label = "Work", Kind = CalendarSourceKind.IcsUrl, Location = "https://example.com/cal.ics" });
        v.TimeBlockWindow.Should().Be((7, 21));
        v.AnyInked.Should().BeFalse();
        v.InkedSlots.Should().BeEmpty();
        v.SourceProblems.Should().BeEmpty();
    }

    [Fact]
    public void Ink_only_fixture_reads_recognized_text_and_marks_every_inked_slot()
    {
        PageXmlFixture page = Page()
            .Inked(S.Objectives[1], "Hire", "two PMs")
            .Inked(S.StandingGoals[2], "Meditate")
            .Inked(S.Habits[3], "Stretch");

        SetupValues v = Read(page);

        v.Objectives[1].Should().Be("Hire two PMs");
        v.StandingGoals[2].Should().Be("Meditate");
        v.Habits[3].Should().Be("Stretch");
        v.AnyInked.Should().BeTrue();
        v.InkedSlots.Should().Equal("objective:2", "standingGoal:3", "habit:4");
    }

    [Fact]
    public void Mixed_fixture_lets_typed_text_win_over_ink_in_the_same_slot_and_marks_only_the_ink_slots()
    {
        PageXmlFixture page = Page()
            .Typed(S.Objectives[0], "Ship the connector")
            .Inked(S.Objectives[0], "Ship", "the", "connector", "now")
            .Inked(S.Objectives[1], "Hire", "two PMs")
            .Typed(S.MetricNames[0], "<span style='font-weight:bold'>Sales &amp; Marketing</span> calls");

        SetupValues v = Read(page);

        v.Objectives[0].Should().Be("Ship the connector", "typed text wins over ink in the same rectangle");
        v.Objectives[1].Should().Be("Hire two PMs");
        v.MetricNames[0].Should().Be("Sales & Marketing calls", "tags are stripped and entities decoded");
        v.AnyInked.Should().BeTrue();
        v.InkedSlots.Should().Equal("objective:2");
    }

    [Fact]
    public void Empty_fixture_reads_nothing_and_a_doodle_is_not_a_value()
    {
        PageXmlFixture page = Page().Drawing(new RectPt(300, 294, 40, 18));

        SetupValues v = Read(page);

        v.Should().BeEquivalentTo(SetupValues.Empty);
        v.TimeBlockWindow.Should().BeNull();
        v.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Every_manifest_rectangle_maps_to_its_own_slot_in_the_programmatic_fixture()
    {
        PageXmlFixture page = Page();
        for (int i = 0; i < S.Objectives.Count; i++)
        {
            page.Typed(S.Objectives[i], "Objective " + (i + 1));
        }

        for (int i = 0; i < S.StandingGoals.Count; i++)
        {
            page.Typed(S.StandingGoals[i], "Goal " + (i + 1));
        }

        for (int i = 0; i < S.MetricNames.Count; i++)
        {
            page.Typed(S.MetricNames[i], "Metric " + (i + 1));
        }

        for (int i = 0; i < S.Habits.Count; i++)
        {
            page.Typed(S.Habits[i], "Habit " + (i + 1));
        }

        for (int i = 0; i < S.CalendarSources.Count; i++)
        {
            page.Typed(S.CalendarSources[i], "Source " + (i + 1) + " | https://example.com/" + (i + 1) + ".ics");
        }

        SetupValues v = Read(page);

        v.Objectives.Should().Equal(Enumerable.Range(1, 8).Select(n => "Objective " + n));
        v.StandingGoals.Should().Equal("Goal 1", "Goal 2", "Goal 3");
        v.MetricNames.Should().Equal("Metric 1", "Metric 2", "Metric 3");
        v.Habits.Should().Equal(Enumerable.Range(1, 16).Select(n => "Habit " + n));
        v.Sources.Select(s => s.Label).Should().Equal("Source 1", "Source 2", "Source 3", "Source 4");
    }

    [Fact]
    public void Sheet_origin_is_subtracted_so_a_hidden_title_page_reads_the_same()
    {
        PageXmlFixture page = new PageXmlFixture(PageId, "setup", title: null, sheetY: 0).Typed(S.Objectives[2], "Third");

        Read(page).Objectives[2].Should().Be("Third");
    }

    [Fact]
    public void Multi_line_typed_text_is_collapsed_to_one_line()
    {
        PageXmlFixture page = Page().Typed(S.Objectives[0], "Ship the<br>connector  ");

        Read(page).Objectives[0].Should().Be("Ship the connector");
    }

    [Fact]
    public void One_two_paragraph_outline_over_objective_rows_1_and_2_reads_as_two_objectives()
    {
        RectPt rows1And2 = new(S.Objectives[0].X + 2, S.Objectives[0].Y + 2, S.Objectives[0].W - 4, S.Objectives[1].Bottom - S.Objectives[0].Y - 6);
        PageXmlFixture page = Page().Paragraphs(rows1And2, "Ship the connector", "Hire two PMs");

        SetupValues v = Read(page);

        v.Objectives[0].Should().Be("Ship the connector");
        v.Objectives[1].Should().Be("Hire two PMs", "each paragraph goes to its own row, not the whole outline to every row it overlaps");
        v.Objectives.Skip(2).Should().AllSatisfy(o => o.Should().BeNull());
        v.AnyInked.Should().BeFalse();
    }

    [Fact]
    public void An_outline_belongs_to_the_slot_holding_its_position_even_when_it_reaches_the_next_row()
    {
        RectPt tall = new(S.Objectives[0].X + 2, S.Objectives[0].Y + 2, S.Objectives[0].W - 4, 40);
        PageXmlFixture page = Page().Paragraphs(tall, "A long objective that wrapped onto a second line");

        SetupValues v = Read(page);

        v.Objectives[0].Should().Be("A long objective that wrapped onto a second line");
        v.Objectives[1].Should().BeNull("one paragraph is one value, read into the row where the outline starts");
    }

    [Fact]
    public void Lines_typed_in_one_outline_fill_the_rows_after_it_in_order_and_the_last_row_takes_any_left_over()
    {
        RectPt threeLines = new(S.Objectives[0].X + 2, S.Objectives[0].Y + 2, S.Objectives[0].W - 4, 44);
        RectPt fromGoal2 = new(S.StandingGoals[1].X + 2, S.StandingGoals[1].Y + 2, S.StandingGoals[1].W - 4, 50);
        PageXmlFixture page = Page()
            .Paragraphs(threeLines, "Ship the connector", "Hire two PMs", "Launch v2")
            .Paragraphs(fromGoal2, "Walk", "Read", "Stretch", "Sleep early");

        SetupValues v = Read(page);

        v.Objectives.Take(4).Should().Equal("Ship the connector", "Hire two PMs", "Launch v2", null);
        v.StandingGoals.Should().Equal(null, "Walk", "Read Stretch Sleep early");
    }

    [Fact]
    public void An_outline_whose_position_is_in_no_slot_goes_to_the_slot_it_overlaps_most()
    {
        RectPt mostlyRow2 = new(S.Objectives[0].X - 5, S.Objectives[1].Y - 8, 400, 20);
        PageXmlFixture page = Page().TypedAt(mostlyRow2, "Hire two PMs");

        SetupValues v = Read(page);

        v.Objectives[0].Should().BeNull();
        v.Objectives[1].Should().Be("Hire two PMs");
    }

    [Fact]
    public void Inked_lines_and_source_lines_are_handed_out_the_same_way_and_lines_inside_one_row_stay_together()
    {
        RectPt rows1And2 = new(S.Objectives[0].X + 2, S.Objectives[0].Y + 2, S.Objectives[0].W - 4, S.Objectives[1].Bottom - S.Objectives[0].Y - 6);
        RectPt sources1And2 = new(S.CalendarSources[0].X + 2, S.CalendarSources[0].Y + 2, S.CalendarSources[0].W - 4, S.CalendarSources[1].Bottom - S.CalendarSources[0].Y - 6);
        RectPt insideMetric1 = new(S.MetricNames[0].X + 2, S.MetricNames[0].Y + 2, S.MetricNames[0].W - 4, S.MetricNames[0].H - 4);
        PageXmlFixture page = Page()
            .InkedParagraphs(rows1And2, new[] { "Ship", "it" }, new[] { "Hire" })
            .Paragraphs(sources1And2, "Work | https://example.com/work.ics", "Home | https://example.com/home.ics")
            .Paragraphs(insideMetric1, "Calls", "made");

        SetupValues v = Read(page);

        v.Objectives.Take(3).Should().Equal("Ship it", "Hire", null);
        v.InkedSlots.Should().Equal("objective:1", "objective:2");
        v.Sources.Select(s => s.Label).Should().Equal("Work", "Home");
        v.MetricNames.Should().Equal("Calls made", null, null);
    }

    [Fact]
    public void Bad_source_lines_are_reported_and_skipped_and_the_window_needs_two_integers()
    {
        PageXmlFixture page = Page()
            .Typed(S.CalendarSources[0], "ftp://nope/cal.ics")
            .Typed(S.CalendarSources[1], "holidays")
            .Typed(S.TimeBlockStart, "six")
            .Typed(S.TimeBlockEnd, "22");

        SetupValues v = Read(page);

        v.Sources.Should().ContainSingle().Which.Kind.Should().Be(CalendarSourceKind.Holidays);
        v.SourceProblems.Should().ContainSingle().Which.Should().Contain("ftp://nope/cal.ics");
        v.TimeBlockWindow.Should().BeNull();
    }

    [Fact]
    public void Annual_fallback_fills_only_the_empty_objective_slots()
    {
        PageXmlFixture page = Page().Typed(S.Objectives[0], "Ship the connector");
        StubFallback fallback = new(new Dictionary<int, string?> { [1] = "ignored", [2] = "  Hire two PMs ", [5] = "Launch" });

        SetupValues v = Read(page, fallback);

        v.Objectives[0].Should().Be("Ship the connector");
        v.Objectives[1].Should().Be("Hire two PMs");
        v.Objectives[4].Should().Be("Launch");
        v.Objectives[2].Should().BeNull();
        fallback.Asked.Should().Equal(2, 3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void Annual_page_fallback_reads_typed_user_text_in_the_annual_row_but_never_tool_owned_or_inked_text()
    {
        AnnualTitleSlot slot = M.Slots.AnnualTitle;
        string owned = "{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{90}{B0}";
        PageXml annual1 = new PageXmlFixture(PageId, "annual:1", "Annual Objectives")
            .Typed(slot.Rect(0), "Propagated earlier", owned)
            .Typed(slot.Rect(1), "User typed this")
            .Inked(slot.Rect(2), "inked")
            .Build();
        PageXml annual2 = new PageXmlFixture(PageId, "annual:2", "Annual Objectives, continued")
            .Typed(slot.Rect(0), "Fifth block")
            .Build();
        Dictionary<string, PageXml> pages = new(StringComparer.Ordinal) { ["annual:1"] = annual1, ["annual:2"] = annual2 };
        AnnualPageFallback fallback = new(
            M,
            key => pages.TryGetValue(key.Value, out PageXml? p) ? p : null,
            key => key == PageKey.Annual(1) ? new[] { owned } : Array.Empty<string>());

        fallback.ObjectiveTitle(1).Should().BeNull("the only outline in the row is tool-owned");
        fallback.ObjectiveTitle(2).Should().Be("User typed this");
        fallback.ObjectiveTitle(3).Should().BeNull("ink is not typed text");
        fallback.ObjectiveTitle(4).Should().BeNull();
        fallback.ObjectiveTitle(5).Should().Be("Fifth block");
        fallback.ObjectiveTitle(9).Should().BeNull("there is no third annual page");
    }

    private sealed class StubFallback : IAnnualFallback
    {
        private readonly IReadOnlyDictionary<int, string?> titles;

        public StubFallback(IReadOnlyDictionary<int, string?> titles)
        {
            this.titles = titles;
        }

        public List<int> Asked { get; } = new();

        public string? ObjectiveTitle(int n)
        {
            Asked.Add(n);
            return titles.TryGetValue(n, out string? t) ? t : null;
        }
    }
}
