using Serilog;

namespace Sfp.App.ViewModels.Services;

/// <summary>One run's log: the logger the orchestrator writes to and the file it lands in.</summary>
public interface IRunLog : IDisposable
{
    ILogger Logger { get; }

    /// <summary>Full path of the log file.</summary>
    string Path { get; }
}

/// <summary>Starts a log file per Generate or Refresh run (the WPF implementation wraps Sfp.Cli's RunLog).</summary>
public interface IRunLogFactory
{
    IRunLog Start(string verb);

    /// <summary>
    /// Starts the run's log in the temp folder: the fallback when <see cref="Start"/> fails (the log folder cannot be
    /// created or written). A factory without a fallback throws, and the run then goes on without a log file.
    /// </summary>
    IRunLog StartFallback(string verb) => throw new NotSupportedException("This log factory has no fallback folder.");
}

/// <summary>Starts a run's log without ever letting a log problem stop or lock the run.</summary>
public static class RunLogStarter
{
    /// <summary>
    /// The run's log: <see cref="IRunLogFactory.Start"/>, else <see cref="IRunLogFactory.StartFallback"/> (which records
    /// why the usual folder failed), else a log without a file (an empty <see cref="IRunLog.Path"/>) and
    /// <paramref name="notice"/> set to the one line the tab shows. Never throws.
    /// </summary>
    public static IRunLog StartOrFallBack(this IRunLogFactory logs, string verb, out string? notice)
    {
        ArgumentNullException.ThrowIfNull(logs);
        notice = null;
        Exception failure;
        try
        {
            return logs.Start(verb);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            IRunLog fallback = logs.StartFallback(verb);
            fallback.Logger.Warning(failure, "The log folder could not be used; this run logs to {Path}", fallback.Path);
            return fallback;
        }
        catch (Exception)
        {
            // The usual folder's failure is the one worth naming; the fallback's own reason adds nothing for the user.
            notice = "The run log could not be started (" + failure.Message + "); this run has no log file.";
            return new SilentRunLog("");
        }
    }
}

/// <summary>A log that goes nowhere, for tests and for runs without a file (then <see cref="Path"/> is empty).</summary>
public sealed class SilentRunLog : IRunLog
{
    public SilentRunLog(string path)
    {
        Path = path;
    }

    public ILogger Logger => Serilog.Core.Logger.None;

    public string Path { get; }

    public void Dispose()
    {
    }
}

/// <summary>Factory of <see cref="SilentRunLog"/>s with predictable paths.</summary>
public sealed class SilentRunLogFactory : IRunLogFactory
{
    private int _count;

    public IRunLog Start(string verb)
    {
        _count++;
        return new SilentRunLog(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sfp2onenote-{verb}-{_count}.log"));
    }
}
