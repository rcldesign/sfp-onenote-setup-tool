using Sfp.Refresh.Scheduler;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Scheduler;

/// <summary>M5.4: the exact schtasks command lines, the query parsing, and the not-supported report off Windows.</summary>
public class ScheduledTaskServiceTests
{
    private const string Exe = @"C:\Tools\sfp2onenote.exe";
    private const string Name = "SFP Q1 2027";

    private static ScheduledTaskService Service(FakeProcessRunner runner, bool windows = true) =>
        new(runner, new CapturingLog().Logger, windows);

    [Fact]
    public void Register_runs_schtasks_create_daily_at_five_with_the_refresh_command_line()
    {
        FakeProcessRunner runner = new();

        Service(runner).Register(Name, Exe, 14);

        (string file, IReadOnlyList<string> args) = runner.Calls.Should().ContainSingle().Subject;
        file.Should().Be("schtasks.exe");
        args.Should().Equal(
            "/Create", "/SC", "DAILY", "/ST", "05:00",
            "/TN", "sfp2onenote refresh SFP Q1 2027",
            "/TR", "\"C:\\Tools\\sfp2onenote.exe\" refresh --notebook \"SFP Q1 2027\" --calendar --horizon 14",
            "/F");
    }

    [Fact]
    public void IsRegistered_queries_by_task_name_and_reads_the_answer()
    {
        FakeProcessRunner runner = new();
        runner.Results.Enqueue(new ProcessResult(0, "Folder: \\\r\nTaskName: sfp2onenote refresh SFP Q1 2027\r\nNext Run Time: 1/5/2027 5:00:00 AM\r\n", ""));
        runner.Results.Enqueue(new ProcessResult(1, "", "ERROR: The system cannot find the file specified."));
        ScheduledTaskService service = Service(runner);

        service.IsRegistered(Name).Should().BeTrue();
        service.IsRegistered(Name).Should().BeFalse();

        runner.Calls.Should().AllSatisfy(c => c.Arguments.Should().Equal("/Query", "/TN", "sfp2onenote refresh SFP Q1 2027"));
    }

    [Fact]
    public void Remove_deletes_without_a_prompt_and_tolerates_an_already_missing_task()
    {
        FakeProcessRunner runner = new();
        runner.Results.Enqueue(new ProcessResult(0, "SUCCESS: The scheduled task \"sfp2onenote refresh SFP Q1 2027\" was successfully deleted.", ""));
        Service(runner).Remove(Name);
        runner.Calls.Single().Arguments.Should().Equal("/Delete", "/TN", "sfp2onenote refresh SFP Q1 2027", "/F");

        FakeProcessRunner missing = new();
        missing.Results.Enqueue(new ProcessResult(1, "", "ERROR: The system cannot find the file specified."));
        missing.Results.Enqueue(new ProcessResult(1, "", "ERROR: The system cannot find the file specified."));
        Action remove = () => Service(missing).Remove(Name);
        remove.Should().NotThrow("a task that is already gone is the wanted end state");

        FakeProcessRunner denied = new();
        denied.Results.Enqueue(new ProcessResult(1, "", "ERROR: Access is denied."));
        denied.Results.Enqueue(new ProcessResult(0, "TaskName: sfp2onenote refresh SFP Q1 2027", ""));
        Action deniedRemove = () => Service(denied).Remove(Name);
        deniedRemove.Should().Throw<InvalidOperationException>().WithMessage("*remove*Access is denied*");
    }

    [Fact]
    public void A_failed_create_is_reported_with_the_schtasks_output()
    {
        FakeProcessRunner runner = new();
        runner.Results.Enqueue(new ProcessResult(1, "", "ERROR: Access is denied."));

        Action register = () => Service(runner).Register(Name, Exe, 14);

        register.Should().Throw<InvalidOperationException>().WithMessage("*register*sfp2onenote refresh SFP Q1 2027*exit code 1*Access is denied*");
    }

    [Fact]
    public void Off_Windows_the_service_reports_not_supported_and_runs_nothing()
    {
        FakeProcessRunner runner = new();
        ScheduledTaskService service = Service(runner, windows: false);

        service.IsSupported.Should().BeFalse();
        service.UnsupportedReason.Should().Be(ScheduledTaskService.NotSupportedMessage);
        service.IsRegistered(Name).Should().BeFalse();
        Action register = () => service.Register(Name, Exe, 14);
        register.Should().Throw<PlatformNotSupportedException>().WithMessage("*Windows*");
        Action remove = () => service.Remove(Name);
        remove.Should().Throw<PlatformNotSupportedException>();
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Arguments_are_validated()
    {
        ScheduledTaskService service = Service(new FakeProcessRunner());

        Action blank = () => service.Register(" ", Exe, 14);
        blank.Should().Throw<ArgumentException>();
        Action negative = () => service.Register(Name, Exe, -1);
        negative.Should().Throw<ArgumentOutOfRangeException>();
        ScheduledTaskService.TaskName(" SFP Q1 2027 ").Should().Be("sfp2onenote refresh SFP Q1 2027");
    }

    /// <summary>Records every command line and hands back queued results (exit 0 with empty output when the queue is empty).</summary>
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = new();

        public Queue<ProcessResult> Results { get; } = new();

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToList()));
            return Results.Count > 0 ? Results.Dequeue() : new ProcessResult(0, "", "");
        }
    }
}
