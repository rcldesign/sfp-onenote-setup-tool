using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Propagation;

/// <summary>
/// M4.4: the propagator over the Q1 2027 plan with pages built from fixtures: 8 objectives produce the PRD counts, a user
/// outline in the band blocks that page's legend with a reason, and a second plan on the same values is empty.
/// </summary>
public class PropagatorTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));

    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private static readonly string?[] EightObjectives =
    {
        "Ship the connector", "Hire two PMs", "Launch v2", "Cut churn", "Open Berlin", "Close series B", "Refresh the brand", "Write the book",
    };

    private static SetupValues Values(string?[]? objectives = null, string?[]? goals = null, string?[]? metrics = null, string?[]? habits = null, IReadOnlyList<string>? inked = null) =>
        new(objectives ?? new string?[8], goals ?? new string?[3], metrics ?? new string?[3], habits ?? new string?[16], Array.Empty<CalendarSource>(), inked is { Count: > 0 })
        {
            InkedSlots = inked ?? Array.Empty<string>(),
        };

    [Fact]
    public void Eight_objectives_produce_legends_on_every_overview_and_day_plus_annual_rows_headings_and_scorecard_rows()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        Propagator propagator = new(nb.Catalog, nb.Read, Mono);

        IReadOnlyList<PlannedChange> changes = propagator.Plan(nb.Plan, Values(EightObjectives), nb.Sidecar);

        changes.Should().AllSatisfy(c =>
        {
            c.Kind.Should().Be(ChangeKind.Create);
            c.Blocked.Should().BeFalse();
            c.OldText.Should().BeNull();
            c.Inked.Should().BeFalse();
        });
        ILookup<string, PlannedChange> bySlot = changes.ToLookup(c => PropagationSlots.Split(c.SlotId).Prefix, StringComparer.Ordinal);
        bySlot["objectiveLegend"].Should().HaveCount(14 + 90, "14 overviews and 90 days");
        bySlot["objectiveLegend"].Select(c => nb.Plan.Page(c.Page).Type).Distinct().Should().BeEquivalentTo(new[] { PageType.WeeklyOverview, PageType.Day });
        bySlot["annualRow"].Should().HaveCount(8);
        bySlot["annualRow"].Where(c => c.Page == PageKey.Annual(1)).Select(c => c.SlotId).Should().Equal("annualRow:1", "annualRow:2", "annualRow:3", "annualRow:4");
        bySlot["annualRow"].Where(c => c.Page == PageKey.Annual(2)).Select(c => c.NewText).Should().Equal("Open Berlin", "Close series B", "Refresh the brand", "Write the book");
        bySlot["objectiveHeading"].Should().HaveCount(8);
        bySlot["objectiveHeading"].Single(c => c.Page == PageKey.Objective(3)).NewText.Should().Be("Launch v2");
        bySlot["scorecardRow"].Should().HaveCount(8).And.AllSatisfy(c => c.Page.Should().Be(PageKey.Scorecard));
        changes.Should().HaveCount(104 + 8 + 8 + 8);
        string legend = changes.Single(c => c.Page == MonJan4).NewText;
        legend.Should().StartWith("O1 Ship th", "the legend lists the objectives in order, each cut to its share of the band");
        legend.Split("  ").Should().HaveCount(8).And.SatisfyRespectively(
            e => e.Should().StartWith("O1 "), e => e.Should().StartWith("O2 "), e => e.Should().StartWith("O3 "), e => e.Should().StartWith("O4 "),
            e => e.Should().StartWith("O5 "), e => e.Should().StartWith("O6 "), e => e.Should().StartWith("O7 "), e => e.Should().StartWith("O8 "));
        changes.Select(c => c.Page).Distinct().Count().Should().Be(104 + 2 + 8 + 1);
    }

    [Fact]
    public void Standing_goals_metric_names_and_habits_go_to_every_day_and_calendar_page()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        Propagator propagator = new(nb.Catalog, nb.Read, Mono);
        string?[] habits = new string?[16];
        habits[0] = "Run";
        habits[8] = "Read";

        IReadOnlyList<PlannedChange> changes = propagator.Plan(nb.Plan, Values(goals: new[] { "Walk 30 min", null, "Inbox zero" }, metrics: new[] { null, "Calls", null }, habits: habits), nb.Sidecar);

        changes.Where(c => c.SlotId == "standingGoal:1").Should().HaveCount(90).And.AllSatisfy(c => c.NewText.Should().Be("Walk 30 min"));
        changes.Where(c => c.SlotId == "standingGoal:3").Should().HaveCount(90);
        changes.Should().NotContain(c => c.SlotId == "standingGoal:2", "an empty Setup slot propagates nothing");
        changes.Where(c => c.SlotId == "metricName:2").Should().HaveCount(90).And.AllSatisfy(c => c.NewText.Should().Be("Calls"));
        changes.Where(c => c.SlotId == "habitLegend").Should().HaveCount(3).And.AllSatisfy(c => c.NewText.Should().Be("1 Run  9 Read"));
        changes.Should().NotContain(c => c.SlotId == "objectiveLegend");
    }

    [Fact]
    public void A_user_outline_in_the_band_blocks_that_pages_legend_with_a_reason_and_nothing_else()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        nb.TypeUserText(MonJan4, M.Slots.ObjectiveLegend.Rect, "my note");
        Propagator propagator = new(nb.Catalog, nb.Read, Mono);

        IReadOnlyList<PlannedChange> changes = propagator.Plan(nb.Plan, Values(EightObjectives), nb.Sidecar);

        PlannedChange blocked = changes.Single(c => c.Blocked);
        blocked.Page.Should().Be(MonJan4);
        blocked.SlotId.Should().Be("objectiveLegend");
        blocked.Kind.Should().Be(ChangeKind.Create);
        blocked.Reason.Should().Be("A user object overlaps the slot: text 'my note'.");
        changes.Count(c => c.SlotId == "objectiveLegend" && !c.Blocked).Should().Be(103);
    }

    [Fact]
    public void A_second_plan_on_the_same_values_is_empty_and_a_changed_value_updates_in_place()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        Propagator first = new(nb.Catalog, nb.Read, Mono);
        IReadOnlyList<PlannedChange> changes = first.Plan(nb.Plan, Values(EightObjectives, goals: new[] { "Walk", null, null }), nb.Sidecar);
        WrittenNotebook written = WrittenNotebook.Apply(nb, changes);

        Propagator second = new(nb.Catalog, written.Read, Mono);
        second.Plan(nb.Plan, Values(EightObjectives, goals: new[] { "Walk", null, null }), written.Sidecar).Should().BeEmpty();

        string?[] changed = EightObjectives.ToArray();
        changed[1] = "Hire three PMs";
        IReadOnlyList<PlannedChange> third = second.Plan(nb.Plan, Values(changed, goals: new[] { "Walk", null, null }), written.Sidecar);
        third.Should().HaveCount(104 + 1 + 1 + 1, "every legend, annual row 2, objective 2's heading and scorecard row 2");
        third.Should().AllSatisfy(c =>
        {
            c.Kind.Should().Be(ChangeKind.Update);
            c.Blocked.Should().BeFalse();
            c.OldText.Should().NotBeNull();
        });
        PlannedChange row = third.Single(c => c.SlotId == "annualRow:2");
        row.OldText.Should().Be("Hire two PMs");
        row.NewText.Should().Be("Hire three PMs");
    }

    [Fact]
    public void A_cleared_setup_slot_removes_the_tool_owned_object_and_a_moved_one_is_reported()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        Propagator first = new(nb.Catalog, nb.Read, Mono);
        WrittenNotebook written = WrittenNotebook.Apply(nb, first.Plan(nb.Plan, Values(goals: new[] { "Walk", "Read", null }), nb.Sidecar));
        written.Move(MonJan4, "standingGoal:2", new RectPt(500, 400, 100, 14));

        Propagator second = new(nb.Catalog, written.Read, Mono);
        IReadOnlyList<PlannedChange> changes = second.Plan(nb.Plan, Values(goals: new[] { "Walk", null, null }), written.Sidecar);

        changes.Should().HaveCount(90).And.AllSatisfy(c => c.SlotId.Should().Be("standingGoal:2"));
        changes.Where(c => c.Page != MonJan4).Should().AllSatisfy(c =>
        {
            c.Kind.Should().Be(ChangeKind.Remove);
            c.Blocked.Should().BeFalse();
            c.OldText.Should().Be("Read");
            c.NewText.Should().BeEmpty();
        });
        PlannedChange moved = changes.Single(c => c.Page == MonJan4);
        moved.Blocked.Should().BeTrue();
        moved.Reason.Should().Be(Propagator.MovedReason);
    }

    [Fact]
    public void Inked_values_mark_their_changes_and_a_page_that_cannot_be_read_is_skipped_with_a_reason()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string?[] objectives = { "Ship the connector", "Hire two PMs", null, null, null, null, null, null };
        Propagator propagator = new(nb.Catalog, key => key == MonJan4 ? null : nb.Read(key), Mono);

        IReadOnlyList<PlannedChange> changes = propagator.Plan(nb.Plan, Values(objectives, goals: new[] { "Walk", null, null }, inked: new[] { "objective:2", "standingGoal:1" }), nb.Sidecar);

        changes.Where(c => c.SlotId == "objectiveLegend").Should().AllSatisfy(c => c.Inked.Should().BeTrue("one of the legend's objectives is inked"));
        changes.Single(c => c.SlotId == "annualRow:1").Inked.Should().BeFalse();
        changes.Single(c => c.SlotId == "annualRow:2").Inked.Should().BeTrue();
        changes.Single(c => c.Page == PageKey.Objective(2)).Inked.Should().BeTrue();
        changes.Where(c => c.SlotId == "standingGoal:1").Should().AllSatisfy(c => c.Inked.Should().BeTrue());
        IEnumerable<PlannedChange> unreadable = changes.Where(c => c.Page == MonJan4);
        unreadable.Should().HaveCount(2).And.AllSatisfy(c =>
        {
            c.Blocked.Should().BeTrue();
            c.Reason.Should().Be(Propagator.UnreadablePageReason);
        });
    }

    [Fact]
    public void Without_confirmation_the_legends_are_built_from_the_typed_values_and_are_not_inked()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string?[] objectives = { "Ship the connector", "Hire two PMs", "Launch v2", null, null, null, null, null };
        string?[] habits = new string?[16];
        habits[0] = "Run";
        habits[1] = "Read";
        SetupValues v = Values(objectives, habits: habits, inked: new[] { "objective:2", "habit:2" });
        Propagator propagator = new(nb.Catalog, nb.Read, Mono);

        IReadOnlyList<PlannedChange> unconfirmed = propagator.Plan(nb.Plan, v, nb.Sidecar, writeInkedValues: false);

        unconfirmed.Where(c => c.SlotId == "objectiveLegend").Should().HaveCount(104).And.AllSatisfy(c =>
        {
            c.NewText.Should().Be("O1 Ship the connector  O3 Launch v2", "the inked objective is left out and the others keep their numbers");
            c.Inked.Should().BeFalse();
        });
        unconfirmed.Where(c => c.SlotId == "habitLegend").Should().HaveCount(3).And.AllSatisfy(c =>
        {
            c.NewText.Should().Be("1 Run");
            c.Inked.Should().BeFalse();
        });
        unconfirmed.Single(c => c.SlotId == "annualRow:2").Should().Match<PlannedChange>(c => c.Inked && c.NewText == "Hire two PMs", "the inked value is still listed in its own slot");

        IReadOnlyList<PlannedChange> confirmed = propagator.Plan(nb.Plan, v, nb.Sidecar, writeInkedValues: true);

        confirmed.Where(c => c.SlotId == "objectiveLegend").Should().AllSatisfy(c =>
        {
            c.NewText.Should().Be("O1 Ship the connector  O2 Hire two PMs  O3 Launch v2");
            c.Inked.Should().BeTrue("the legend includes an inked value");
        });
        propagator.Plan(nb.Plan, v, nb.Sidecar).Should().BeEquivalentTo(confirmed, "the three-argument plan writes every value");
        propagator.Plan(nb.Plan, Values(objectives), nb.Sidecar, writeInkedValues: false).Where(c => c.SlotId == "objectiveLegend")
            .Should().AllSatisfy(c => c.NewText.Should().Contain("O2 Hire two PMs"), "typed values are always in the legend");
    }

    [Fact]
    public void Decide_covers_every_branch()
    {
        PageKey page = MonJan4;
        SlotState empty = new(null, false, Array.Empty<Intruder>());
        SlotState blockedEmpty = new(null, false, new[] { new Intruder("{x}", "text 'note'") });

        Propagator.Decide(page, "s", "", empty).Should().BeNull();
        Propagator.Decide(page, "s", "new", empty).Should().Be(new PlannedChange(page, "s", null, "new", ChangeKind.Create, false, null));
        Propagator.Decide(page, "s", "new", blockedEmpty)!.Reason.Should().Be("A user object overlaps the slot: text 'note'.");

        OutlineInfo owned = new PageXmlFixture("{p}{1}{B0}", "day:2027-01-04").TypedAt(new RectPt(10, 10, 50, 10), "old").Build().Outlines.Last();
        SlotState held = new(owned, false, Array.Empty<Intruder>());
        SlotState heldBlocked = new(owned, false, new[] { new Intruder("{x}", "ink") });
        SlotState movedAway = new(owned, true, Array.Empty<Intruder>());

        Propagator.Decide(page, "s", "old", held).Should().BeNull("idempotent");
        Propagator.Decide(page, "s", " old ", held).Should().BeNull("trimmed");
        Propagator.Decide(page, "s", "old", heldBlocked).Should().Be(
            new PlannedChange(page, "s", "old", "old", ChangeKind.Update, true, "A user object overlaps the slot: ink. The tool's text there is already current and was left as it is."),
            "a user object over current tool text is reported, though nothing is written");
        Propagator.Decide(page, "s", "new", held).Should().Be(new PlannedChange(page, "s", "old", "new", ChangeKind.Update, false, null));
        Propagator.Decide(page, "s", "", held).Should().Be(new PlannedChange(page, "s", "old", "", ChangeKind.Remove, false, null));
        Propagator.Decide(page, "s", "", heldBlocked)!.Blocked.Should().BeFalse("removing the tool's own object touches nothing of the user's");
        Propagator.Decide(page, "s", "new", heldBlocked)!.Blocked.Should().BeTrue();
        Propagator.Decide(page, "s", "new", movedAway).Should().Be(new PlannedChange(page, "s", "old", "new", ChangeKind.Update, true, Propagator.MovedReason));
        Propagator.Decide(page, "s", "old", movedAway).Should().BeNull();

        Propagator.Decide(page, "s", "new", held, toolText: "old").Should().Be(new PlannedChange(page, "s", "old", "new", ChangeKind.Update, false, null), "the outline still says what the tool wrote");
        Propagator.Decide(page, "s", "new", held, toolText: "older").Should().Be(new PlannedChange(page, "s", "old", "new", ChangeKind.Update, true, Propagator.EditedReason), "the user typed into the tool's outline");
        Propagator.Decide(page, "s", "", held, toolText: "older").Should().Be(new PlannedChange(page, "s", "old", "", ChangeKind.Remove, true, Propagator.EditedReason), "text the user typed is never deleted either");
        Propagator.Decide(page, "s", "old", held, toolText: "older").Should().BeNull("the slot already says what Setup asks for");
        Propagator.Decide(page, "s", "new", held, toolText: null)!.Blocked.Should().BeFalse("a sidecar written before the texts were recorded skips the check");
        Propagator.IsEdited("O1 a  O2 b", "O1 a O2 b").Should().BeFalse("runs of whitespace compare equal");
        Propagator.IsEdited("1 Run\n2 Read", "1 Run  2 Read").Should().BeFalse();
        Propagator.IsEdited("Walk, then stretch", "Walk").Should().BeTrue();
        Propagator.IsEdited("anything", null).Should().BeFalse();
    }
}
