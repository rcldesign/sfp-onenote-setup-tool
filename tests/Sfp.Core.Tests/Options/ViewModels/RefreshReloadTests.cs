using System.Collections.Specialized;
using Sfp.App.ViewModels.Refresh;
using Sfp.Core.Dates;
using Sfp.Core.Plan;
using Sfp.Refresh;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-1: the reload after Refresh now keeps the run's summary, Setup values and changes, although the
/// registry hands back a new record (the run saved a new Last refreshed in the sidecar), and although a WPF list pushes
/// a null selection while it is cleared. The results are cleared only when the selection lands on another notebook.
/// </summary>
public class RefreshReloadTests
{
    private const string Q1Path = @"C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027";
    private const string Q4Path = @"D:\Notebooks\SFP Q4 2026";

    private static readonly DateTimeOffset RefreshedAt = new(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);

    private readonly Harness _h = new();

    private static KnownNotebook Q1(DateTimeOffset? lastRefresh) =>
        new("SFP Q1 2027", Q1Path, new QuarterSpec(2027, 1), new DateTimeOffset(2026, 12, 20, 9, 30, 0, TimeSpan.Zero), lastRefresh, Q1Path + ".sfp-state.json");

    private static KnownNotebook Q4 => new("SFP Q4 2026", Q4Path, new QuarterSpec(2026, 10), null, null, Q4Path + ".sfp-state.json");

    private static RefreshResult Refreshed() => new()
    {
        Success = true,
        ExitCode = 0,
        Elapsed = TimeSpan.FromSeconds(30),
        Summary = new RefreshSummary
        {
            Values = new SetupValues(
                new string?[] { "Ship the connector", null, null, null, null, null, null, null },
                new string?[3],
                new string?[3],
                new string?[16],
                Array.Empty<CalendarSource>(),
                AnyInked: false),
            Changes = new[]
            {
                new PlannedChange(PageKey.Objective(1), "objectiveHeading", null, "Ship the connector", ChangeKind.Create, false, null),
                new PlannedChange(PageKey.Day(new DateOnly(2027, 1, 4)), "standingGoal:1", "Old", "Inbox zero", ChangeKind.Update, true, "a user outline sits in the slot"),
            },
            PagesTouched = 1,
            ValuesWritten = 1,
            EventsWritten = 4,
            Skipped = 1,
        },
    };

    /// <summary>A Refresh tab whose runs save the new Last refreshed of Q1 in its sidecar, as RefreshOrchestrator does after writing.</summary>
    private RefreshViewModel NewRefreshThatSavesTheSidecar() => new(
        _h.Registry,
        _h.Tasks,
        _h.Logs,
        (_, _) => new SidecarSavingOrchestrator(_h.Refresher, () => _h.Registry.Notebooks[_h.Registry.Notebooks.FindIndex(n => n.Name == "SFP Q1 2027")] = Q1(RefreshedAt)),
        _h.Dialogs,
        _h.Ui,
        _h.Env);

    [Fact]
    public async Task Refresh_now_keeps_the_runs_results_through_the_reload_that_shows_the_new_time()
    {
        _h.Registry.Notebooks.Add(Q1(null));
        _h.Refresher.Result = Refreshed();
        using RefreshViewModel vm = NewRefreshThatSavesTheSidecar();
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;
        vm.SelectedNotebook!.LastRefreshText.Should().Be("never");

        await vm.RefreshNowCommand.ExecuteAsync(null);

        _h.Registry.Queries.Should().HaveCount(2, "the run reloads the list");
        vm.Notebooks.Should().ContainSingle().Which.LastRefreshText.Should().Be("2026-10-21 05:00");
        vm.SelectedNotebook!.LastRefreshText.Should().Be("2026-10-21 05:00");
        vm.Summary.Should().NotBeNull("the PRD Refresh screen shows the summary after a run");
        vm.Summary!.Headline.Should().Be("Refreshed SFP Q1 2027.");
        vm.Summary.EventsWritten.Should().Be(4);
        vm.Summary.Skipped.Should().Be(1);
        vm.SetupLines.Should().Contain("Objective 1: Ship the connector");
        vm.Changes.Should().HaveCount(2);
        vm.ChangesHeading.Should().Be("2 changes planned (1 blocked)");
        vm.ProgressText.Should().Be("Refresh complete.");
    }

