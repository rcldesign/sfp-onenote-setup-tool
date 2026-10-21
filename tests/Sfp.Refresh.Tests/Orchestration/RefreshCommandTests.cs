using Sfp.OneNote;
using Sfp.Cli;
using Sfp.Cli.Verbs;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>M4.7: the refresh verb over TextWriters with an injected orchestrator.</summary>
public class RefreshCommandTests
{
    private static readonly string LogDir = TestPaths.NewTempDir("refresh-logs");

    private static (int Code, string Out, string Err, RefreshRequest? Request) Run(RefreshArgs args, RefreshResult? result = null)
    {
        using StringWriter stdout = new(System.Globalization.CultureInfo.InvariantCulture);
        using StringWriter stderr = new(System.Globalization.CultureInfo.InvariantCulture);
        RefreshRequest? seen = null;
        int code = RefreshCommand.Run(
            args,
            stdout,
            stderr,
            (request, manifest, templates, log, logPath) => new StubOrchestrator(result ?? Ok(), r => seen = r),
            LogDir);
        return (code, stdout.ToString(), stderr.ToString(), seen);
    }

    private static RefreshResult Ok(IReadOnlyList<PlannedChange>? changes = null) => new()
    {
        Success = true,
        ExitCode = 0,
        Elapsed = TimeSpan.FromSeconds(1.5),
        Summary = new RefreshSummary
        {
            Values = SetupValues.Empty with { Objectives = new[] { "Ship the connector", null, null, null, null, null, null, null } },
            Changes = changes ?? new[] { new PlannedChange(PageKey.Day(new DateOnly(2027, 1, 4)), "objectiveLegend", null, "O1 Ship the connector", ChangeKind.Create, false, null) },
            PagesTouched = 1,
            ValuesWritten = 1,
            PageTitles = new Dictionary<string, string>(StringComparer.Ordinal) { ["day:2027-01-04"] = "Mon Jan 4" },
        },
    };

    [Fact]
    public void Arguments_are_validated_with_exit_code_2()
    {
        Run(new RefreshArgs { Notebook = " " }).Should().BeEquivalentTo((Code: ExitCodes.ValidationError, Err: "--notebook needs"), o => o.Including(r => r.Code));
        Run(new RefreshArgs { Notebook = " " }).Err.Should().Contain("--notebook");
        Run(new RefreshArgs { Notebook = "SFP Q1 2027", Horizon = -1 }).Err.Should().Contain("--horizon");
        Run(new RefreshArgs { Notebook = "SFP Q1 2027", Today = "yesterday" }).Err.Should().Contain("--today");
        Run(new RefreshArgs { Notebook = "SFP Q1 2027", Folder = Path.Combine(LogDir, "missing") }).Err.Should().Contain("Folder not found");
        Run(new RefreshArgs { Notebook = "SFP Q1 2027", Templates = Path.Combine(LogDir, "no-templates") }).Err.Should().Contain("Templates folder not found");
        Run(new RefreshArgs { Notebook = "SFP Q1 2027", Templates = Path.Combine(LogDir, "no-templates") }).Code.Should().Be(ExitCodes.ValidationError);
    }

    [Fact]
    public void The_request_carries_the_parsed_options_and_the_summary_is_printed_on_success()
    {
        string folder = TestPaths.NewTempDir("cmd-folder");

        (int code, string stdout, string stderr, RefreshRequest? request) = Run(new RefreshArgs
        {
            Notebook = " SFP Q1 2027 ",
            Folder = folder,
            Calendar = true,
            Horizon = 7,
            WriteInked = true,
            Today = "2027-01-04",
        });

        code.Should().Be(0);
        request.Should().Be(new RefreshRequest("SFP Q1 2027", Path.GetFullPath(folder), true, 7, false, true, new DateOnly(2027, 1, 4)));
        stdout.Should().Contain("Refreshed SFP Q1 2027 in 1.5 s.").And.Contain("Pages touched: 1. Values written: 1.");
        stdout.Should().Contain("Opening", "progress lines are printed");
        stderr.Should().BeEmpty();
    }

