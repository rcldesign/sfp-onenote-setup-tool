using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Sfp.Cli.Logging;

/// <summary>
/// One log file per run under %LOCALAPPDATA%\sfp2onenote\logs (override with SFP2ONENOTE_LOGDIR), timestamped lines,
/// every COM call name and duration, every rendered page. Image data never reaches the log.
/// </summary>
public sealed class RunLog : IDisposable
{
    public const string LogDirEnvVar = "SFP2ONENOTE_LOGDIR";

    private readonly Logger _logger;

    private RunLog(Logger logger, string path)
    {
        _logger = logger;
        Path = path;
    }

    /// <summary>The Serilog logger for this run.</summary>
    public ILogger Logger => _logger;

    /// <summary>Full path of this run's log file.</summary>
    public string Path { get; }

    /// <summary>Default log folder.</summary>
    public static string DefaultDirectory
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable(LogDirEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env;
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(local, "sfp2onenote", "logs");
        }
    }

    /// <summary>The console's line: Serilog's default without the exception, so a stack trace goes to the file only.</summary>
    private const string ConsoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}";

    /// <summary>
    /// Starts a log for a verb. Everything from Debug up goes to the file, exceptions with their stack traces. The console
    /// shows the events <see cref="ConsoleShows"/> allows, never a stack trace; warnings and errors go to stderr.
    /// </summary>
    public static RunLog Start(string verb, bool quiet, string? directory = null, DateTimeOffset? now = null, bool printsOwnProgress = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        string dir = directory ?? DefaultDirectory;
        Directory.CreateDirectory(dir);
        DateTimeOffset stamp = now ?? DateTimeOffset.Now;
        string file = System.IO.Path.Combine(dir, stamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + verb + ".log");

        LoggerConfiguration config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("verb", verb)
            .WriteTo.File(file, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}", formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.Conditional(
                logEvent => ConsoleShows(logEvent.Level, quiet, printsOwnProgress),
                sink => sink.Console(outputTemplate: ConsoleTemplate, standardErrorFromLevel: LogEventLevel.Warning, formatProvider: CultureInfo.InvariantCulture));

        Logger logger = config.CreateLogger();
        logger.Information("sfp2onenote {Verb} started; log {Path}", verb, file);
        return new RunLog(logger, file);
    }

    /// <summary>
    /// Whether the console shows an event of <paramref name="level"/>. A verb that prints its own progress, summary and
    /// failure block (<paramref name="printsOwnProgress"/>) gets the warnings only, and nothing when
    /// <paramref name="quiet"/>: its Information lines (every COM call, every rendered page) would repeat the progress,
    /// and its errors are the failure it prints itself, so they go to the file only. Any other verb gets errors only when
    /// quiet, else Information and up.
    /// </summary>
    public static bool ConsoleShows(LogEventLevel level, bool quiet, bool printsOwnProgress) =>
        printsOwnProgress
            ? !quiet && level == LogEventLevel.Warning
            : level >= (quiet ? LogEventLevel.Error : LogEventLevel.Information);

    public void Dispose() => _logger.Dispose();
}
