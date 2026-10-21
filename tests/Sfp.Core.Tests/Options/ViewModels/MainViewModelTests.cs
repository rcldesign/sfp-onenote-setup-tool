using Sfp.App.ViewModels;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Refresh;
using Sfp.Core.Dates;
using Sfp.Core.Options;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>M6.1: the window loads last-used values at startup, saves on change and on exit, and feeds the Refresh tab's folders.</summary>
public class MainViewModelTests
{
    private readonly Harness _h = new();

    [UiFact]
    public async Task Initialize_loads_settings_into_both_tabs()
    {
        _h.Settings.Stored = new AppSettings { Start = new QuarterSpec(2027, 4), Dpi = 144, KnownOutputFolders = new[] { @"D:\Notebooks", "", @"d:\notebooks" } };
        using MainViewModel main = _h.NewMain();

        main.Initialize();
        await _h.Settle(main.Generate);
        await main.Refresh.PendingReload;

        main.Title.Should().Be("SFP to OneNote");
        main.Generate.Start.Should().Be(new QuarterSpec(2027, 4));
        main.Generate.Dpi.Should().Be(144);
        main.KnownOutputFolders.Should().Equal(@"D:\Notebooks");
        main.Refresh.Folders.Should().Equal(@"D:\Notebooks", Harness.DefaultFolder);
        main.Notice.Should().BeNull();
        _h.Settings.Saves.Should().Be(0, "loading does not save");
        main.Initialize();
        _h.Settings.Saves.Should().Be(0, "a second Initialize is a no-op");
    }

    [UiFact]
    public async Task Field_changes_save_once_per_real_change()
    {
        using MainViewModel main = _h.NewMain();
        main.Initialize();

        main.Generate.Dpi = 300;
        _h.Settings.Saves.Should().Be(1);
        _h.Settings.Stored.Dpi.Should().Be(300);

        main.Generate.Dpi = 300;
        _h.Settings.Saves.Should().Be(1);

        main.Generate.NotebookName = "Mine";
        _h.Settings.Saves.Should().Be(2);
        _h.Settings.Stored.NotebookName.Should().Be("Mine");
        await _h.Settle(main.Generate);
    }

    [UiFact]
    public void Save_on_exit_writes_even_when_nothing_changed()
    {
        using MainViewModel main = _h.NewMain();
        main.Initialize();

        main.SaveSettings().Should().BeTrue();
        main.SaveSettings().Should().BeTrue();

        _h.Settings.Saves.Should().Be(2);
        _h.Settings.Stored.Start.Should().Be(new QuarterSpec(2027, 1));
        _h.Settings.Stored.NotebookName.Should().Be("SFP Q1 2027");
    }

