using System.Globalization;
using System.Text;
using System.Xml.Linq;
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

    /// <summary>The longest /TR value schtasks.exe accepts; a longer command is registered from a task definition file (/XML).</summary>
    public const int MaxTaskRunLength = 261;

    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly IProcessRunner runner;
    private readonly ILogger log;
    private readonly bool isWindows;
    private readonly string tempFolder;

    /// <param name="runner">Runs schtasks.exe; a fake in tests.</param>
    /// <param name="log">Run log.</param>
    /// <param name="isWindows">Override of the platform check for tests; null asks the operating system.</param>
    /// <param name="tempFolder">Where a task definition file is written for /XML and deleted afterwards; the user's temp folder when null.</param>
    public ScheduledTaskService(IProcessRunner runner, ILogger log, bool? isWindows = null, string? tempFolder = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(log);
        this.runner = runner;
        this.log = log;
        this.isWindows = isWindows ?? OperatingSystem.IsWindows();
        this.tempFolder = tempFolder ?? Path.GetTempPath();
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
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return "\"" + executablePath + "\" " + TaskArguments(notebookName, horizonDays, notebooksFolder);
    }

    /// <summary>The program's arguments: refresh --notebook "{name}" --calendar --horizon {n}, plus --folder "{folder}" when given.</summary>
    public static string TaskArguments(string notebookName, int horizonDays, string? notebooksFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookName);
        string arguments = string.Format(CultureInfo.InvariantCulture, "refresh --notebook \"{0}\" --calendar --horizon {1}", notebookName.Trim(), horizonDays);
        if (!string.IsNullOrWhiteSpace(notebooksFolder))
        {
            arguments += " --folder \"" + notebooksFolder.Trim().TrimEnd('\\') + "\"";
        }

        return arguments;
    }

    /// <summary>The schtasks arguments that create (or replace) the task from a task definition file.</summary>
    public static IReadOnlyList<string> XmlCreateArguments(string notebookName, string definitionPath) => new[]
    {
        "/Create", "/TN", TaskName(notebookName), "/XML", definitionPath, "/F",
    };

    /// <summary>
    /// The task definition registered with /XML when the /TR value would pass <see cref="MaxTaskRunLength"/>: the same
    /// daily 05:00 trigger, the same principal (the logged-on user, least privilege: schtasks' defaults without /RU and
    /// /RL) and schtasks' default settings, with the program and its arguments in separate elements, which have no such
    /// limit. <paramref name="startDate"/> is the trigger's first day, as schtasks uses the day the task is created.
    /// </summary>
    public static string TaskXml(string notebookName, string executablePath, int horizonDays, string? notebooksFolder, DateTime startDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        XNamespace t = TaskNamespace;
        XElement task = new(
            t + "Task",
            new XAttribute("version", "1.2"),
            new XElement(t + "RegistrationInfo", new XElement(t + "Description", "sfp2onenote daily refresh of " + notebookName.Trim())),
            new XElement(
                t + "Triggers",
                new XElement(
                    t + "CalendarTrigger",
                    new XElement(t + "StartBoundary", startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T" + StartTime + ":00"),
                    new XElement(t + "Enabled", "true"),
                    new XElement(t + "ScheduleByDay", new XElement(t + "DaysInterval", "1")))),
            new XElement(
                t + "Principals",
                new XElement(t + "Principal", new XAttribute("id", "Author"), new XElement(t + "LogonType", "InteractiveToken"), new XElement(t + "RunLevel", "LeastPrivilege"))),
            new XElement(
                t + "Settings",
                new XElement(t + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(t + "DisallowStartIfOnBatteries", "true"),
                new XElement(t + "StopIfGoingOnBatteries", "true"),
                new XElement(t + "AllowHardTerminate", "true"),
                new XElement(t + "StartWhenAvailable", "false"),
                new XElement(t + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(t + "IdleSettings", new XElement(t + "StopOnIdleEnd", "true"), new XElement(t + "RestartOnIdle", "false")),
                new XElement(t + "AllowStartOnDemand", "true"),
                new XElement(t + "Enabled", "true"),
                new XElement(t + "Hidden", "false"),
                new XElement(t + "RunOnlyIfIdle", "false"),
                new XElement(t + "WakeToRun", "false"),
                new XElement(t + "ExecutionTimeLimit", "PT72H"),
                new XElement(t + "Priority", "7")),
            new XElement(
                t + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(t + "Exec", new XElement(t + "Command", executablePath), new XElement(t + "Arguments", TaskArguments(notebookName, horizonDays, notebooksFolder)))));
        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + task;
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
        string run = TaskRun(notebookName, executablePath, horizonDays, notebooksFolder);
        ProcessResult result = run.Length <= MaxTaskRunLength
            ? runner.Run(SchtasksExe, CreateArguments(notebookName, executablePath, horizonDays, notebooksFolder))
            : CreateFromDefinition(notebookName, executablePath, horizonDays, notebooksFolder);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(Failure("register", notebookName, result));
        }

        log.Information("Scheduled task {Task} registered: daily at {Time}, {Run}", TaskName(notebookName), StartTime, run);
    }

    /// <summary>Registers the task from a definition file in the temp folder (see <see cref="TaskXml"/>), deleting the file afterwards.</summary>
    private ProcessResult CreateFromDefinition(string notebookName, string executablePath, int horizonDays, string? notebooksFolder)
    {
        string path = Path.Combine(tempFolder, "sfp2onenote-task-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(path, TaskXml(notebookName, executablePath, horizonDays, notebooksFolder, DateTime.Today), Encoding.Unicode);
        log.Information("The task command is longer than the {Limit} characters schtasks /TR accepts, so the task is registered from {Path}", MaxTaskRunLength, path);
        try
        {
            return runner.Run(SchtasksExe, XmlCreateArguments(notebookName, path));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning(ex, "Could not delete the task definition file {Path}", path);
            }
        }
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
