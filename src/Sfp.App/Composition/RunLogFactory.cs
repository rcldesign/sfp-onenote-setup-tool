using Serilog;
using Sfp.App.ViewModels.Services;
using Sfp.Cli.Logging;

namespace Sfp.App.Composition;

/// <summary>Starts one Sfp.Cli <see cref="RunLog"/> per run, so the app and the CLI write the same log files.</summary>
public sealed class RunLogFactory : IRunLogFactory
{
    public IRunLog Start(string verb) => new RunLogHandle(RunLog.Start(verb, quiet: true));

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
