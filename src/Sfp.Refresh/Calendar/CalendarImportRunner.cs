using System.Globalization;
using Serilog;

namespace Sfp.Refresh.Calendar;

/// <summary>A source that could not be read on this run.</summary>
/// <param name="Label">The source's label from the Setup page.</param>
/// <param name="Message">The exception message, for the summary and the log.</param>
public sealed record SourceFailure(string Label, string Message);

/// <summary>A reader paired with the label it is reported under.</summary>
public sealed record CalendarSourceBinding(string Label, ICalendarSource Source);

/// <summary>What one fetch across all sources produced.</summary>
/// <param name="Events">Every event the reachable sources returned, in start order.</param>
/// <param name="Failures">The sources that failed, in configuration order.</param>
/// <param name="SourceCount">How many sources were tried.</param>
public sealed record CalendarFetchResult(IReadOnlyList<CalendarEvent> Events, IReadOnlyList<SourceFailure> Failures, int SourceCount)
{
    public bool AnyFailed => Failures.Count > 0;

    /// <summary>
    /// True when at least one source was configured and every one of them failed. The refresh verb maps this to
    /// exit code 6 (refresh source unreachable); a partial failure still writes what it got and exits 0.
    /// </summary>
    public bool AllSourcesFailed => SourceCount > 0 && Failures.Count == SourceCount;

    /// <summary>"Work: timeout; Holidays: file missing" for the summary line and the toast.</summary>
    public string FailureSummary => string.Join("; ", Failures.Select(f => f.Label + ": " + f.Message));
}

/// <summary>
/// Failure handling for the calendar import (M5.5): each source is fetched on its own, a failing source is skipped
/// and reported while the others are written, and the consecutive-total-failure streak drives the toast.
/// </summary>
public static class CalendarImportRunner
{
    /// <summary>Title of the toast raised after <see cref="FailureCounter.ToastThreshold"/> total failures.</summary>
    public const string ToastTitle = "sfp2onenote: calendar sources unreachable";

    /// <summary>Fetches every source independently; a source that throws is recorded and skipped, never fatal.</summary>
    public static CalendarFetchResult Fetch(IReadOnlyList<CalendarSourceBinding> sources, DateOnly from, DateOnly to, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);
        List<CalendarEvent> events = new();
        List<SourceFailure> failures = new();
        foreach (CalendarSourceBinding binding in sources)
        {
            try
            {
                IReadOnlyList<CalendarEvent> got = binding.Source.Fetch(from, to);
                events.AddRange(got);
                logger.Information("Calendar source {Label}: {Count} events from {From} to {To}", binding.Label, got.Count, from, to);
            }
            catch (Exception ex)
            {
                failures.Add(new SourceFailure(binding.Label, ex.Message));
                logger.Warning(ex, "Calendar source {Label} failed and is skipped this run", binding.Label);
            }
        }

        return new CalendarFetchResult(
            events.OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal).ToList(),
            failures,
            sources.Count);
    }

    /// <summary>Creates the readers for the Setup sources through the factory and fetches them. A source the factory cannot create counts as failed.</summary>
    public static CalendarFetchResult Fetch(IReadOnlyList<CalendarSource> sources, ICalendarSourceFactory factory, DateOnly from, DateOnly to, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(factory);
        List<CalendarSourceBinding> bindings = new();
        List<SourceFailure> creationFailures = new();
        foreach (CalendarSource source in sources)
        {
            try
            {
                bindings.Add(new CalendarSourceBinding(source.Label, factory.Create(source)));
            }
            catch (Exception ex)
            {
                creationFailures.Add(new SourceFailure(source.Label, ex.Message));
                logger.Warning(ex, "Calendar source {Label} could not be opened and is skipped this run", source.Label);
            }
        }

        CalendarFetchResult fetched = Fetch(bindings, from, to, logger);
        foreach (CalendarSourceBinding binding in bindings)
        {
            (binding.Source as IDisposable)?.Dispose();
        }

        return fetched with { Failures = creationFailures.Concat(fetched.Failures).ToList(), SourceCount = sources.Count };
    }

    /// <summary>
    /// Updates and saves the failure counter from a fetch result and raises the toast while the streak is at or past the
    /// threshold (once per run, so a broken feed reminds the user once a day). Returns true when the toast was raised.
    /// </summary>
    public static bool Track(CalendarFetchResult result, FailureCounter counter, ISourceFailureNotifier notifier, DateTimeOffset now, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(logger);

        if (!result.AllSourcesFailed)
        {
            if (counter.ConsecutiveTotalFailures > 0)
            {
                logger.Information("Calendar sources reachable again after {Count} failed runs", counter.ConsecutiveTotalFailures);
            }

            counter.Reset(now);
            counter.Save();
            return false;
        }

        counter.RecordTotalFailure(now, result.FailureSummary);
        counter.Save();
        logger.Warning("All {Count} calendar sources failed ({Streak} runs in a row): {Summary}", result.SourceCount, counter.ConsecutiveTotalFailures, result.FailureSummary);
        if (!counter.ShouldToast)
        {
            return false;
        }

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "All {0} calendar source(s) failed {1} runs in a row. Last error: {2}",
            result.SourceCount,
            counter.ConsecutiveTotalFailures,
            result.FailureSummary);
        notifier.Notify(ToastTitle, message);
        return true;
    }
}
