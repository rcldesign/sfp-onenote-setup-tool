using System.Globalization;
using Serilog.Events;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-4 c and the integration rule): every sidecar save of a run (the rebuilt sidecar, the journal before
/// the first write, the best-effort save after a failure, the final save) is tried three times with short waits, since a
/// sync client or a virus scanner may hold the file. A final save that fails every attempt fails the run (exit 5) naming
/// the path and saying the next run re-adopts what it wrote; a save before the first write that fails every attempt ends
/// the run (exit 5) before anything is written.
/// </summary>
public class SidecarSaveTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
    private static readonly TimeSpan[] Waits = { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500) };

    [Fact]
    public void A_save_that_fails_twice_is_tried_again_and_the_run_succeeds()
    {
        RefreshHarness h = new();
        FlakySidecarStore store = new(h.Store);
        store.FailNext(2, FlakySidecarStore.Final);
        h.Services = h.Services with { Sidecars = store };
        h.Type(S.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0);
        store.Failed.Should().Be(2);
        store.Saves.Should().Be(4, "the journal is saved at once and the final save on its third attempt");
        h.Sleeps.Should().Equal(Waits);
        h.SavedSidecar().Pages[MonJan4.Value].Anchors.Should().ContainKey("objectiveLegend", "the third attempt saved what the run wrote");
        h.SavedSidecar().Pages[MonJan4.Value].PendingTexts.Should().BeNull("every write was recorded, so its journal entry is gone");
        h.Log.Messages(LogEventLevel.Warning).Where(m => m.Contains("trying again", StringComparison.Ordinal)).Should().HaveCount(2);
        h.Run().Summary!.Changes.Should().BeEmpty();
    }

    [Fact]
    public void A_save_that_fails_three_times_fails_the_run_naming_the_path()
    {
        RefreshHarness h = new();
        FlakySidecarStore store = new(h.Store);
        store.FailNext(3, FlakySidecarStore.Final);
        h.Services = h.Services with { Sidecars = store };
        h.Type(S.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run();

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
        result.Error.Should().Be(string.Format(CultureInfo.InvariantCulture, RefreshOrchestrator.SidecarSaveFailedFormat, h.SidecarPath, 3, FlakySidecarStore.Message));
        result.Error.Should().Contain(h.SidecarPath).And.Contain("re-adopts");
        store.Failed.Should().Be(3);
        store.Saves.Should().Be(4, "the journal, then three attempts at the final save");
        result.Summary!.ValuesWritten.Should().Be(107, "the pages were written before the save");
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector");
        Sidecar saved = h.SavedSidecar();
        saved.LastRefresh.Should().BeNull("of this run only the journal reached the file");
        saved.Pages[MonJan4.Value].PendingTexts.Should().Contain("objectiveLegend", "O1 Ship the connector", "the journal is left for the next run");
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(1, "a run whose state was not saved counts toward the streak");
    }

    [Fact]
    public void A_journal_save_that_fails_three_times_ends_the_run_before_any_page_write()
    {
        RefreshHarness h = new();
        FlakySidecarStore store = new(h.Store);
        store.FailNext(3, FlakySidecarStore.Journal);
        h.Services = h.Services with { Sidecars = store };
        h.Type(S.Objectives[0], "Ship the connector");
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run();

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
        result.Error.Should().Be(string.Format(CultureInfo.InvariantCulture, RefreshOrchestrator.SidecarSaveBeforeWritingFailedFormat, h.SidecarPath, 3, FlakySidecarStore.Message));
        result.Error.Should().Contain(h.SidecarPath).And.Contain("before writing").And.Contain(FlakySidecarStore.Message);
        store.Failed.Should().Be(3);
        store.Saves.Should().Be(3, "the journal save was tried three times and nothing else was saved");
        h.Sleeps.Should().Equal(Waits);
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" || c.Call == "DeletePageContent", "nothing is written without a saved journal");
        h.Notebook.Read(MonJan4).Outlines.Should().NotContain(o => o.PlainText == "O1 Ship the connector");
        h.SavedSidecar().Pages[MonJan4.Value].PendingTexts.Should().BeNull("no journal reached the file");
    }

    [Fact]
    public void A_rebuilt_sidecar_whose_save_fails_twice_is_saved_on_the_third_attempt()
    {
        RefreshHarness h = new(saveSidecar: false);
        FlakySidecarStore store = new(h.Store);
        store.FailNext(2, FlakySidecarStore.Any);
        h.Services = h.Services with { Sidecars = store };

        RefreshResult result = h.Run(dryRun: true, folder: h.Folder);

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.SidecarRebuilt.Should().BeTrue();
        store.Failed.Should().Be(2);
        store.Saves.Should().Be(3, "a dry run saves only the rebuilt sidecar");
        h.Sleeps.Should().Equal(Waits);
        File.Exists(h.SidecarPath).Should().BeTrue();
    }

    [Fact]
    public void A_rebuilt_sidecar_whose_save_fails_three_times_ends_the_run_before_any_page_write()
    {
        RefreshHarness h = new(saveSidecar: false);
        FlakySidecarStore store = new(h.Store);
        store.FailNext(3, FlakySidecarStore.Any);
        h.Services = h.Services with { Sidecars = store };
        h.Type(S.Objectives[0], "Ship the connector");
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(folder: h.Folder);

        result.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
        result.Error.Should().Be(string.Format(CultureInfo.InvariantCulture, RefreshOrchestrator.SidecarSaveBeforeWritingFailedFormat, h.SidecarPath, 3, FlakySidecarStore.Message));
        store.Saves.Should().Be(3);
        h.Sleeps.Should().Equal(Waits);
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" || c.Call == "DeletePageContent");
        File.Exists(h.SidecarPath).Should().BeFalse();
    }
}