    [Fact]
    public void Dry_run_prints_the_listing_and_quiet_prints_only_errors()
    {
        (int code, string stdout, _, RefreshRequest? request) = Run(new RefreshArgs { Notebook = "SFP Q1 2027", DryRun = true, Today = "2027-01-04" });

        code.Should().Be(0);
        request!.DryRun.Should().BeTrue();
        stdout.Should().Contain("Planned changes (1):").And.Contain("Mon Jan 4  objectiveLegend  create  (none) -> \"O1 Ship the connector\"").And.Contain("Dry run: nothing was written.");

        (int quietCode, string quietOut, string quietErr, _) = Run(new RefreshArgs { Notebook = "SFP Q1 2027", Quiet = true, Today = "2027-01-04" });
        quietCode.Should().Be(0);
        quietOut.Should().BeEmpty();
        quietErr.Should().BeEmpty();
    }

    [Fact]
    public void Failures_are_printed_to_stderr_with_the_log_path_and_the_exit_code_passes_through()
    {
        RefreshResult failed = new() { Success = false, ExitCode = ExitCodes.WriteError, Error = "Writing page 'Mon Jan 4' failed: rejected", LogPath = "C:\\logs\\run.log" };

        (int code, string stdout, string stderr, _) = Run(new RefreshArgs { Notebook = "SFP Q1 2027", Today = "2027-01-04" }, failed);

        code.Should().Be(ExitCodes.WriteError);
        stdout.Should().NotContain("Refreshed");
        stderr.Should().Contain("Refresh failed: Writing page 'Mon Jan 4' failed: rejected").And.Contain("Log: C:\\logs\\run.log");

        RefreshResult unreachable = Ok() with { Success = false, ExitCode = ExitCodes.RefreshSourceUnreachable };
        (int sourcesCode, _, string sourcesErr, _) = Run(new RefreshArgs { Notebook = "SFP Q1 2027", Today = "2027-01-04" }, unreachable);
        sourcesCode.Should().Be(6);
        sourcesErr.Should().Contain("failed");
    }

    [Fact]
    public void The_production_composition_reports_OneNote_unavailable_off_Windows()
    {
        Skip.If(OperatingSystem.IsWindows(), "On Windows the production composition starts OneNote.");
        using StringWriter stdout = new(System.Globalization.CultureInfo.InvariantCulture);
        using StringWriter stderr = new(System.Globalization.CultureInfo.InvariantCulture);

        int code = RefreshCommand.Run(new RefreshArgs { Notebook = "SFP Q1 2027", Today = "2027-01-04" }, stdout, stderr, null, LogDir);

        code.Should().Be(ExitCodes.OneNoteUnavailable);
        stderr.ToString().Should().Contain("OneNote desktop");
    }

    [Fact]
    public void Build_declares_the_verb_and_its_options()
    {
        System.CommandLine.Command command = RefreshCommand.Build();

        command.Name.Should().Be("refresh");
        command.Options.Select(o => o.Name).Should().BeEquivalentTo(new[] { "--notebook", "--folder", "--calendar", "--horizon", "--dry-run", "--write-inked", "--quiet", "--today", "--manifest", "--templates" });
        command.Options.Single(o => o.Name == "--notebook").Required.Should().BeTrue();
        RefreshCommand.DefaultSearchFolders().Should().NotBeEmpty();
    }

