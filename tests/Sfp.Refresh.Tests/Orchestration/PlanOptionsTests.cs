using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-12): Refresh rebuilds the notebook plan with the Generate options the sidecar records, so pages that
/// exist only because of an option (the out-of-quarter days) get propagation; a sidecar without the block keeps the
/// default options, as before.
/// </summary>
public class PlanOptionsTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;
    private static readonly SetupPageSpec S = M.Generated.Setup;

    /// <summary>The days of week 1 and week 13 outside Q1 2027.</summary>
    private static readonly string[] OutOfQuarterDays =
    {
        "day:2026-12-28", "day:2026-12-29", "day:2026-12-30", "day:2026-12-31",
        "day:2027-04-01", "day:2027-04-02", "day:2027-04-03", "day:2027-04-04",
    };

    [Fact]
    public void A_sidecar_recording_out_of_quarter_days_plans_and_writes_the_8_extra_day_pages()
    {
        RefreshHarness h = new(shape: o => o with { IncludeOutOfQuarterDays = true });
        h.Notebook.Plan.Pages.Select(p => p.Key.Value).Should().Contain(OutOfQuarterDays, "the notebook was generated with them");
        h.SavedSidecar().PlanOptions!.IncludeOutOfQuarterDays.Should().BeTrue();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Type(S.StandingGoals[0], "Walk 30 min");

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        RefreshSummary summary = result.Summary!;
        foreach (string key in OutOfQuarterDays)
        {
            summary.Changes.Should().Contain(c => c.Page.Value == key && c.SlotId == "objectiveLegend" && !c.Blocked, key);
            summary.Changes.Should().Contain(c => c.Page.Value == key && c.SlotId == "standingGoal:1" && !c.Blocked, key);
            summary.PageTitles.Should().ContainKey(key);
            h.Notebook.Read(new PageKey(key)).Outlines.Select(o => o.PlainText).Should().Contain(new[] { "O1 Ship the connector", "Walk 30 min" }, key);
        }

        summary.Changes.Count(c => c.SlotId == "objectiveLegend").Should().Be(104 + 8);
        h.Run().Summary!.Changes.Should().BeEmpty("the next run finds the extra pages current too");
    }

    [Fact]
    public void A_sidecar_without_the_options_block_keeps_the_default_plan()
    {
        RefreshHarness h = new(shape: o => o with { IncludeOutOfQuarterDays = true });
        h.Store.Save(h.SidecarPath, h.Notebook.Sidecar with { PlanOptions = null });
        h.Type(S.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run(dryRun: true);

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.Changes.Should().NotContain(c => OutOfQuarterDays.Contains(c.Page.Value), "a sidecar written before the options were recorded plans with the defaults");
        result.Summary.Changes.Count(c => c.SlotId == "objectiveLegend").Should().Be(104);
    }

    [Fact]
    public void A_rebuild_keeps_the_recorded_options_and_refinds_the_anchors_of_the_extra_day_pages()
    {
        FakeNotebook nb = FakeNotebook.Generate(shape: o => o with { IncludeOutOfQuarterDays = true });
        nb.Sidecar.Pages["day:2026-12-28"].Anchors.Should().NotBeEmpty("Generate wrote the day's link anchors");
        string movedId = nb.App.RenumberIds(nb.NotebookId);
        DateEngine dates = new();

        RecoveryResult result = new SidecarRecovery(nb.App, M, dates, new PlanBuilder(dates), new CapturingLog().Logger)
            .Recover(new NotebookHandle(movedId, nb.NotebookPath, nb.Plan.Name), nb.Sidecar, "1.0.0");

        result.Rebuilt.Should().BeTrue();
        result.Sidecar.PlanOptions.Should().BeEquivalentTo(nb.Sidecar.PlanOptions, "a rebuild keeps the options of the stale sidecar");
        foreach (string key in OutOfQuarterDays)
        {
            result.Sidecar.Pages[key].Anchors.Keys.Should().BeEquivalentTo(nb.Sidecar.Pages[key].Anchors.Keys, key);
        }
    }

    [Fact]
    public void The_recorded_options_rebuild_the_generated_plan()
    {
        DateEngine dates = new();
        QuarterSpec spec = new(2027, 1);
        Quarter quarter = dates.Build(spec);
        Sfp.Core.Options.GenerateOptions generated = new() { Start = spec, IncludeOutOfQuarterDays = true, ReflectionDateIsWeekStart = true, NotesPages = new[] { "Backlog", "Someday" } };

        NotebookPlan rebuilt = new PlanBuilder(dates).Build(M, quarter, SidecarPlanOptions.ToGenerateOptions(spec, SidecarPlanOptions.From(generated)));
        NotebookPlan old = new PlanBuilder(dates).Build(M, quarter, SidecarPlanOptions.ToGenerateOptions(spec, null));

        rebuilt.Pages.Select(p => (p.Key, p.Title)).Should().Equal(new PlanBuilder(dates).Build(M, quarter, generated).Pages.Select(p => (p.Key, p.Title)));
        rebuilt.Pages.Should().HaveCount(old.Pages.Count() + 8 - 2, "8 out-of-quarter days, and two Notes pages in place of the default four");
        rebuilt.Pages.Should().Contain(p => p.Key == PageKey.Notes("Someday"));
        old.Pages.Should().Contain(p => p.Key == PageKey.Notes("Contacts"));
    }
}
