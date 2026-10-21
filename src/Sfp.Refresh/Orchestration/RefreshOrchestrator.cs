using System.Diagnostics;
using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Writer;

namespace Sfp.Refresh.Orchestration;

/// <summary>Where the Time Blocks window of a Refresh run came from.</summary>
public enum TimeBlockWindowSource
{
    /// <summary>The sidecar: the window the daily pages were generated with.</summary>
    Sidecar,

    /// <summary>The Setup page, because the sidecar records no window (it was written before the window was recorded).</summary>
    SetupPage,

    /// <summary>The default window, because neither the sidecar nor the Setup page gives a valid one.</summary>
    Default,
}

/// <summary>
/// The production <see cref="IRefreshOrchestrator"/>: opens the notebook, recovers the sidecar, reads Setup (with the
/// annual fallback), plans propagation and, when asked, the calendar changes (sources from Setup plus the bundled
/// holidays, placement through the engine in the Time Blocks window the pages were generated with (the sidecar's, else
/// the Setup page's, see <see cref="CalendarWindow"/>), previous texts read from the tool-owned outlines, freeze and
/// diff with the sidecar's FrozenThrough), then either lists every change (dry run, nothing written) or writes them
/// page by page through the anchor writer, updates the sidecar (anchors, LastRefresh, FrozenThrough), syncs and fills
/// the summary. Exit codes: 0 success, 2 notebook not found, 3 OneNote unavailable, 5 write failure, 6 every Setup
/// calendar source failed. Every real run then updates the failure streak beside the sidecar (see <see cref="TrackStreak"/>).
/// </summary>
public sealed class RefreshOrchestrator : IRefreshOrchestrator
{
    /// <summary>Exit code for a notebook that is nowhere to be found.</summary>
    public const int NotFoundExitCode = 2;
    public const int OneNoteUnavailableExitCode = 3;
    public const int WriteFailureExitCode = 5;
    public const int SourcesUnreachableExitCode = 6;

    /// <summary>Reason recorded on a change skipped because its value came from ink and the run was not told to write inked values.</summary>
    public const string InkedSkipReason = "The value came from ink and was not confirmed (pass --write-inked or confirm in the app).";

    /// <summary>Title of the toast raised when OneNote made the third real run in a row fail (exit 3 or 5).</summary>
    public const string RefreshFailureToastTitle = "sfp2onenote: refresh failing";

    /// <summary>Reason recorded on a change not written because the page changed under the write twice in a row.</summary>
    public const string ConflictReason = "The page changed while Refresh was writing it (twice in a row); nothing was written to it and it will be tried again on the next run.";

    /// <summary>
    /// Error of a run refused because another run of the same notebook holds the lock beside its sidecar ({0}: the
    /// notebook, {1}: the run that holds it); exit 5, and the failure streak is left alone.
    /// </summary>
    public const string AlreadyRunningFormat = "Another Refresh of '{0}' is running ({1}); this run was not started. Try again when it has finished.";

    /// <summary>
    /// Error of a run whose end-of-run sidecar save failed on every attempt ({0}: the sidecar path, {1}: the attempts,
    /// {2}: the last error); the run fails (exit 5) and the next run re-adopts the objects it wrote.
    /// </summary>
    public const string SidecarSaveFailedFormat = "The sidecar {0} could not be saved after {1} attempts ({2}). What this run wrote is on the pages; the next run re-adopts those objects.";

    /// <summary>How many times the end-of-run sidecar save is tried before the run reports it failed.</summary>
    public const int SidecarSaveAttempts = 3;

