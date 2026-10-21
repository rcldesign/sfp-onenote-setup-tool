using Serilog;
using Sfp.App.ViewModels.Services;
using Sfp.Cli.Logging;

namespace Sfp.App.Composition;

/// <summary>
/// Starts one Sfp.Cli <see cref="RunLog"/> per run, so the app and the CLI write the same log files. Each log is started
/// with <see cref="RunLog.StartChecked"/>, which opens the file once first: a folder that exists but cannot take the file
/// throws, so <see cref="RunLogStarter.StartOrFallBack"/> moves the run to the temp folder, else runs it without a log
/// file and says so, instead of naming a log file that does not exist.
/// </summary>
public sealed class RunLogFactory : IRunLogFactory
{
    public IRunLog Start(string verb) => new RunLogHandle(RunLog.StartChecked(verb, quiet: true));

    /// <summary>The same log in %TEMP%\sfp2onenote\logs, for a run whose usual log folder cannot be used.</summary>
    public IRunLog StartFallback(string verb) =>
        new RunLogHandle(RunLog.StartChecked(verb, quiet: true, directory: RunLog.FallbackDirectory));

    private sealed class RunLogHandle : IRunLog
    {
        private readonly RunLog _log;

        public RunLogHandle(RunLog log)
        {
            _log = log;
        }

        public ILogger Logger => _log.Logger;

        public string Path => _log.Path;

        public void Dispose() => _log.Dispose();
    }
}
