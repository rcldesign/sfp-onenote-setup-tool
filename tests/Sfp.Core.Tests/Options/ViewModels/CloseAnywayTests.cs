using Sfp.App.ViewModels;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Dates;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-10: when a cancelled run sees the cancel but never returns, a second close request or the delay asks
/// whether to close anyway, naming the partial notebook (Generate) or saying the refresh is cut off (Refresh); yes closes,
/// no keeps waiting.
/// </summary>
public class CloseAnywayTests
{
    private const string OutputFolder = @"D:\Notebooks";
    private const string Folder = Harness.DefaultFolder + @"\SFP Q1 2027";

    private readonly Harness _h = new();
    private readonly CloseAnywayDialogs _closeDialogs = new();

    private static GenerateRequest Request() => new(
        new Core.Options.GenerateOptions { Start = new QuarterSpec(2027, 1), PdfPath = Harness.Pdf, OutputFolder = OutputFolder, NotebookName = "SFP Q1 2027" },
        TestManifest.Shipped,
        TestPaths.TemplatesDir,
        RenderOnly: false,
        RenderOutputDir: null);

    private async Task<MainViewModel> Window(TimeSpan closeAnywayDelay)
    {
        _h.Registry.Notebooks.Add(new KnownNotebook("SFP Q1 2027", Folder, new QuarterSpec(2027, 1), null, null, Folder + ".sfp-state.json"));
        MainViewModel main = new(_h.NewGenerate(), _h.NewRefresh(), _h.Settings, _closeDialogs) { CloseAnywayDelay = closeAnywayDelay };
        main.Initialize();
        await main.Generate.PendingPdfCheck;
        await main.Refresh.PendingReload;
        return main;
    }