    [UiFact]
    public async Task A_successful_generate_adds_its_output_folder_to_the_known_folders_and_saves()
    {
        using MainViewModel main = _h.NewMain();
        main.Initialize();
        main.Generate.PdfPath = Harness.Pdf;
        await _h.Settle(main.Generate);
        _h.Generator.Result = _h.Generator.Result with { OutputPath = @"D:\Notebooks\SFP Q1 2027\" };
        int savesBefore = _h.Settings.Saves;

        await main.Generate.GenerateCommand.ExecuteAsync(null);
        await main.Refresh.PendingReload;

        main.KnownOutputFolders.Should().Equal(@"D:\Notebooks");
        main.Refresh.Folders.Should().Equal(@"D:\Notebooks", Harness.DefaultFolder);
        _h.Settings.Saves.Should().Be(savesBefore + 1);
        _h.Settings.Stored.KnownOutputFolders.Should().Equal(@"D:\Notebooks");

        await main.Generate.GenerateCommand.ExecuteAsync(null);
        main.KnownOutputFolders.Should().Equal(new[] { @"D:\Notebooks" }, "a folder is listed once");
    }

    [UiFact]
    public async Task A_failed_generate_adds_nothing()
    {
        _h.Generator.Result = new GenerateResult { Success = false, ExitCode = 5, Phase = GeneratePhase.WritingPages, OutputPath = @"D:\Notebooks\SFP Q1 2027" };
        using MainViewModel main = _h.NewMain();
        main.Initialize();
        main.Generate.PdfPath = Harness.Pdf;
        await _h.Settle(main.Generate);

        await main.Generate.GenerateCommand.ExecuteAsync(null);

        main.KnownOutputFolders.Should().BeEmpty();
    }

    [UiFact]
    public void A_store_that_refuses_to_save_sets_the_notice_instead_of_throwing()
    {
        _h.Settings.ThrowOnSave = new IOException("read-only");
        using MainViewModel main = _h.NewMain();
        main.Initialize();

        main.Generate.Dpi = 144;
        main.SaveSettings().Should().BeFalse();

        main.Notice.Should().Contain("/fake/settings.json").And.Contain("read-only");
    }

    [UiFact]
    public void A_store_that_fails_to_load_still_opens_with_defaults()
    {
        _h.Settings.ThrowOnLoad = new UnauthorizedAccessException("no access");
        using MainViewModel main = _h.NewMain();

        main.Initialize();

        main.Notice.Should().Contain("defaults are used").And.Contain("no access");
        main.Generate.Start.Should().Be(new QuarterSpec(2027, 1));
    }

    [UiFact]
    public void A_corrupt_settings_file_is_reported_as_a_notice()
    {
        string dir = TestPaths.NewTempDir("main-settings");
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ not json");
            using MainViewModel main = new(_h.NewGenerate(), _h.NewRefresh(), new SettingsStore(path));

            main.Initialize();

            main.Notice.Should().Contain(path).And.Contain("defaults");
            main.SaveSettings().Should().BeTrue();
            new SettingsStore(path).Load(out string? problem).NotebookName.Should().Be("SFP Q1 2027");
            problem.Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [UiFact]
    public void Current_settings_mirror_the_generate_fields_and_the_known_folders()
    {
        _h.Settings.Stored = new AppSettings { KnownOutputFolders = new[] { @"E:\x" } };
        using MainViewModel main = _h.NewMain();
        main.Initialize();
        main.Generate.WeeksRemaining = WeeksRemainingMode.AsPrinted;

        AppSettings current = main.Current();

        current.WeeksRemaining.Should().Be(WeeksRemainingMode.AsPrinted);
        current.KnownOutputFolders.Should().Equal(@"E:\x");
        current.PdfPath.Should().Be("");
    }

    [UiFact]
    public async Task Closing_the_window_goes_through_the_generate_tab()
    {
        _h.Generator.WaitForCancel = true;
        using MainViewModel main = _h.NewMain();
        main.Initialize();
        main.Generate.PdfPath = Harness.Pdf;
        await _h.Settle(main.Generate);
        (await main.PrepareToCloseAsync()).Should().BeTrue("nothing runs");

        Task generating = main.Generate.GenerateCommand.ExecuteAsync(null);
        SpinWait.SpinUntil(() => _h.Generator.Runs == 1, TimeSpan.FromSeconds(5)).Should().BeTrue();
        _h.Dialogs.ConfirmAnswer = false;
        (await main.PrepareToCloseAsync()).Should().BeFalse("the user kept the run going");

        _h.Dialogs.ConfirmAnswer = true;
        (await main.PrepareToCloseAsync()).Should().BeTrue();
        main.Generate.Run.ShowCancelled.Should().BeTrue();
        await generating;
    }

    [UiFact]
    public async Task Closing_during_a_refresh_asks_no_keeps_it_running_and_yes_closes_once_the_cancelled_run_returns()
    {
        using ManualResetEventSlim finishing = new(false);
        _h.Refresher.WaitForCancel = true;
        _h.Refresher.AfterCancelGate = finishing;
        using MainViewModel main = await InitializedWithANotebook();
        Task refreshing = main.Refresh.RefreshNowCommand.ExecuteAsync(null);
        SpinWait.SpinUntil(() => _h.Refresher.WaitingForCancel, TimeSpan.FromSeconds(5)).Should().BeTrue();

        _h.Dialogs.ConfirmAnswer = false;
        (await main.PrepareToCloseAsync()).Should().BeFalse("the user kept the refresh going");
        _h.Dialogs.Titles.Should().Equal(RefreshViewModel.CloseWhileRunningTitle);
        _h.Dialogs.Questions.Should().Equal(RefreshViewModel.CloseWhileRunningQuestion);
        main.Refresh.IsBusy.Should().BeTrue();
        _h.Refresher.SawCancel.Should().BeFalse("No cancels nothing");

        _h.Dialogs.ConfirmAnswer = true;
        Task<bool> closing = main.PrepareToCloseAsync();
        SpinWait.SpinUntil(() => _h.Refresher.SawCancel, TimeSpan.FromSeconds(5)).Should().BeTrue("yes cancels the run");
        main.Refresh.ProgressText.Should().Be(RefreshViewModel.CancellingText);
        closing.IsCompleted.Should().BeFalse("the window stays open until the run returns");

        finishing.Set();
        (await closing).Should().BeTrue();
        main.Refresh.IsBusy.Should().BeFalse();
        main.Refresh.CancelledText.Should().StartWith("Refresh cancelled");
        _h.Dialogs.Questions.Should().Equal(RefreshViewModel.CloseWhileRunningQuestion, RefreshViewModel.CloseWhileRunningQuestion);
        await refreshing;
    }

    [UiFact]
    public async Task Closing_with_both_tabs_running_asks_about_the_refresh_first_and_closes_after_both_runs_stopped()
    {
        _h.Refresher.WaitForCancel = true;
        _h.Generator.WaitForCancel = true;
        using MainViewModel main = await InitializedWithANotebook();
        main.Generate.PdfPath = Harness.Pdf;
        await _h.Settle(main.Generate);
        Task generating = main.Generate.GenerateCommand.ExecuteAsync(null);
        Task refreshing = main.Refresh.RefreshNowCommand.ExecuteAsync(null);
        SpinWait.SpinUntil(() => _h.Generator.Runs == 1 && _h.Refresher.WaitingForCancel, TimeSpan.FromSeconds(5)).Should().BeTrue();

        _h.Dialogs.ConfirmAnswer = false;
        (await main.PrepareToCloseAsync()).Should().BeFalse();
        _h.Dialogs.Questions.Should().Equal(new[] { RefreshViewModel.CloseWhileRunningQuestion }, "a No to the first question ends the close attempt");
        main.Generate.Run.IsRunning.Should().BeTrue();
        main.Refresh.IsBusy.Should().BeTrue();

        _h.Dialogs.ConfirmAnswer = true;
        (await main.PrepareToCloseAsync()).Should().BeTrue();
        _h.Dialogs.Questions.Skip(1).Should().Equal(RefreshViewModel.CloseWhileRunningQuestion, GenerateViewModel.CloseWhileRunningQuestion);
        main.Generate.Run.ShowCancelled.Should().BeTrue();
        main.Refresh.IsBusy.Should().BeFalse();
        main.Refresh.CancelledText.Should().StartWith("Refresh cancelled");
        await generating;
        await refreshing;
    }

    /// <summary>A window over one notebook built by the tool, listed and selected on the Refresh tab.</summary>
    private async Task<MainViewModel> InitializedWithANotebook()
    {
        const string folder = Harness.DefaultFolder + @"\SFP Q1 2027";
        _h.Registry.Notebooks.Add(new KnownNotebook("SFP Q1 2027", folder, new QuarterSpec(2027, 1), null, null, folder + ".sfp-state.json"));
        MainViewModel main = _h.NewMain();
        main.Initialize();
        await _h.Settle(main.Generate);
        await main.Refresh.PendingReload;
        main.Refresh.SelectedNotebook.Should().NotBeNull();
        return main;
    }

    [UiFact]
    public void Tab_index_and_notice_are_observable()
    {
        using MainViewModel main = _h.NewMain();
        List<string?> changed = new();
        main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        main.SelectedTabIndex = 1;
        main.Notice = "x";

        changed.Should().Equal(nameof(MainViewModel.SelectedTabIndex), nameof(MainViewModel.Notice));
        main.Generate.Should().BeOfType<GenerateViewModel>();
        main.Refresh.Should().BeOfType<RefreshViewModel>();
    }
}
