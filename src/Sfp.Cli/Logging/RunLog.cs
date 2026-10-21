using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security;
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

    /// <summary>Where a CLI verb's log goes when its folder cannot be used (<see cref="StartWithFallback"/>): sfp2onenote\logs in the temp folder.</summary>
    public static string FallbackDirectory => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sfp2onenote", "logs");

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
        string file = FilePath(dir, verb, now ?? DateTimeOffset.Now);

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
    /// <see cref="Start"/> after making sure the log file can be written: the folder is created and the file opened once,
    /// because the file sink, unlike <see cref="Directory.CreateDirectory(string)"/>, does not throw for a file it cannot
    /// open (it logs nothing, and the run would name a log file that does not exist). A folder or file that cannot be used
    /// throws here instead (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/> and the like), so the
    /// caller can fall back. The app's log factory starts its logs this way.
    /// </summary>
    public static RunLog StartChecked(string verb, bool quiet, string? directory = null, DateTimeOffset? now = null, bool printsOwnProgress = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        string dir = directory ?? DefaultDirectory;
        DateTimeOffset stamp = now ?? DateTimeOffset.Now;
        Directory.CreateDirectory(dir);
        using (new FileStream(FilePath(dir, verb, stamp), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
        }

        return Start(verb, quiet, dir, stamp, printsOwnProgress);
    }

    /// <summary>
    /// <see cref="Start"/> for a CLI verb, which must not stop for its log folder: the daily refresh task runs unattended.
    /// When <paramref name="directory"/> (the default folder when null) cannot be created or cannot hold the log file,
    /// one line naming the folder and the reason goes to <paramref name="stderr"/>, even when quiet (it is a setting the
    /// user has to fix), and the log starts in <paramref name="fallbackDirectory"/> (<see cref="FallbackDirectory"/> when
    /// null) instead. When that fails too, one line names both folders and their reasons and the result is null: the verb
    /// stops with the validation exit code.
    /// </summary>
    public static RunLog? StartWithFallback(
        string verb,
        bool quiet,
        TextWriter stderr,
        string? directory = null,
        bool printsOwnProgress = false,
        string? fallbackDirectory = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        ArgumentNullException.ThrowIfNull(stderr);
        string dir = directory ?? DefaultDirectory;
        string fallback = fallbackDirectory ?? FallbackDirectory;
        DateTimeOffset stamp = now ?? DateTimeOffset.Now;
        if (TryStart(verb, quiet, dir, stamp, printsOwnProgress, out RunLog? log, out string? reason))
        {
            return log;
        }

        if (TryStart(verb, quiet, fallback, stamp, printsOwnProgress, out log, out string? fallbackReason))
        {
            stderr.WriteLine("Could not use the log folder " + dir + ": " + Sentence(reason) + " This run logs to " + fallback + " instead.");

            // Information: the file keeps the reason, and the console, which shows a verb's warnings, has the line above.
            log.Logger.Information("The log folder {Folder} could not be used: {Reason}", dir, reason);
            return log;
        }

        stderr.WriteLine("Could not start the run log in " + dir + ": " + Sentence(reason) + " Nor in " + fallback + ": " + Sentence(fallbackReason));
        return null;
    }

    /// <summary>Starts the log in <paramref name="dir"/> with <see cref="StartChecked"/>, or says why it cannot.</summary>
    private static bool TryStart(string verb, bool quiet, string dir, DateTimeOffset stamp, bool printsOwnProgress, [NotNullWhen(true)] out RunLog? log, [NotNullWhen(false)] out string? reason)
    {
        try
        {
            log = StartChecked(verb, quiet, dir, stamp, printsOwnProgress);
            reason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            log = null;
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>The run's log file: yyyyMMdd-HHmmss-verb.log in <paramref name="dir"/>.</summary>
    private static string FilePath(string dir, string verb, DateTimeOffset stamp) =>
        System.IO.Path.Combine(dir, stamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + verb + ".log");

    /// <summary>The text trimmed and ending with a period, so a reason reads as a sentence inside the line.</summary>
    private static string Sentence(string text)
    {
        string trimmed = text.Trim();
        return trimmed.EndsWith('.') ? trimmed : trimmed + ".";
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