    [UiFact]
    public async Task After_the_delay_a_generate_that_never_returns_asks_and_yes_closes_naming_the_partial_notebook()
    {
        using ManualResetEventSlim neverReturns = new(false);
        _h.Generator.WaitForCancel = true;
        _h.Generator.AfterCancelGate = neverReturns;
        _closeDialogs.Answers.Enqueue(true);
        using MainViewModel main = await Window(TimeSpan.FromMilliseconds(200));
        Task generating = main.Generate.Run.StartAsync(Request());
        await _h.WaitUntilAsync(() => _h.Generator.Runs == 1);
        try
        {
            Task<bool> closing = main.PrepareToCloseAsync();
            await _h.WaitUntilAsync(() => _h.Generator.SawCancel, "the run sees the cancel");

            (await closing.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("yes closes although the run never returned");

            string question = _closeDialogs.Asked.Should().ContainSingle().Subject;
            question.Should().Be(MainViewModel.CloseAnywayQuestion(true, Path.Combine(OutputFolder, "SFP Q1 2027"), false));
            question.Should().Contain(Path.Combine(OutputFolder, "SFP Q1 2027"));
            _closeDialogs.Titles.Should().Equal(MainViewModel.CloseAnywayTitle);
            main.Generate.Run.IsRunning.Should().BeTrue();
        }
        finally
        {
            neverReturns.Set();
            await generating;
        }
    }

    [UiFact]
    public async Task A_second_close_request_asks_no_keeps_waiting_and_a_third_closes_on_yes()
    {
        using ManualResetEventSlim neverReturns = new(false);
        _h.Refresher.WaitForCancel = true;
        _h.Refresher.AfterCancelGate = neverReturns;
        _closeDialogs.Answers.Enqueue(false);
        _closeDialogs.Answers.Enqueue(true);
        using MainViewModel main = await Window(TimeSpan.FromHours(1));
        Task refreshing = main.Refresh.RefreshNowCommand.ExecuteAsync(null);
        await _h.WaitUntilAsync(() => _h.Refresher.WaitingForCancel);
        try
        {
            Task<bool> closing = main.PrepareToCloseAsync();
            await _h.WaitUntilAsync(() => _h.Refresher.SawCancel);

            main.RequestCloseAgain();
            await _h.WaitUntilAsync(() => _closeDialogs.Asked.Count == 1, "the second click asks");
            closing.IsCompleted.Should().BeFalse("no keeps waiting for the run");

            main.RequestCloseAgain();
            (await closing.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("the next click asks again and yes closes");

            _closeDialogs.Asked.Should().HaveCount(2);
            _closeDialogs.Asked[0].Should().Be(MainViewModel.CloseAnywayQuestion(false, null, true));
            _closeDialogs.Asked[0].Should().Contain("cut off");
            main.Refresh.IsBusy.Should().BeTrue();
        }
        finally
        {
            neverReturns.Set();
            await refreshing;
        }
    }

    [UiFact]
    public async Task After_a_no_the_close_completes_once_the_run_stops_without_asking_again()
    {
        using ManualResetEventSlim finishing = new(false);
        _h.Generator.WaitForCancel = true;
        _h.Generator.AfterCancelGate = finishing;
        _closeDialogs.Answers.Enqueue(false);
        using MainViewModel main = await Window(TimeSpan.FromMilliseconds(100));
        Task generating = main.Generate.Run.StartAsync(Request());
        await _h.WaitUntilAsync(() => _h.Generator.Runs == 1);

        Task<bool> closing = main.PrepareToCloseAsync();
        await _h.WaitUntilAsync(() => _closeDialogs.Asked.Count == 1, "the delay asks once");
        await Task.Delay(300);
        _closeDialogs.Asked.Should().HaveCount(1, "after a no only another close request asks again");
        closing.IsCompleted.Should().BeFalse();

        finishing.Set();
        (await closing.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        main.Generate.Run.ShowCancelled.Should().BeTrue();
        await generating;
    }

    [UiFact]
    public void The_question_names_what_is_left_behind()
    {
        MainViewModel.CloseAnywayQuestion(true, @"D:\Notebooks\SFP Q1 2027", false).Should().Be(
            @"The cancelled Generate run has not stopped yet. Close the window anyway? The partial notebook at D:\Notebooks\SFP Q1 2027 is left behind; delete it before you generate that notebook again.");
        MainViewModel.CloseAnywayQuestion(false, null, true).Should().Be(
            "The cancelled refresh has not stopped yet. Close the window anyway? The refresh is cut off in the middle of a page; run Refresh again afterwards.");
        MainViewModel.CloseAnywayQuestion(true, null, true).Should().Be(
            "The cancelled Generate run and refresh have not stopped yet. Close the window anyway? The partial notebook is left behind; delete it before you generate that notebook again. The refresh is cut off in the middle of a page; run Refresh again afterwards.");
    }

    [UiFact]
    public async Task A_close_request_without_a_waiting_close_does_nothing()
    {
        using MainViewModel main = await Window(TimeSpan.FromMilliseconds(100));

        main.RequestCloseAgain();
        (await main.PrepareToCloseAsync()).Should().BeTrue();

        _closeDialogs.Asked.Should().BeEmpty();
    }

    /// <summary>Answers the close-anyway questions in order (no once the answers run out) and records them.</summary>
    private sealed class CloseAnywayDialogs : IDialogService
    {
        private readonly object _gate = new();
        private readonly List<string> _asked = new();
        private readonly List<string> _titles = new();

        public Queue<bool> Answers { get; } = new();

        public IReadOnlyList<string> Asked
        {
            get
            {
                lock (_gate)
                {
                    return _asked.ToList();
                }
            }
        }

        public IReadOnlyList<string> Titles
        {
            get
            {
                lock (_gate)
                {
                    return _titles.ToList();
                }
            }
        }

        public bool Confirm(string title, string question)
        {
            lock (_gate)
            {
                _titles.Add(title);
                _asked.Add(question);
                return Answers.Count > 0 && Answers.Dequeue();
            }
        }

        public string? PickFile(string title, string filter, string? initialPath) => throw new NotSupportedException();

        public string? PickFolder(string title, string? initialFolder) => throw new NotSupportedException();

        public string? PickSaveFile(string title, string filter, string suggestedFileName) => throw new NotSupportedException();
    }
}