    [Fact]
    public void Without_quiet_a_failure_page_conflicts_and_a_lock_refusal_the_verb_prints_are_shown_once()
    {
        // Fix round 3 app-4: the run log's console sink shows this verb's warnings, and the orchestrator logged at Warning
        // what the verb prints itself (the failure streak, the page conflicts, the lock refusal), so each reached the
        // console twice. Those lines are logged at Information now: the log file keeps them, the console shows them once.
        PageKey monJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
        RectPt objective = RefreshManifest.Shipped.Generated.Setup.Objectives[0];

        // Every page write is refused: the run fails after the sidecar was found (exit 5) and warns about every page.
        RefreshHarness refused = new();
        refused.Type(objective, "Ship the connector");
        refused.App.UpdatePageContentFailure = _ => new OneNoteException("UpdatePageContent", OneNoteHResults.LastModifiedDateDidNotMatch, "The page was modified since it was read.", null);
        (int refusedCode, string refusedShown, string refusedLog) = RunOverHarness(refused, "refused");
        refusedCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
        Occurrences(refusedShown, "refused every page write").Should().Be(1, refusedShown);
        Occurrences(refusedShown, "Page Mon Jan 4 changed").Should().Be(1, "only the summary's warning names the page: {0}", refusedShown);
        refusedLog.Should().Contain("[INF] Refresh of SFP Q1 2027 failed (1 runs in a row): OneNote refused every page write")
            .And.Contain("[INF] Page Mon Jan 4 changed while it was being written; reading it again")
            .And.Contain("[INF] Page Mon Jan 4 changed again while it was being written");

        // One page keeps changing while it is written: the run succeeds and its summary warns about that page.
        RefreshHarness conflicted = new();
        conflicted.Type(objective, "Ship the connector");
        string pageId = conflicted.Notebook.PageIds[monJan4];
        int edits = 0;
        conflicted.App.BeforeWrite = id =>
        {
            if (id == pageId)
            {
                edits++;
                conflicted.Notebook.TypeUserText(monJan4, new RectPt(400, 380 + (20 * edits), 200, 14), "typing " + edits);
            }
        };
        (int conflictedCode, string conflictedShown, _) = RunOverHarness(conflicted, "conflicted");
        conflictedCode.Should().Be(0, conflictedShown);
        Occurrences(conflictedShown, "Page Mon Jan 4 changed").Should().Be(1, conflictedShown);

        // Another run holds the lock: this one is refused (exit 5) without being started.
        RefreshHarness locked = new();
        using RefreshLock held = RefreshLock.TryAcquire(locked.SidecarPath, RefreshHarness.Now, Serilog.Core.Logger.None, out _)!;
        (int lockedCode, string lockedShown, string lockedLog) = RunOverHarness(locked, "locked");
        lockedCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
        Occurrences(lockedShown, "not started").Should().Be(1, lockedShown);
        lockedLog.Should().Contain("[INF] Refresh of SFP Q1 2027 not started: ");
        (refusedShown + conflictedShown + lockedShown).Should().NotMatchRegex(LogLevelToken, "no line of the run log reaches the console");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_run_the_opener_stops_is_shown_once_with_or_without_quiet(bool quiet)
    {
        // Fix round 4 cliapp-1 (case a): the sidecar's notebook is closed and another notebook of that name is open, so the
        // opener stops the run (exit 2). It logs its problem at Warning and the verb prints it as the failure, so without
        // --quiet the run log's console sink showed it a second time. The verb's lines are the only console output now.
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("shown-once-trial"));
        RefreshHarness h = new(app: trial.App);
        h.App.Close(h.Notebook.NotebookId);

        (int code, string shown, string log) = RunOverHarness(h, "opener-" + quiet, quiet);

        code.Should().Be(RefreshOrchestrator.NotFoundExitCode, shown);
        Occurrences(shown, "Refresh failed: ").Should().Be(1, shown);
        Occurrences(shown, trial.NotebookPath).Should().Be(1, "the problem names the other notebook once (quiet {0}): {1}", quiet, shown);
        shown.Should().NotMatchRegex(LogLevelToken, "no line of the run log reaches the console (quiet {0})", quiet);
        log.Should().MatchRegex(@"\[WRN\] [^\n]*" + System.Text.RegularExpressions.Regex.Escape(trial.NotebookPath), "the log file keeps the opener's problem");
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stale_lock_that_cannot_be_removed_is_shown_once_with_or_without_quiet(bool quiet)
    {
        // Fix round 4 cliapp-1 (case c): a stale lock that cannot be deleted refuses the run (exit 5). The lock logged its
        // takeover at Warning, the orchestrator logged the stuck lock at Warning, and the verb prints the refusal; without
        // --quiet the console showed all three. The verb's line is the only console output now.
        RefreshHarness h = new();
        string lockPath = RefreshLock.PathBeside(h.SidecarPath);
        File.WriteAllText(lockPath, string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{{ \"token\": \"other\", \"processId\": 4242, \"processStart\": null, \"machine\": \"OTHER-PC\", \"acquired\": \"{RefreshHarness.Now.AddHours(-3):O}\" }}"));
        using Undeletable? stuck = Undeletable.Make(lockPath);
        Skip.If(stuck is null, "This system cannot make the lock file undeletable (chattr +i needs root off Windows).");

        (int code, string shown, string log) = RunOverHarness(h, "stuck-" + quiet, quiet);

        code.Should().Be(RefreshOrchestrator.WriteFailureExitCode, shown);
        Occurrences(shown, "Refresh failed: ").Should().Be(1, shown);
        Occurrences(shown, "is stale").Should().Be(1, "the refusal says it once (quiet {0}): {1}", quiet, shown);
        Occurrences(shown, lockPath).Should().Be(1, "only the refusal names the lock file (quiet {0}): {1}", quiet, shown);
        shown.Should().NotMatchRegex(LogLevelToken, "no line of the run log reaches the console (quiet {0})", quiet);
        log.Should().Contain("is stale but could not be taken over", "the log file keeps the orchestrator's warning");
    }

    /// <summary>
    /// The level of a run log line, as its console line ("[09:30:00 WRN] ...") and its file line ("... [WRN] ...") show
    /// it; no line the verb prints itself has one.
    /// </summary>
    private const string LogLevelToken = @"\b(VRB|DBG|INF|WRN|ERR|FTL)\]";

    /// <summary>
    /// Runs the verb over a real orchestrator on <paramref name="h"/>, with both console streams captured, where the run
    /// log's console sink writes. Returns the exit code, everything shown and the run's log file.
    /// </summary>
    private static (int Code, string Shown, string Log) RunOverHarness(RefreshHarness h, string name, bool quiet = false)
    {
        string logs = Path.Combine(LogDir, "shown-once-" + name);
        using StringWriter stdout = new(System.Globalization.CultureInfo.InvariantCulture);
        using StringWriter stderr = new(System.Globalization.CultureInfo.InvariantCulture);
        using StringWriter console = new(System.Globalization.CultureInfo.InvariantCulture);
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        Console.SetOut(console);
        Console.SetError(console);
        int code;
        try
        {
            code = RefreshCommand.Run(
                new RefreshArgs { Notebook = h.Name, Folder = h.Folder, Today = "2027-01-04", Templates = TestPaths.TemplatesDir, Quiet = quiet },
                stdout,
                stderr,
                (_, _, _, log, logPath) => new RefreshOrchestrator(h.Services with { Log = log, LogPath = logPath }),
                logs);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return (code, console.ToString() + stdout + stderr, File.ReadAllText(Directory.GetFiles(logs, "*-refresh.log").Single()));
    }

    private static int Occurrences(string text, string value) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(value)).Count;

    /// <summary>
    /// Keeps a file from being deleted until disposed, as StoppedRunStreakTests does: on Windows a handle open without
    /// delete sharing; elsewhere the immutable attribute (chattr +i), which needs root.
    /// </summary>
    private sealed class Undeletable : IDisposable
    {
        private readonly string path;
        private readonly FileStream? handle;

        private Undeletable(string path, FileStream? handle)
        {
            this.path = path;
            this.handle = handle;
        }

        /// <summary>The file made undeletable, or null when this system cannot do it.</summary>
        public static Undeletable? Make(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return new Undeletable(path, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            }

            return Chattr("+i", path) ? new Undeletable(path, null) : null;
        }

        public void Dispose()
        {
            if (handle is not null)
            {
                handle.Dispose();
            }
            else
            {
                Chattr("-i", path);
            }
        }

        private static bool Chattr(string flag, string path)
        {
            System.Diagnostics.ProcessStartInfo start = new("chattr") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(flag);
            start.ArgumentList.Add(path);
            try
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)!;
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    private sealed class StubOrchestrator : IRefreshOrchestrator
    {
        private readonly RefreshResult result;
        private readonly Action<RefreshRequest> seen;

        public StubOrchestrator(RefreshResult result, Action<RefreshRequest> seen)
        {
            this.result = result;
            this.seen = seen;
        }

        public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            seen(request);
            progress?.Report("Opening notebook " + request.NotebookName);
            return result;
        }
    }
}
