using System.Globalization;
using Serilog;

namespace Sfp.Refresh.Scheduler;

/// <summary>
/// Production <see cref="IScheduledTaskService"/> over schtasks.exe: registers "sfp2onenote refresh {name}" as a daily
/// task at 05:00 running "{exe}" refresh --notebook "{name}" --calendar --horizon {n}. A task created with /SC DAILY
/// and no /RU password runs only when the user is logged on (the Task Scheduler default for an interactive user task),
/// which is what the PRD asks for since OneNote is a per-user application. Starting OneNote when it is not running is
/// not the task's job: the refresh verb's first COM call activates OneNote.Application and the run waits for the
/// notebook to open. The process runner is injected so tests check the exact command lines; off Windows the service
/// reports that it is not supported.
/// </summary>
public sealed class ScheduledTaskService : IScheduledTaskService
{
    /// <summary>The command schtasks is run as.</summary>
    public const string SchtasksExe = "schtasks.exe";

    /// <summary>The daily start time, per the PRD.</summary>
    public const string StartTime = "05:00";

    /// <summary>The message reported where Windows Task Scheduler is not available.</summary>
    public const string NotSupportedMessage = "Scheduled refresh needs Windows Task Scheduler (schtasks.exe), which is only available on Windows.";

    private readonly IProcessRunner runner;
    private readonly ILogger log;
    private readonly bool isWindows;

    /// <param name="runner">Runs schtasks.exe; a fake in tests.</param>
    /// <param name="log">Run log.</param>
    /// <param name="isWindows">Override of the platform check for tests; null asks the operating system.</param>
    public ScheduledTaskService(IProcessRunner runner, ILogger log, bool? isWindows = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(log);
        this.runner = runner;
        this.log = log;
        this.isWindows = isWindows ?? OperatingSystem.IsWindows();
    }

    /// <summary>True when the service can register tasks on this machine.</summary>
    public bool IsSupported => isWindows;

    /// <summary>Why the service is not supported, or null.</summary>
    public string? UnsupportedReason => isWindows ? null : NotSupportedMessage;

    /// <summary>"sfp2onenote refresh SFP Q1 2027", the task name as it appears in Task Scheduler.</summary>
    public static string TaskName(string notebookName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookName);
        return "sfp2onenote refresh " + notebookName.Trim();
    }

    /// <summary>The /TR value: "{exe}" refresh --notebook "{name}" --calendar --horizon {n}.</summary>
    public static string TaskRun(string notebookName, string executablePath, int horizonDays) =>
        TaskRun(notebookName, executablePath, horizonDays, null);

    /// <summary>The /TR value with --folder "{folder}" appended when a notebooks folder is given (the sidecar lives there).</summary>
    public static string TaskRun(string notebookName, string executablePath, int horizonDays, string? notebooksFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string run = string.Format(
            CultureInfo.InvariantCulture,
            "\"{0}\" refresh --notebook \"{1}\" --calendar --horizon {2}",
            executablePath,
            notebookName.Trim(),
            horizonDays);
        if (!string.IsNullOrWhiteSpace(notebooksFolder))
        {
            run += " --folder \"" + notebooksFolder.Trim().TrimEnd('\\') + "\"";
        }

        return run;
    }

    /// <summary>The schtasks arguments that create (or replace) the task.</summary>
    public static IReadOnlyList<string> CreateArguments(string notebookName, string executablePath, int horizonDays) =>
        CreateArguments(notebookName, executablePath, horizonDays, null);

    /// <summary>The schtasks arguments that create (or replace) the task, with the notebooks folder on the command line.</summary>
    public static IReadOnlyList<string> CreateArguments(string notebookName, string executablePath, int horizonDays, string? notebooksFolder) => new[]
    {
        "/Create", "/SC", "DAILY", "/ST", StartTime, "/TN", TaskName(notebookName), "/TR", TaskRun(notebookName, executablePath, horizonDays, notebooksFolder), "/F",
    };

    /// <summary>The schtasks arguments that query the task.</summary>
    public static IReadOnlyList<string> QueryArguments(string notebookName) => new[] { "/Query", "/TN", TaskName(notebookName) };

    /// <summary>The schtasks arguments that delete the task without a prompt.</summary>
    public static IReadOnlyList<string> DeleteArguments(string notebookName) => new[] { "/Delete", "/TN", TaskName(notebookName), "/F" };

    /// <inheritdoc />
    public bool IsRegistered(string notebookName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookName);
        if (!isWindows)
        {
            return false;
        }

        ProcessResult result = runner.Run(SchtasksExe, QueryArguments(notebookName));
        bool registered = result.Succeeded && result.StandardOutput.Contains(TaskName(notebookName), StringComparison.OrdinalIgnoreCase);
        log.Information("Scheduled task {Task} is {State}", TaskName(notebookName), registered ? "registered" : "not registered");
        return registered;
    }

    /// <inheritdoc />
    public void Register(string notebookName, string executablePath, int horizonDays) =>
        Register(notebookName, executablePath, horizonDays, null);

    /// <inheritdoc />
    public void Register(string notebookName, string executablePath, int horizonDays, string? notebooksFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentOutOfRangeException.ThrowIfNegative(horizonDays);
        RequireWindows();
        ProcessResult result = runner.Run(SchtasksExe, CreateArguments(notebookName, executablePath, horizonDays, notebooksFolder));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(Failure("register", notebookName, result));
        }

        log.Information("Scheduled task {Task} registered: daily at {Time}, {Run}", TaskName(notebookName), StartTime, TaskRun(notebookName, executablePath, horizonDays, notebooksFolder));
    }

    /// <inheritdoc />
    public void Remove(string notebookName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookName);
        RequireWindows();
        ProcessResult result = runner.Run(SchtasksExe, DeleteArguments(notebookName));
        if (!result.Succeeded && IsRegistered(notebookName))
        {
            throw new InvalidOperationException(Failure("remove", notebookName, result));
        }

        log.Information("Scheduled task {Task} removed", TaskName(notebookName));
    }

    private static string Failure(string verb, string notebookName, ProcessResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim();
        return string.Format(CultureInfo.InvariantCulture, "Could not {0} the scheduled task '{1}' (schtasks exit code {2}): {3}", verb, TaskName(notebookName), result.ExitCode, detail);
    }

    private void RequireWindows()
    {
        if (!isWindows)
        {
            throw new PlatformNotSupportedException(NotSupportedMessage);
        }
    }
}
