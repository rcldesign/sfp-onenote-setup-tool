using Sfp.App.ViewModels.Refresh;
using Sfp.Core.Dates;
using Sfp.Refresh;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-8: a run is about the notebook selected when it started (request and headline), the notebook list is
/// disabled while a run is on, and the selection is kept by sidecar across reloads, so two notebooks with one name in
/// different folders are never mixed up.
/// </summary>
public class RefreshRunTargetTests
{
    private const string Q1Path = @"C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027";
    private const string Q1OtherPath = @"D:\Notebooks\SFP Q1 2027";
    private const string Q4Path = @"D:\Notebooks\SFP Q4 2026";

    private readonly Harness _h = new();

    private static KnownNotebook Notebook(string name, string path, QuarterSpec quarter) => new(name, path, quarter, null, null, path + ".sfp-state.json");

    private static RefreshResult Refreshed() => new()
    {
        Success = true,
        ExitCode = 0,
        Summary = new RefreshSummary { Values = SetupValues.Empty, Changes = Array.Empty<PlannedChange>() },
    };

    [Fact]
    public async Task The_headline_names_the_notebook_the_run_started_on_and_the_list_is_locked_meanwhile()
    {
        using ManualResetEventSlim started = new(false);
        using ManualResetEventSlim release = new(false);
        _h.Registry.Notebooks.Add(Notebook("SFP Q1 2027", Q1Path, new QuarterSpec(2027, 1)));
        _h.Registry.Notebooks.Add(Notebook("SFP Q4 2026", Q4Path, new QuarterSpec(2026, 10)));
        _h.Refresher.Result = Refreshed();
        using RefreshViewModel vm = new(_h.Registry, _h.Tasks, _h.Logs, (_, _) => new GatedOrchestrator(_h.Refresher, started, release), _h.Dialogs, _h.Ui, _h.Env);
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;
        vm.SelectedNotebook!.Name.Should().Be("SFP Q1 2027");
        vm.CanSelectNotebook.Should().BeTrue();

        Task running = vm.PreviewChangesCommand.ExecuteAsync(null);
        started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        vm.CanSelectNotebook.Should().BeFalse("the list is disabled while a run is on");
        vm.SelectedNotebook = vm.Notebooks[1];
        release.Set();
        await running;

        _h.Refresher.LastRequest!.NotebookName.Should().Be("SFP Q1 2027");
        vm.Summary.Should().NotBeNull();
        vm.ProgressText.Should().Be("Preview complete.");
        vm.CanSelectNotebook.Should().BeTrue();

        vm.SelectedNotebook = vm.Notebooks[0];
        started.Reset();
        release.Reset();
        Task refreshing = vm.RefreshNowCommand.ExecuteAsync(null);
        started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        vm.SelectedNotebook = vm.Notebooks[1];
        release.Set();
        await refreshing;

        vm.Summary!.Headline.Should().Be("Refreshed SFP Q1 2027.", "the headline names the notebook that was refreshed, not the one selected at the end");
    }

    [Fact]
    public async Task Two_notebooks_with_one_name_keep_their_own_selection_across_the_reload_after_a_run()
    {
        _h.Registry.Notebooks.Add(Notebook("SFP Q1 2027", Q1Path, new QuarterSpec(2027, 1)));
        _h.Registry.Notebooks.Add(Notebook("SFP Q1 2027", Q1OtherPath, new QuarterSpec(2027, 1)));
        _h.Refresher.Result = Refreshed();
        using RefreshViewModel vm = _h.NewRefresh();
        vm.SetKnownFolders(new[] { @"D:\Notebooks" });
        await vm.PendingReload;
        vm.SelectedNotebook = vm.Notebooks.Single(n => n.Path == Q1OtherPath);

        await vm.RefreshNowCommand.ExecuteAsync(null);

        _h.Refresher.LastRequest!.NotebooksFolder.Should().Be(@"D:\Notebooks");
        _h.Registry.Queries.Should().HaveCount(2, "the run reloaded the list");
        vm.SelectedNotebook!.Path.Should().Be(Q1OtherPath, "the selection follows the sidecar, not the first notebook with that name");
        vm.Summary!.Headline.Should().Be("Refreshed SFP Q1 2027.");
        vm.ToRequest(dryRun: false)!.NotebooksFolder.Should().Be(@"D:\Notebooks", "the next Refresh now targets the same folder");
    }

    /// <summary>Waits for the test before running the inner orchestrator, so the test can act while the run is on.</summary>
    private sealed class GatedOrchestrator : IRefreshOrchestrator
    {
        private readonly IRefreshOrchestrator _inner;
        private readonly ManualResetEventSlim _started;
        private readonly ManualResetEventSlim _release;

        public GatedOrchestrator(IRefreshOrchestrator inner, ManualResetEventSlim started, ManualResetEventSlim release)
        {
            _inner = inner;
            _started = started;
            _release = release;
        }

        public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            _started.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The test never released the run.");
            }

            return _inner.Run(request, progress, cancellationToken);
        }
    }
}