    [Fact]
    public async Task The_null_a_list_view_pushes_while_its_items_are_cleared_does_not_clear_the_results()
    {
        _h.Registry.Notebooks.Add(Q1(null));
        _h.Refresher.Result = Refreshed();
        using RefreshViewModel vm = NewRefreshThatSavesTheSidecar();
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;
        vm.Notebooks.CollectionChanged += (_, e) =>
        {
            // A WPF ListView bound two-way to SelectedNotebook does this when its items are reset.
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                vm.SelectedNotebook = null;
            }
        };

        await vm.RefreshNowCommand.ExecuteAsync(null);

        vm.SelectedNotebook!.Name.Should().Be("SFP Q1 2027");
        vm.Summary.Should().NotBeNull();
        vm.SetupLines.Should().NotBeEmpty();
        vm.Changes.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_reload_that_lands_on_another_notebook_clears_the_results()
    {
        _h.Registry.Notebooks.Add(Q1(null));
        _h.Registry.Notebooks.Add(Q4);
        _h.Refresher.Result = Refreshed();
        using RefreshViewModel vm = _h.NewRefresh();
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;
        await vm.PreviewChangesCommand.ExecuteAsync(null);
        vm.Summary.Should().NotBeNull();

        _h.Registry.Notebooks.RemoveAt(0);
        await vm.ReloadNotebooksCommand.ExecuteAsync(null);

        vm.SelectedNotebook!.Name.Should().Be("SFP Q4 2026");
        vm.Summary.Should().BeNull("the results were about SFP Q1 2027, which is gone");
        vm.Changes.Should().BeEmpty();
        vm.SetupLines.Should().BeEmpty();
    }

    [Fact]
    public void The_same_notebook_is_the_same_sidecar_file_or_the_same_name_without_one()
    {
        KnownNotebookItem q1 = new(Q1(null));

        RefreshViewModel.IsSameNotebook(q1, new KnownNotebookItem(Q1(RefreshedAt))).Should().BeTrue("a new Last refreshed is the same notebook");
        RefreshViewModel.IsSameNotebook(q1, new KnownNotebookItem(Q1(null) with { SidecarPath = Q1Path.ToUpperInvariant() + ".SFP-STATE.JSON" })).Should().BeTrue();
        RefreshViewModel.IsSameNotebook(q1, new KnownNotebookItem(Q1(null) with { SidecarPath = @"D:\Notebooks\SFP Q1 2027.sfp-state.json" })).Should().BeFalse("same name, other folder");
        RefreshViewModel.IsSameNotebook(new KnownNotebookItem(Q1(null) with { SidecarPath = "" }), q1).Should().BeTrue("no sidecar path: the name decides");
        RefreshViewModel.IsSameNotebook(null, null).Should().BeTrue();
        RefreshViewModel.IsSameNotebook(q1, null).Should().BeFalse();
    }

    /// <summary>Runs the inner orchestrator, then saves the sidecar the way a real run does (the registry then reads a new record).</summary>
    private sealed class SidecarSavingOrchestrator : IRefreshOrchestrator
    {
        private readonly IRefreshOrchestrator _inner;
        private readonly Action _saveSidecar;

        public SidecarSavingOrchestrator(IRefreshOrchestrator inner, Action saveSidecar)
        {
            _inner = inner;
            _saveSidecar = saveSidecar;
        }

        public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            RefreshResult result = _inner.Run(request, progress, cancellationToken);
            if (result.Success && !request.DryRun)
            {
                _saveSidecar();
            }

            return result;
        }
    }
}
