using System.Collections.Concurrent;
using Sfp.App.ViewModels;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Refresh;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Dates;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-6: once a close is accepted and has to wait, both tabs are closing: Generate, the Browse buttons,
/// Preview changes, Refresh now and Reload stay disabled, and the close looks again after the wait until no run is on.
/// </summary>
public class CloseFlowTests
{
    private const string Folder = Harness.DefaultFolder + @"\SFP Q1 2027";

    private readonly Harness _h = new();
    private readonly QueuedUiThread _ui = new();

    private static GenerateRequest Request() => new(
        new Core.Options.GenerateOptions { Start = new QuarterSpec(2027, 1), PdfPath = Harness.Pdf },
        TestManifest.Shipped,
        TestPaths.TemplatesDir,
        RenderOnly: false,
        RenderOutputDir: null);

    /// <summary>A window over the harness fakes whose UI posts wait for <see cref="QueuedUiThread.Drain"/>, with a valid PDF and one notebook listed.</summary>
    private async Task<MainViewModel> ReadyWindow()
    {
        _h.Registry.Notebooks.Add(new KnownNotebook("SFP Q1 2027", Folder, new QuarterSpec(2027, 1), null, null, Folder + ".sfp-state.json"));
        GenerateViewModel generate = new(
            _h.Manifest,
            _h.Dates,
            _h.Validator,
            new PreviewStripViewModel(_h.Plans, _h.Renderer, _h.Manifest, _h.Dates, _ui, TimeSpan.Zero),
            new GenerateRunViewModel(_h.Logs, (_, _) => _h.Generator, _ui, _h.Shell, _h.Dialogs),
            _h.Dialogs,
            _ui,
            _h.Env);
        RefreshViewModel refresh = new(_h.Registry, _h.Tasks, _h.Logs, (_, _) => _h.Refresher, _h.Dialogs, _ui, _h.Env);
        MainViewModel main = new(generate, refresh, _h.Settings);
        main.Initialize();
        main.Generate.PdfPath = Harness.Pdf;
        await main.Generate.PendingPdfCheck;
        _ui.Drain();
        await main.Generate.Previews.Pending;
        _ui.Drain();
        await main.Refresh.PendingReload;
        main.Generate.GenerateCommand.CanExecute(null).Should().BeTrue("the form is valid before the close");
        main.Refresh.RefreshNowCommand.CanExecute(null).Should().BeTrue("a notebook is selected before the close");
        return main;
    }

    [UiFact]
    public async Task While_the_close_waits_for_a_refresh_no_generate_and_no_browse_can_start()
    {
        using ManualResetEventSlim finishing = new(false);
        _h.Refresher.WaitForCancel = true;
        _h.Refresher.AfterCancelGate = finishing;
        using MainViewModel main = await ReadyWindow();
        Task refreshing = main.Refresh.RefreshNowCommand.ExecuteAsync(null);
        await _h.WaitUntilAsync(() => _h.Refresher.WaitingForCancel);

        Task<bool> closing = main.PrepareToCloseAsync();
        await _h.WaitUntilAsync(() => _h.Refresher.SawCancel);

        closing.IsCompleted.Should().BeFalse("the refresh is still finishing its page");
        main.Generate.IsClosing.Should().BeTrue();
        main.Generate.CanGenerate.Should().BeFalse();
        main.Generate.GenerateCommand.CanExecute(null).Should().BeFalse("a run started now would be cut off when the window closes");
        main.Generate.BrowsePdfCommand.CanExecute(null).Should().BeFalse();
        main.Generate.BrowseFolderCommand.CanExecute(null).Should().BeFalse();
        main.Generate.BrowsePreviousCommand.CanExecute(null).Should().BeFalse();
        await main.Generate.GenerateCommand.ExecuteAsync(null);
        _h.Generator.Runs.Should().Be(0);

        finishing.Set();
        (await closing).Should().BeTrue();
        await refreshing;
    }

    [UiFact]
    public async Task While_the_close_waits_for_a_generate_no_refresh_and_no_reload_can_start()
    {
        using ManualResetEventSlim deleting = new(false);
        _h.Generator.WaitForCancel = true;
        _h.Generator.AfterCancelGate = deleting;
        using MainViewModel main = await ReadyWindow();
        Task generating = main.Generate.GenerateCommand.ExecuteAsync(null);
        await _h.WaitUntilAsync(() => _h.Generator.Runs == 1);

        Task<bool> closing = main.PrepareToCloseAsync();
        await _h.WaitUntilAsync(() => _h.Generator.SawCancel);

        closing.IsCompleted.Should().BeFalse("the orchestrator is deleting the partial notebook");
        main.Refresh.IsClosing.Should().BeTrue();
        main.Refresh.PreviewChangesCommand.CanExecute(null).Should().BeFalse();
        main.Refresh.RefreshNowCommand.CanExecute(null).Should().BeFalse();
        main.Refresh.ReloadNotebooksCommand.CanExecute(null).Should().BeFalse();
        await main.Refresh.RefreshNowCommand.ExecuteAsync(null);
        await main.Refresh.PreviewChangesCommand.ExecuteAsync(null);
        _h.Refresher.Runs.Should().Be(0);

        deleting.Set();
        (await closing).Should().BeTrue();
        await generating;
    }

    [UiFact]
    public async Task A_run_that_started_during_the_wait_is_cancelled_and_awaited_before_the_close_completes()
    {
        using ManualResetEventSlim refreshFinishing = new(false);
        using ManualResetEventSlim generateDeleting = new(false);
        _h.Refresher.WaitForCancel = true;
        _h.Refresher.AfterCancelGate = refreshFinishing;
        _h.Generator.WaitForCancel = true;
        _h.Generator.AfterCancelGate = generateDeleting;
        using MainViewModel main = await ReadyWindow();
        Task refreshing = main.Refresh.RefreshNowCommand.ExecuteAsync(null);
        await _h.WaitUntilAsync(() => _h.Refresher.WaitingForCancel);
        Task<bool> closing = main.PrepareToCloseAsync();
        await _h.WaitUntilAsync(() => _h.Refresher.SawCancel);

        // A run that got past the disabled buttons (started straight on the run view model).
        Task generating = main.Generate.Run.StartAsync(Request());
        await _h.WaitUntilAsync(() => _h.Generator.Runs == 1);
        refreshFinishing.Set();
        await refreshing;

        await _h.WaitUntilAsync(() => _h.Generator.SawCancel, "the close looks again and cancels it");
        closing.IsCompleted.Should().BeFalse("the close waits for that run too");
        generateDeleting.Set();
        (await closing).Should().BeTrue();
        main.Generate.Run.IsRunning.Should().BeFalse();
        main.Generate.Run.ShowCancelled.Should().BeTrue();
        await generating;
    }

    [UiFact]
    public async Task A_close_with_nothing_running_completes_at_once_and_leaves_the_tabs_as_they_are()
    {
        using MainViewModel main = await ReadyWindow();

        Task<bool> closing = main.PrepareToCloseAsync();

        closing.IsCompleted.Should().BeTrue("the window's Closing handler answers synchronously");
        (await closing).Should().BeTrue();
        main.Generate.IsClosing.Should().BeFalse("the window closes at once; there is no wait to protect");
        main.Refresh.IsClosing.Should().BeFalse();
    }

    /// <summary>Posts wait in a queue until the test drains it on its own thread, one at a time, as a dispatcher runs them.</summary>
    private sealed class QueuedUiThread : IUiThread
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        public void Post(Action action) => _posted.Enqueue(action);

        public void Drain()
        {
            while (_posted.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
