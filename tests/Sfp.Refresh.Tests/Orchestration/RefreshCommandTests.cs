using Sfp.Cli;
using Sfp.Cli.Verbs;
using Sfp.Core.Plan;
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
