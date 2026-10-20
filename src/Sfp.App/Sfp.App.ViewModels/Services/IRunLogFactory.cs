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
}

/// <summary>A log that goes nowhere, for tests and for runs that need no file.</summary>
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