    /// <summary>The waits before the second and the third save attempt.</summary>
    private static readonly TimeSpan[] SidecarSaveWaits = { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500) };

    /// <summary>
    /// Error of a run in which at least one page write was made and OneNote refused every one as a conflict ({0}: how
    /// many pages); such a run fails (exit 5) and counts toward the failure streak.
    /// </summary>
    public const string AllWritesConflictedFormat = "OneNote refused every page write of this run ({0} page(s)): each page was reported as changed since Refresh read it (hrLastModifiedDateDidNotMatch). If nobody was editing those pages, the page time stamp check (dateExpectedLastModified) may be rejecting unchanged pages; see the log.";

    private readonly RefreshServices services;

    public RefreshOrchestrator(RefreshServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    /// <inheritdoc />
    public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch clock = Stopwatch.StartNew();
        RunState state = new();
        try
        {
            RefreshResult result = RunGuarded(request, progress, cancellationToken, clock, state);
            TrackStreak(request, state, result);
            return result;
        }
        finally
        {
            // Held from the moment the sidecar's path is known until the streak beside it is updated.
            state.Lock?.Dispose();
        }
    }

    private RefreshResult RunGuarded(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken, Stopwatch clock, RunState state)
    {
        try
        {
            return RunCore(request, progress, cancellationToken, clock, state);
        }
        catch (OneNoteUnavailableException ex)
        {
            services.Log.Error(ex, "OneNote is unavailable");
            return Fail(OneNoteUnavailableExitCode, ex.Message, clock);
        }
        catch (OperationCanceledException)
        {
            services.Log.Information("Refresh cancelled");
            state.Cancelled = true;
            return new RefreshResult { Success = false, ExitCode = WriteFailureExitCode, Error = "Cancelled.", Elapsed = clock.Elapsed, LogPath = services.LogPath };
        }
        catch (OneNoteException ex)
        {
            services.Log.Error(ex, "OneNote call {Call} failed", ex.Call);
            return Fail(WriteFailureExitCode, ex.Message, clock);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Top-level handler of the run: the result carries the message and the exit code instead of a crash.
            services.Log.Error(ex, "Refresh failed");
            return Fail(WriteFailureExitCode, ex.Message, clock);
        }
    }

    private RefreshResult RunCore(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken, Stopwatch clock, RunState state)
    {
        List<string> warnings = new();
        Manifest manifest = services.Manifest;

        progress?.Report("Opening notebook " + request.NotebookName);
        NotebookOpener opener = new(services.App, services.Sidecars, services.Log, services.OpenTimeout, services.Sleep);
        (Sidecar? Sidecar, string? Path) found = opener.FindSidecar(request.NotebookName, request.NotebooksFolder, services.SearchFolders);
        state.SidecarPath = found.Path;
        OpenedNotebook? opened = opener.Open(request.NotebookName, request.NotebooksFolder, services.SearchFolders, found);
        if (opened is null)
        {
            return Fail(NotFoundExitCode, $"Notebook '{request.NotebookName}' was not found: no sidecar names it, OneNote does not list it and no folder holds it. Generate it first, or pass --folder with the folder that holds it.", clock);
        }

        state.SidecarPath = opened.SidecarPath;
        state.Lock = RefreshLock.TryAcquire(opened.SidecarPath, services.Clock(), services.Log, out string? holder);
        if (state.Lock is null && holder is not null)
        {
            state.Refused = true;
            services.Log.Warning("Refresh of {Name} not started: {Holder} holds the lock beside its sidecar", request.NotebookName, holder);
            return Fail(WriteFailureExitCode, string.Format(CultureInfo.InvariantCulture, AlreadyRunningFormat, request.NotebookName, holder), clock);
        }

        progress?.Report("Checking the sidecar");
        SidecarRecovery recovery = new(services.App, manifest, services.Dates, services.Plans, services.Log);
        RecoveryResult recovered = recovery.Recover(opened.Notebook, opened.Sidecar, services.ToolVersion);
        Sidecar sidecar = recovered.Sidecar;
        warnings.AddRange(recovered.Warnings);
        if (recovered.Rebuilt)
        {
            services.Sidecars.Save(opened.SidecarPath, sidecar);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Quarter quarter = services.Dates.Build(sidecar.Quarter);
        NotebookPlan plan = services.Plans.Build(manifest, quarter, SidecarPlanOptions.ToGenerateOptions(sidecar.Quarter, sidecar.PlanOptions));
        PageCache pages = new(services.App, sidecar, services.Log, recovered.Pages);
        Dictionary<string, string> titles = plan.Pages.ToDictionary(p => p.Key.Value, p => p.Title, StringComparer.Ordinal);

        progress?.Report("Reading the Setup page");
        PageXml? setupXml = pages.Read(PageKey.Setup);
        if (setupXml is null)
        {
            return Fail(
                pages.PageId(PageKey.Setup) is null ? NotFoundExitCode : WriteFailureExitCode,
                $"The Setup page of '{request.NotebookName}' could not be read: {(pages.Failures.TryGetValue(PageKey.Setup.Value, out string? why) ? why : "unknown")}.",
                clock);
        }

        SetupValues values = services.SetupReader.Read(setupXml, manifest);
        values = SetupReader.ApplyFallback(values, AnnualPageFallback.FromSidecar(manifest, pages.Read, sidecar));
        TimeBlockWindow window = ChooseWindow(sidecar, values);
        SlotCatalog catalog = new(manifest, services.Dates, quarter, window);

        progress?.Report("Planning propagation");
        IPropagator propagator = services.Propagators(catalog, pages.Read);
        List<PlannedChange> changes = propagator.Plan(plan, values, sidecar, request.WriteInkedValues).ToList();
        HashSet<PlannedChange> calendarChanges = new(ReferenceEqualityComparer.Instance);

        CalendarOutcome? calendar = null;
        if (request.Calendar)
        {
            progress?.Report("Importing calendar events");
            calendar = PlanCalendar(request, values, sidecar, plan, catalog, pages, state, window, warnings);
            if (calendar is not null)
            {
                changes.AddRange(calendar.Changes);
                foreach (PlannedChange change in calendar.Changes)
                {
                    calendarChanges.Add(change);
                }
            }
        }

        warnings.AddRange(pages.Failures.Select(f => $"Page {(titles.TryGetValue(f.Key, out string? t) ? t : f.Key)} could not be read: {f.Value}"));
        bool allSourcesFailed = calendar?.AllSourcesFailed == true;
        int exitCode = allSourcesFailed ? SourcesUnreachableExitCode : 0;

        if (request.DryRun)
        {
            services.Log.Information("Dry run: {Count} planned changes, nothing written", changes.Count);
            RefreshSummary dry = new()
            {
                Values = values,
                Changes = changes,
                Skipped = changes.Count(c => c.Blocked || (c.Inked && !request.WriteInkedValues)),
                FailedSources = calendar?.FailedSources ?? Array.Empty<string>(),
                SidecarRebuilt = recovered.Rebuilt,
                Warnings = warnings,
                PageTitles = titles,
                FrozenThrough = calendar?.FrozenThrough,
            };
            return new RefreshResult { Success = !allSourcesFailed, ExitCode = exitCode, Summary = dry, Elapsed = clock.Elapsed, LogPath = services.LogPath };
        }

        progress?.Report("Writing changes");
        WriteState writeState = new(sidecar);
        WriteOutcome written;
        try
        {
            written = WriteAll(changes, calendarChanges, plan, catalog, pages, writeState, request.WriteInkedValues, progress, cancellationToken);
        }
        catch
        {
            // Cancelled, OneNote gone or an unexpected failure part way through: the outlines written so far are still
            // recorded, or the next run would find them unowned and block their own slots. A run that did not finish
            // confirms no inked value.
            SaveWritten(opened.SidecarPath, sidecar, writeState, calendar, sidecar.ConfirmedInked, afterFailure: true);
            throw;
        }

        (sidecar, string? saveError) = SaveWritten(opened.SidecarPath, sidecar, writeState, calendar, Propagator.ConfirmedInked(values, sidecar, request.WriteInkedValues), afterFailure: false);

        RefreshSummary summary = new()
        {
            Values = values,
            Changes = written.Changes,
            PagesTouched = written.PagesTouched,
            ValuesWritten = written.ValuesWritten,
            EventsWritten = written.EventsWritten,
            Skipped = written.Changes.Count(c => c.Blocked),
            FailedSources = calendar?.FailedSources ?? Array.Empty<string>(),
            SidecarRebuilt = recovered.Rebuilt,
            Warnings = warnings.Concat(written.Warnings).ToList(),
            PageTitles = titles,
            FrozenThrough = sidecar.FrozenThrough,
        };

        if (written.Error is not null || saveError is not null)
        {
            string error = string.Join(" ", new[] { written.Error, saveError }.OfType<string>());
            return new RefreshResult { Success = false, ExitCode = WriteFailureExitCode, Summary = summary, Error = error, Elapsed = clock.Elapsed, LogPath = services.LogPath };
        }

        progress?.Report("Syncing");
        services.App.SyncHierarchy(opened.Notebook.Id);
        services.Log.Information("Refresh done: {Pages} pages touched, {Values} values, {Events} event slots, {Skipped} skipped", summary.PagesTouched, summary.ValuesWritten, summary.EventsWritten, summary.Skipped);
        return new RefreshResult { Success = !allSourcesFailed, ExitCode = exitCode, Summary = summary, Elapsed = clock.Elapsed, LogPath = services.LogPath };
    }

    /// <summary>
    /// The window calendar events are placed with. It must be the one the daily pages were generated with, because their
    /// Time Blocks rows are baked art: the sidecar's when it records a valid one, else the window typed on the Setup page
    /// (sidecars written before the window was recorded), else the default.
    /// </summary>
    public static (TimeBlockWindow Window, TimeBlockWindowSource Source) CalendarWindow(Sidecar sidecar, SetupValues values)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(values);
        if (sidecar.TimeBlocks is { IsValid: true } recorded)
        {
            return (recorded, TimeBlockWindowSource.Sidecar);
        }

        if (values.TimeBlockWindow is (int start, int end) && new TimeBlockWindow(start, end).IsValid)
        {
            return (new TimeBlockWindow(start, end), TimeBlockWindowSource.SetupPage);
        }

        return (TimeBlockWindow.Default, TimeBlockWindowSource.Default);
    }

    /// <summary><see cref="CalendarWindow"/>, logging which window was used and why.</summary>
    private TimeBlockWindow ChooseWindow(Sidecar sidecar, SetupValues values)
    {
        (TimeBlockWindow window, TimeBlockWindowSource source) = CalendarWindow(sidecar, values);
        if (sidecar.TimeBlocks is { IsValid: false } invalid)
        {
            services.Log.Warning("The sidecar's time-block window {Start} to {End} is not valid and was ignored", invalid.StartHour, invalid.EndHour);
        }

        string from = source switch
        {
            TimeBlockWindowSource.Sidecar => "the sidecar (the window the pages were generated with)",
            TimeBlockWindowSource.SetupPage => "the Setup page (the sidecar records no window)",
            _ => "the default (neither the sidecar nor the Setup page gives one)",
        };
        services.Log.Information("Time-block window {Start} to {End} from {Source:l}", window.StartHour, window.EndHour, from);
        if (source == TimeBlockWindowSource.Sidecar && values.TimeBlockWindow is (int start, int end) && (start, end) != (window.StartHour, window.EndHour))
        {
            services.Log.Information("The Setup page asks for {Start} to {End}; the pages keep their window until the next Generate", start, end);
        }

        return window;
    }

    /// <summary>
    /// The sidecar with what the write phase recorded (anchors and the texts written), LastRefresh, FrozenThrough and
    /// the confirmed inked values, saved: up to <see cref="SidecarSaveAttempts"/> times with short waits, since a sync
    /// client or a virus scanner may hold the file for a moment. When every attempt fails the error to report is returned
    /// (<see cref="SidecarSaveFailedFormat"/>, naming the path); after a failure part way through it is only logged, so the
    /// failure itself still reaches the result.
    /// </summary>
    private (Sidecar Updated, string? Error) SaveWritten(string path, Sidecar sidecar, WriteState state, CalendarOutcome? calendar, IReadOnlyDictionary<string, string> confirmedInked, bool afterFailure)
    {
        Sidecar updated = sidecar with
        {
            Pages = state.Pages(sidecar),
            LastRefresh = services.Clock(),
            FrozenThrough = calendar?.FrozenThrough ?? sidecar.FrozenThrough,
            ConfirmedInked = confirmedInked,
        };
        Exception? failure = SaveWithRetry(path, updated);
        if (failure is null)
        {
            if (afterFailure)
            {
                services.Log.Information("The run stopped part way; the objects written so far were recorded in {Path}", path);
            }

            return (updated, null);
        }

        services.Log.Error(failure, "The sidecar {Path} could not be saved after {Attempts} attempts; the next run re-adopts the objects this run wrote", path, SidecarSaveAttempts);
        return (updated, string.Format(CultureInfo.InvariantCulture, SidecarSaveFailedFormat, path, SidecarSaveAttempts, failure.Message));
    }

    /// <summary>Saves the sidecar, trying again after a short wait when the file cannot be written; returns the last error, or null once saved.</summary>
    private Exception? SaveWithRetry(string path, Sidecar sidecar)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                services.Sidecars.Save(path, sidecar);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= SidecarSaveAttempts)
                {
                    return ex;
                }

                services.Log.Warning(ex, "Saving the sidecar {Path} failed (attempt {Attempt} of {Attempts}); trying again", path, attempt, SidecarSaveAttempts);
                (services.Sleep ?? Thread.Sleep)(SidecarSaveWaits[attempt - 1]);
            }
        }
    }

    /// <summary>
    /// Writes the unblocked changes page by page (<see cref="WritePage"/>); inked values are skipped unless confirmed; the
    /// first OneNote failure stops the run with the page named. What each page write did is recorded in
    /// <paramref name="state"/> as soon as it is done, so a run that stops part way still saves it. When page writes were
    /// made and OneNote refused every one of them as a conflict, the run fails (<see cref="AllWritesConflictedFormat"/>):
    /// a run that only some conflicts held back still succeeds.
    /// </summary>
    private WriteOutcome WriteAll(
        List<PlannedChange> changes,
        HashSet<PlannedChange> calendarChanges,
        NotebookPlan plan,
        SlotCatalog catalog,
        PageCache pages,
        WriteState state,
        bool writeInked,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        List<PlannedChange> outcome = new();
        List<string> warnings = new();
        int touched = 0;
        int valuesWritten = 0;
        int eventsWritten = 0;
        int attempted = 0;
        int conflicted = 0;
        string? error = null;

        foreach (IGrouping<PageKey, PlannedChange> group in changes.GroupBy(c => c.Page))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<PlannedChange> pending = new();
            foreach (PlannedChange change in group)
            {
                if (change.Blocked)
                {
                    outcome.Add(change);
                    continue;
                }

                if (change.Inked && !writeInked)
                {
                    outcome.Add(change with { Blocked = true, Reason = InkedSkipReason });
                    continue;
                }

                if (error is not null)
                {
                    outcome.Add(change with { Blocked = true, Reason = "Not written: an earlier page failed." });
                    continue;
                }

                pending.Add(change);
            }

            if (pending.Count == 0)
            {
                continue;
            }

            PageSpec? spec = plan.FindPage(group.Key);
            string? pageId = pages.PageId(group.Key);
            if (spec is null || pageId is null)
            {
                outcome.AddRange(pending.Select(c => c with { Blocked = true, Reason = Propagator.UnreadablePageReason }));
                continue;
            }

            progress?.Report("Writing " + spec.Title);
            PageWrite page;
            try
            {
                page = WritePage(spec, pageId, pending, catalog, pages, state);
            }
            catch (OneNoteUnavailableException)
            {
                throw;
            }
            catch (OneNoteException ex)
            {
                services.Log.Error(ex, "Writing page {Title} failed", spec.Title);
                error = $"Writing page '{spec.Title}' failed: {ex.Message}";
                outcome.AddRange(pending.Select(c => c with { Blocked = true, Reason = error }));
                continue;
            }

            outcome.AddRange(page.Changes);
            warnings.AddRange(page.Warnings);
            touched += page.Touched ? 1 : 0;
            attempted += page.Attempted ? 1 : 0;
            conflicted += page.Conflicted ? 1 : 0;
            foreach ((PlannedChange planned, PlannedChange written) in page.Written.Where(w => w.Written.Kind != ChangeKind.Remove))
            {
                if (calendarChanges.Contains(planned))
                {
                    eventsWritten++;
                }
                else
                {
                    valuesWritten++;
                }
            }
        }

        if (error is null && attempted > 0 && conflicted == attempted)
        {
            error = string.Format(CultureInfo.InvariantCulture, AllWritesConflictedFormat, conflicted);
            services.Log.Error("Every page write was refused as a conflict ({Count} pages); check how OneNote compares the dateExpectedLastModified stamp", conflicted);
        }

        return new WriteOutcome(outcome, touched, valuesWritten, eventsWritten, warnings, error);
    }

    /// <summary>
    /// Writes one page's changes. The page is read again just before the write and every change is decided again
    /// against what the page holds now (<see cref="Propagator.Decide"/> with the run's ownership records): a slot the
    /// user filled or edited since planning is blocked, a change already in place is dropped, and a removal whose outline
    /// is already gone only drops its ID. The write passes the page's lastModifiedTime, so a page the user changes between
    /// this read and the write is not written over; on hrLastModifiedDateDidNotMatch the page is read, re-checked and
    /// written once more, and when it changed again its changes are reported as not written, for the next run. When the
    /// write fails for good after some of its removals were made, the page is read again and the records of the outlines
    /// gone from it are dropped (<see cref="RemovedBeforeFailure"/>), so the sidecar never keeps the ID of a deleted
    /// outline; on a conflict those removals are reported done, any other failure still fails the page.
    /// </summary>
    private PageWrite WritePage(PageSpec spec, string pageId, IReadOnlyList<PlannedChange> pending, SlotCatalog catalog, PageCache pages, WriteState state)
    {
        for (int attempt = 1; ; attempt++)
        {
            pages.Invalidate(spec.Key);
            PageXml? fresh = pages.Read(spec.Key);
            if (fresh is null)
            {
                return new PageWrite(pending.Select(c => c with { Blocked = true, Reason = Propagator.UnreadablePageReason }).ToList(), new(), false, new()) { Attempted = attempt > 1 };
            }

            (List<(PlannedChange Planned, AnchorWrite Write)> writes, List<PlannedChange> settled, List<string> gone) = Recheck(spec, pending, fresh, catalog, state);
            if (writes.Count == 0)
            {
                state.Forget(spec.Key, gone);
                return new PageWrite(settled, new(), false, new()) { Attempted = attempt > 1 };
            }

            AnchorWriteResult result;
            try
            {
                result = services.Writer.Write(pageId, fresh, writes.Select(w => w.Write).ToList());
            }
            catch (OneNoteException ex) when (ex is not OneNoteUnavailableException)
            {
                bool conflict = ex.HResult == OneNoteHResults.LastModifiedDateDidNotMatch;
                if (conflict && attempt == 1)
                {
                    // Removals made before the refusal are found gone by the next attempt's re-check.
                    services.Log.Warning("Page {Title} changed while it was being written; reading it again", spec.Title);
                    continue;
                }

                pages.Invalidate(spec.Key);
                List<AnchorWrite> removed = RemovedBeforeFailure(spec.Key, writes.Select(w => w.Write), pages);
                state.Forget(spec.Key, gone.Concat(removed.Select(w => w.Change.SlotId)));
                if (!conflict)
                {
                    throw;
                }

                services.Log.Warning(ex, "Page {Title} changed again while it was being written; its changes are left for the next run", spec.Title);
                List<PlannedChange> conflicted = settled
                    .Concat(writes.Select(w => removed.Contains(w.Write) ? w.Write.Change : w.Write.Change with { Blocked = true, Reason = ConflictReason }))
                    .ToList();
                return new PageWrite(conflicted, new(), false, new() { $"Page {spec.Title} changed while Refresh was writing it; its changes were not written and will be tried again on the next run." }) { Attempted = true, Conflicted = true };
            }

            pages.Invalidate(spec.Key);
            state.Record(spec.Key, result, writes.Select(w => w.Write).ToList());
            state.Forget(spec.Key, gone);
            List<string> warnings = result.Unresolved
                .Select(slotId => $"Page {spec.Title}: the object written for {slotId} could not be found again; it will be re-planned next run.")
                .ToList();
            return new PageWrite(
                settled.Concat(writes.Select(w => w.Write.Change)).ToList(),
                writes.Select(w => (w.Planned, w.Write.Change)).ToList(),
                true,
                warnings) { Attempted = true };
        }
    }

    /// <summary>
    /// The removals among <paramref name="writes"/> whose outline is no longer on the page, read again after OneNote
    /// refused the write part way: each removal is its own DeletePageContent call made before the one UpdatePageContent,
    /// so some may be done although the write as a whole failed. None when the page cannot be read.
    /// </summary>
    private static List<AnchorWrite> RemovedBeforeFailure(PageKey key, IEnumerable<AnchorWrite> writes, PageCache pages)
    {
        List<AnchorWrite> removals = writes.Where(w => w.Change.Kind == ChangeKind.Remove && !string.IsNullOrEmpty(w.ObjectId)).ToList();
        if (removals.Count == 0)
        {
            return removals;
        }

        PageXml? now = pages.Read(key);
        if (now is null)
        {
            return new List<AnchorWrite>();
        }

        HashSet<string> present = now.Outlines.Select(o => o.ObjectId).ToHashSet(StringComparer.Ordinal);
        return removals.Where(w => !present.Contains(w.ObjectId!)).ToList();
    }

    /// <summary>
    /// Decides every pending change of a page again against the page as just read: the writes still to make (with their
    /// geometry and owned object ID), the changes settled without a write (blocked now, or a removal whose outline is
    /// already gone) and the slots whose outline is gone (their IDs are dropped).
    /// </summary>
    private (List<(PlannedChange Planned, AnchorWrite Write)> Writes, List<PlannedChange> Settled, List<string> Gone) Recheck(
        PageSpec spec, IReadOnlyList<PlannedChange> pending, PageXml fresh, SlotCatalog catalog, WriteState state)
    {
        IReadOnlyDictionary<string, string> anchors = state.Anchors(spec.Key);
        IReadOnlyDictionary<string, string> texts = state.Texts(spec.Key);
        HashSet<string> ownedIds = anchors.Values.ToHashSet(StringComparer.Ordinal);
        List<(PlannedChange Planned, AnchorWrite Write)> writes = new();
        List<PlannedChange> settled = new();
        List<string> gone = new();
        foreach (PlannedChange change in pending)
        {
            SlotGeometry? geometry = catalog.Slot(spec, change.SlotId);
            if (geometry is null)
            {
                settled.Add(change with { Blocked = true, Reason = "The page has no slot for this change." });
                continue;
            }

            string? ownedId = anchors.TryGetValue(change.SlotId, out string? id) ? id : null;
            string? written = texts.TryGetValue(change.SlotId, out string? last) ? last : null;
            SlotState slot = SlotInspector.Inspect(fresh, geometry.Rect, ownedId, ownedIds, catalog.Manifest.PageWidthPt);
            PlannedChange? now = Propagator.Decide(change.Page, change.SlotId, change.Kind == ChangeKind.Remove ? "" : change.NewText, slot, written);
            if (now is null)
            {
                if (change.Kind == ChangeKind.Remove && slot.Owned is null)
                {
                    gone.Add(change.SlotId);
                    settled.Add(change);
                }
                else
                {
                    services.Log.Information("Page {Title}: {Slot} already holds its text; nothing to write", spec.Title, change.SlotId);
                }

                continue;
            }

            now = now with { Inked = change.Inked };
            if (now.Blocked)
            {
                services.Log.Information("Page {Title}: {Slot} changed since planning and is left alone: {Reason}", spec.Title, change.SlotId, now.Reason);
                settled.Add(now);
                continue;
            }

            writes.Add((change, new AnchorWrite(now, geometry, now.Kind == ChangeKind.Create ? null : ownedId)));
        }

        return (writes, settled, gone);
    }

    /// <summary>
    /// Fetches the Setup sources (plus the bundled holidays), places the events and diffs them against what the tool wrote
    /// last. Whether every source failed (exit 6, the failure streak) is decided by the Setup sources alone: the bundled
    /// holiday list is local and added by the tool, so it would otherwise keep a dead calendar feed from ever counting.
    /// </summary>
    private CalendarOutcome? PlanCalendar(
        RefreshRequest request,
        SetupValues values,
        Sidecar sidecar,
        NotebookPlan plan,
        SlotCatalog catalog,
        PageCache pages,
        RunState state,
        TimeBlockWindow window,
        List<string> warnings)
    {
        if (services.CalendarSources is null)
        {
            warnings.Add("Calendar import is not available in this build; propagation only.");
            return null;
        }

        List<CalendarSource> setupSources = values.Sources.ToList();
        List<CalendarSource> bundled = new();
        if (services.IncludeBundledHolidays && !setupSources.Any(s => s.Kind == CalendarSourceKind.Holidays))
        {
            bundled.Add(new CalendarSource("Holidays", CalendarSourceKind.Holidays, HolidayCalendarSource.DefaultListId, null, null));
        }

        DateOnly today = request.Today;
        int horizon = Math.Max(0, request.HorizonDays);
        DateOnly frozen = sidecar.FrozenThrough is DateOnly f && f > today.AddDays(-1) ? f : today.AddDays(-1);
        DateOnly from = Max(frozen.AddDays(1), plan.Quarter.Start);
        DateOnly to = Min(today.AddDays(horizon), plan.Quarter.End);
        if (to < from)
        {
            warnings.Add($"No calendar days to write: the window {from:yyyy-MM-dd} to {to:yyyy-MM-dd} lies outside the quarter or behind the frozen days.");
            return new CalendarOutcome(Array.Empty<PlannedChange>(), frozen, Array.Empty<string>(), false);
        }

        CalendarFetchResult setup = CalendarImportRunner.Fetch(setupSources, services.CalendarSources, from, to, services.Log);
        CalendarFetchResult tool = CalendarImportRunner.Fetch(bundled, services.CalendarSources, from, to, services.Log);
        CalendarFetchResult fetched = new(
            setup.Events.Concat(tool.Events).OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal).ToList(),
            setup.Failures.Concat(tool.Failures).ToList(),
            setup.SourceCount + tool.SourceCount);
        state.SetupFetch = setup;

        IReadOnlyList<string> failedSources = fetched.Failures.Select(f => f.Label + ": " + f.Message).ToList();
        if (setup.AllSourcesFailed)
        {
            warnings.Add("Every calendar source failed; the events already on the pages were left as they are.");
            return new CalendarOutcome(Array.Empty<PlannedChange>(), sidecar.FrozenThrough ?? frozen, failedSources, true);
        }

        PlacementEngine engine = new(services.Manifest, window, services.Measure);
        IReadOnlyList<CalendarSlotText> desired = engine.Place(from, to, fetched.Events, PlacementTargets.FromPlan(plan));

        Dictionary<SlotRef, string> previous = new();
        foreach (CalendarSlotText slot in desired)
        {
            PageXml? xml = pages.Read(slot.Page);
            if (xml is null || !sidecar.Pages.TryGetValue(slot.Page.Value, out SidecarPage? record) || !record.Anchors.TryGetValue(slot.SlotId, out string? ownedId))
            {
                continue;
            }

            OutlineInfo? owned = xml.Outlines.FirstOrDefault(o => string.Equals(o.ObjectId, ownedId, StringComparison.Ordinal));
            if (owned is not null)
            {
                previous[slot.Ref] = owned.PlainText;
            }
        }

        if (fetched.AnyFailed)
        {
            // A failed source's events are unknown this run, so every line a slot holds stays and the slot only gains
            // the new lines it has room for (M4.6): an Update never drops the dead source's lines.
            desired = engine.Place(from, to, fetched.Events, PlacementTargets.FromPlan(plan), previous);
        }

        CalendarDiffResult diff = CalendarDiff.Diff(today, horizon, sidecar.FrozenThrough, previous, desired);
        List<PlannedChange> changes = new();
        foreach (PlannedChange change in diff.Changes)
        {
            if (fetched.AnyFailed && DropsLines(change))
            {
                continue;
            }

            changes.Add(Check(change, plan, catalog, pages, sidecar));
        }

        if (fetched.AnyFailed)
        {
            warnings.Add(PartialSourceFailureWarning);
        }

        services.Log.Information("Calendar: {Events} events from {From} to {To}, {Changes} slot changes ({Creates} create, {Updates} update, {Removes} remove)", fetched.Events.Count, from, to, changes.Count, diff.Creates, diff.Updates, diff.Removes);
        return new CalendarOutcome(changes, diff.FrozenThrough, failedSources, false);
    }

    /// <summary>The summary warning of a run in which some calendar sources failed and others answered.</summary>
    private const string PartialSourceFailureWarning =
        "Some calendar sources failed; every event line already on the pages was kept, and new events were added only where a slot had room beside them.";

    /// <summary>
    /// A Remove, or an Update that loses one of the old lines (compared as the page reads them back): what a failed
    /// source must never cause. The placement keeps every old line during a partial failure, so this only guards that rule.
    /// </summary>
    private static bool DropsLines(PlannedChange change)
    {
        if (change.Kind == ChangeKind.Remove)
        {
            return true;
        }

        if (change.Kind != ChangeKind.Update || change.OldText is null)
        {
            return false;
        }

        List<string> remaining = change.NewText.Split('\n').Select(CalendarDiff.Comparable).ToList();
        return !change.OldText.Split('\n').Select(CalendarDiff.Comparable).All(remaining.Remove);
    }

    /// <summary>
    /// Applies the ownership rules to a calendar change: a user object in the slot, a moved tool object, or a tool object
    /// whose text the user edited (it no longer holds the text the tool last wrote) blocks it.
    /// </summary>
    private static PlannedChange Check(PlannedChange change, NotebookPlan plan, SlotCatalog catalog, PageCache pages, Sidecar sidecar)
    {
        PageSpec? spec = plan.FindPage(change.Page);
        PageXml? xml = pages.Read(change.Page);
        if (spec is null || xml is null)
        {
            return change with { Blocked = true, Reason = Propagator.UnreadablePageReason };
        }

        SlotGeometry? geometry = catalog.Slot(spec, change.SlotId);
        if (geometry is null)
        {
            return change with { Blocked = true, Reason = "The page has no slot for this day (a stacked sixth-row day shares its box)." };
        }

        sidecar.Pages.TryGetValue(change.Page.Value, out SidecarPage? record);
        string? ownedId = record is not null && record.Anchors.TryGetValue(change.SlotId, out string? id) ? id : null;
        SlotState state = SlotInspector.Inspect(xml, geometry.Rect, ownedId, AnnualPageFallback.OwnedIds(sidecar, change.Page), catalog.Manifest.PageWidthPt);
        if (state.Moved)
        {
            return change with { Blocked = true, Reason = Propagator.MovedReason };
        }

        string? written = record is not null && record.AnchorTexts.TryGetValue(change.SlotId, out string? last) ? last : null;
        if (state.Owned is not null && Propagator.IsEdited(state.OwnedText, written))
        {
            return change with { Blocked = true, Reason = Propagator.EditedReason };
        }

        if (change.Kind != ChangeKind.Remove && state.IsBlocked)
        {
            return change with { Blocked = true, Reason = Propagator.BlockedReason(state) };
        }

        if (change.Kind != ChangeKind.Create && state.Owned is null)
        {
            return change with { Kind = ChangeKind.Create, OldText = null, Blocked = change.Kind == ChangeKind.Remove, Reason = change.Kind == ChangeKind.Remove ? "The tool-owned object is already gone." : null };
        }

        return change;
    }

    /// <summary>
    /// The PRD's "three consecutive failures raise a Windows toast", updated once per real run from how it ended: a run
    /// that succeeded clears the streak; a run whose every Setup calendar source failed (exit 6) or that OneNote failed
    /// (exit 3 or 5) adds one, and from the third in a row on each such run raises the toast. Dry runs, cancelled runs, a
    /// notebook that was not found (exit 2), a run refused because another run of the notebook holds its lock, and runs
    /// whose sidecar path never became known leave the streak alone. The
    /// counter file lives beside the sidecar; failing to update it is logged, never fatal.
    /// </summary>
    private void TrackStreak(RefreshRequest request, RunState state, RefreshResult result)
    {
        if (request.DryRun || state.Cancelled || state.Refused || state.SidecarPath is null)
        {
            return;
        }

        try
        {
            FailureCounter counter = FailureCounter.LoadBeside(state.SidecarPath);
            ISourceFailureNotifier notifier = services.Notifier ?? new LogOnlyNotifier(services);
            DateTimeOffset now = services.Clock();
            switch (result.ExitCode)
            {
                case 0:
                    if (counter.ConsecutiveTotalFailures > 0)
                    {
                        services.Log.Information("Refresh succeeded after {Count} failed runs", counter.ConsecutiveTotalFailures);
                    }

                    counter.Reset(now);
                    counter.Save();
                    break;
                case SourcesUnreachableExitCode when state.SetupFetch is not null:
                    CalendarImportRunner.Track(state.SetupFetch, counter, notifier, now, services.Log);
                    break;
                case OneNoteUnavailableExitCode or WriteFailureExitCode:
                    counter.RecordTotalFailure(now, result.Error);
                    counter.Save();
                    services.Log.Warning("Refresh of {Name} failed ({Streak} runs in a row): {Error}", request.NotebookName, counter.ConsecutiveTotalFailures, result.Error);
                    if (counter.ShouldToast)
                    {
                        notifier.Notify(RefreshFailureToastTitle, string.Format(
                            CultureInfo.InvariantCulture,
                            "Refresh of {0} failed {1} runs in a row. Last error: {2}",
                            request.NotebookName,
                            counter.ConsecutiveTotalFailures,
                            result.Error ?? "unknown"));
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            services.Log.Warning(ex, "The failure counter beside {Path} could not be updated", state.SidecarPath);
        }
    }

    private RefreshResult Fail(int exitCode, string error, Stopwatch clock) =>
        new() { Success = false, ExitCode = exitCode, Error = error, Elapsed = clock.Elapsed, LogPath = services.LogPath };

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private sealed record CalendarOutcome(IReadOnlyList<PlannedChange> Changes, DateOnly FrozenThrough, IReadOnlyList<string> FailedSources, bool AllSourcesFailed);

    /// <summary>What a run learned along the way that the failure streak needs once it has ended, however it ended.</summary>
    private sealed class RunState
    {
        /// <summary>Where the sidecar is (and the failure counter beside it), once known.</summary>
        public string? SidecarPath { get; set; }

        /// <summary>The fetch of the Setup calendar sources alone, when the run imported the calendar.</summary>
        public CalendarFetchResult? SetupFetch { get; set; }

        public bool Cancelled { get; set; }

        /// <summary>The lock beside the sidecar this run holds, released when the run has ended.</summary>
        public RefreshLock? Lock { get; set; }

        /// <summary>True when another run of the notebook held the lock, so this one did not start.</summary>
        public bool Refused { get; set; }
    }

    private sealed record WriteOutcome(
        List<PlannedChange> Changes,
        int PagesTouched,
        int ValuesWritten,
        int EventsWritten,
        List<string> Warnings,
        string? Error);

    /// <summary>What one page write did: every change with its final state, the writes made (as planned, as written), whether OneNote was written, and warnings.</summary>
    private sealed record PageWrite(
        List<PlannedChange> Changes,
        List<(PlannedChange Planned, PlannedChange Written)> Written,
        bool Touched,
        List<string> Warnings)
    {
        /// <summary>True when the page was written to OneNote at least once (whether or not OneNote took it).</summary>
        public bool Attempted { get; init; }

        /// <summary>True when OneNote refused the page's write as a conflict on every attempt, so nothing of it was written.</summary>
        public bool Conflicted { get; init; }
    }

    /// <summary>
    /// The sidecar's ownership records (slot to object ID, slot to the text the tool wrote) as the write phase changes
    /// them, page by page, so the sidecar saved at the end, or after a run that stopped part way, holds every object the
    /// run wrote.
    /// </summary>
    private sealed class WriteState
    {
        private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, Dictionary<string, string>> anchors;
        private readonly Dictionary<string, Dictionary<string, string>> texts;

        public WriteState(Sidecar sidecar)
        {
            anchors = sidecar.Pages.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value.Anchors, StringComparer.Ordinal), StringComparer.Ordinal);
            texts = sidecar.Pages.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value.AnchorTexts, StringComparer.Ordinal), StringComparer.Ordinal);
        }

        /// <summary>Slot id to object ID of the page's tool-owned outlines, as of now.</summary>
        public IReadOnlyDictionary<string, string> Anchors(PageKey page) => anchors.TryGetValue(page.Value, out Dictionary<string, string>? a) ? a : None;

        /// <summary>Slot id to the text the tool last wrote there, as of now.</summary>
        public IReadOnlyDictionary<string, string> Texts(PageKey page) => texts.TryGetValue(page.Value, out Dictionary<string, string>? t) ? t : None;

        /// <summary>Records one page write: created outlines get their ID and text, updated ones their text, removed ones lose both.</summary>
        public void Record(PageKey page, AnchorWriteResult result, IReadOnlyList<AnchorWrite> writes)
        {
            Dictionary<string, string> pageAnchors = Get(anchors, page);
            Dictionary<string, string> pageTexts = Get(texts, page);
            foreach ((string slotId, string objectId) in result.Created)
            {
                pageAnchors[slotId] = objectId;
                pageTexts[slotId] = TextOf(writes, slotId);
            }

            foreach (string slotId in result.Updated)
            {
                pageTexts[slotId] = TextOf(writes, slotId);
            }

            Forget(page, result.Removed);
        }

        /// <summary>Drops the records of slots whose outline is gone.</summary>
        public void Forget(PageKey page, IEnumerable<string> slotIds)
        {
            Dictionary<string, string> pageAnchors = Get(anchors, page);
            Dictionary<string, string> pageTexts = Get(texts, page);
            foreach (string slotId in slotIds)
            {
                pageAnchors.Remove(slotId);
                pageTexts.Remove(slotId);
            }
        }

        /// <summary>The sidecar's pages with the records as of now.</summary>
        public Dictionary<string, SidecarPage> Pages(Sidecar sidecar) => sidecar.Pages.ToDictionary(
            p => p.Key,
            p => p.Value with
            {
                Anchors = anchors.TryGetValue(p.Key, out Dictionary<string, string>? a) ? new Dictionary<string, string>(a, StringComparer.Ordinal) : p.Value.Anchors,
                AnchorTexts = texts.TryGetValue(p.Key, out Dictionary<string, string>? t) ? new Dictionary<string, string>(t, StringComparer.Ordinal) : p.Value.AnchorTexts,
            },
            StringComparer.Ordinal);

        private static string TextOf(IReadOnlyList<AnchorWrite> writes, string slotId) => writes.First(w => w.Change.SlotId == slotId).Change.NewText;

        private static Dictionary<string, string> Get(Dictionary<string, Dictionary<string, string>> records, PageKey page)
        {
            if (!records.TryGetValue(page.Value, out Dictionary<string, string>? record))
            {
                record = new Dictionary<string, string>(StringComparer.Ordinal);
                records[page.Value] = record;
            }

            return record;
        }
    }

    /// <summary>Used when no notifier is injected: the toast text goes to the log only.</summary>
    private sealed class LogOnlyNotifier : ISourceFailureNotifier
    {
        private readonly RefreshServices services;

        public LogOnlyNotifier(RefreshServices services)
        {
            this.services = services;
        }

        public void Notify(string title, string message) => services.Log.Warning("{Title}: {Message}", title, message);
    }
}
