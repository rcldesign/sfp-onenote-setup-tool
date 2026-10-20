using System.Diagnostics;
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

/// <summary>
/// The production <see cref="IRefreshOrchestrator"/>: opens the notebook, recovers the sidecar, reads Setup (with the
/// annual fallback), plans propagation and, when asked, the calendar changes (sources from Setup plus the bundled
/// holidays, placement through the engine, previous texts read from the tool-owned outlines, freeze and diff with the
/// sidecar's FrozenThrough), then either lists every change (dry run, nothing written) or writes them page by page
/// through the anchor writer, updates the sidecar (anchors, LastRefresh, FrozenThrough), syncs and fills the summary.
/// Exit codes: 0 success, 2 notebook not found, 3 OneNote unavailable, 5 write failure, 6 every calendar source failed.
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
        try
        {
            return RunCore(request, progress, cancellationToken, clock);
        }
        catch (OneNoteUnavailableException ex)
        {
            services.Log.Error(ex, "OneNote is unavailable");
            return Fail(OneNoteUnavailableExitCode, ex.Message, clock);
        }
        catch (OperationCanceledException)
        {
            services.Log.Information("Refresh cancelled");
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

    private RefreshResult RunCore(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken, Stopwatch clock)
    {
        List<string> warnings = new();
        Manifest manifest = services.Manifest;

        progress?.Report("Opening notebook " + request.NotebookName);
        NotebookOpener opener = new(services.App, services.Sidecars, services.Log, services.OpenTimeout, services.Sleep);
        OpenedNotebook? opened = opener.Open(request.NotebookName, request.NotebooksFolder, services.SearchFolders);
        if (opened is null)
        {
            return Fail(NotFoundExitCode, $"Notebook '{request.NotebookName}' was not found: no sidecar names it, OneNote does not list it and no folder holds it. Generate it first, or pass --folder with the folder that holds it.", clock);
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
        NotebookPlan plan = services.Plans.Build(manifest, quarter, new GenerateOptions { Start = sidecar.Quarter });
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
        TimeBlockWindow window = values.TimeBlockWindow is (int start, int end) && new TimeBlockWindow(start, end).IsValid
            ? new TimeBlockWindow(start, end)
            : TimeBlockWindow.Default;
        SlotCatalog catalog = new(manifest, services.Dates, quarter, window);

        progress?.Report("Planning propagation");
        IPropagator propagator = services.Propagators(catalog, pages.Read);
        List<PlannedChange> changes = propagator.Plan(plan, values, sidecar).ToList();
        HashSet<PlannedChange> calendarChanges = new(ReferenceEqualityComparer.Instance);

        CalendarOutcome? calendar = null;
        if (request.Calendar)
        {
            progress?.Report("Importing calendar events");
            calendar = PlanCalendar(request, values, sidecar, plan, catalog, pages, opened.SidecarPath, window, warnings);
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
        WriteOutcome written = WriteAll(changes, calendarChanges, plan, catalog, pages, request.WriteInkedValues, progress, cancellationToken);
        sidecar = sidecar with
        {
            Pages = written.Pages,
            LastRefresh = services.Clock(),
            FrozenThrough = calendar?.FrozenThrough ?? sidecar.FrozenThrough,
        };
        services.Sidecars.Save(opened.SidecarPath, sidecar);

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

        if (written.Error is not null)
        {
            return new RefreshResult { Success = false, ExitCode = WriteFailureExitCode, Summary = summary, Error = written.Error, Elapsed = clock.Elapsed, LogPath = services.LogPath };
        }

        progress?.Report("Syncing");
        services.App.SyncHierarchy(opened.Notebook.Id);
        services.Log.Information("Refresh done: {Pages} pages touched, {Values} values, {Events} event slots, {Skipped} skipped", summary.PagesTouched, summary.ValuesWritten, summary.EventsWritten, summary.Skipped);
        return new RefreshResult { Success = !allSourcesFailed, ExitCode = exitCode, Summary = summary, Elapsed = clock.Elapsed, LogPath = services.LogPath };
    }

    /// <summary>Writes the unblocked changes page by page; inked values are skipped unless confirmed; the first OneNote failure stops the run with the page named.</summary>
    private WriteOutcome WriteAll(
        List<PlannedChange> changes,
        HashSet<PlannedChange> calendarChanges,
        NotebookPlan plan,
        SlotCatalog catalog,
        PageCache pages,
        bool writeInked,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Dictionary<string, string>> anchors = pages.Sidecar.Pages.ToDictionary(
            p => p.Key, p => new Dictionary<string, string>(p.Value.Anchors, StringComparer.Ordinal), StringComparer.Ordinal);
        List<PlannedChange> outcome = new();
        List<string> warnings = new();
        int touched = 0;
        int valuesWritten = 0;
        int eventsWritten = 0;
        string? error = null;

        foreach (IGrouping<PageKey, PlannedChange> group in changes.GroupBy(c => c.Page))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<AnchorWrite> writes = new();
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

                anchors.TryGetValue(group.Key.Value, out Dictionary<string, string>? pageAnchors);
                string? ownedId = pageAnchors is not null && pageAnchors.TryGetValue(change.SlotId, out string? id) ? id : null;
                PageSpec? spec = plan.FindPage(group.Key);
                SlotGeometry? geometry = spec is null ? null : catalog.Slot(spec, change.SlotId);
                writes.Add(new AnchorWrite(change, geometry, ownedId));
            }

            if (writes.Count == 0)
            {
                continue;
            }

            string? pageId = pages.PageId(group.Key);
            PageXml? current = pages.Read(group.Key);
            if (pageId is null || current is null)
            {
                outcome.AddRange(writes.Select(w => w.Change with { Blocked = true, Reason = Propagator.UnreadablePageReason }));
                continue;
            }

            string title = plan.FindPage(group.Key)?.Title ?? group.Key.Value;
            progress?.Report("Writing " + title);
            AnchorWriteResult result;
            try
            {
                result = services.Writer.Write(pageId, current, writes);
            }
            catch (OneNoteUnavailableException)
            {
                throw;
            }
            catch (OneNoteException ex)
            {
                services.Log.Error(ex, "Writing page {Title} failed", title);
                error = $"Writing page '{title}' failed: {ex.Message}";
                outcome.AddRange(writes.Select(w => w.Change with { Blocked = true, Reason = error }));
                continue;
            }

            pages.Invalidate(group.Key);
            touched++;
            Dictionary<string, string> updatedAnchors = anchors.TryGetValue(group.Key.Value, out Dictionary<string, string>? existing)
                ? existing
                : new Dictionary<string, string>(StringComparer.Ordinal);
            anchors[group.Key.Value] = updatedAnchors;
            foreach ((string slotId, string objectId) in result.Created)
            {
                updatedAnchors[slotId] = objectId;
            }

            foreach (string slotId in result.Removed)
            {
                updatedAnchors.Remove(slotId);
            }

            foreach (string slotId in result.Unresolved)
            {
                warnings.Add($"Page {title}: the object written for {slotId} could not be found again; it will be re-planned next run.");
            }

            foreach (AnchorWrite write in writes)
            {
                outcome.Add(write.Change);
                if (write.Change.Kind != ChangeKind.Remove)
                {
                    if (calendarChanges.Contains(write.Change))
                    {
                        eventsWritten++;
                    }
                    else
                    {
                        valuesWritten++;
                    }
                }
            }
        }

        Dictionary<string, SidecarPage> pagesWithAnchors = pages.Sidecar.Pages.ToDictionary(
            p => p.Key,
            p => anchors.TryGetValue(p.Key, out Dictionary<string, string>? a) ? p.Value with { Anchors = a } : p.Value,
            StringComparer.Ordinal);
        return new WriteOutcome(outcome, pagesWithAnchors, touched, valuesWritten, eventsWritten, warnings, error);
    }

    /// <summary>Fetches the Setup sources (plus the bundled holidays), places the events and diffs them against what the tool wrote last.</summary>
    private CalendarOutcome? PlanCalendar(
        RefreshRequest request,
        SetupValues values,
        Sidecar sidecar,
        NotebookPlan plan,
        SlotCatalog catalog,
        PageCache pages,
        string sidecarPath,
        TimeBlockWindow window,
        List<string> warnings)
    {
        if (services.CalendarSources is null)
        {
            warnings.Add("Calendar import is not available in this build; propagation only.");
            return null;
        }

        List<CalendarSource> sources = values.Sources.ToList();
        if (services.IncludeBundledHolidays && !sources.Any(s => s.Kind == CalendarSourceKind.Holidays))
        {
            sources.Add(new CalendarSource("Holidays", CalendarSourceKind.Holidays, HolidayCalendarSource.DefaultListId, null, null));
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

        CalendarFetchResult fetched = CalendarImportRunner.Fetch(sources, services.CalendarSources, from, to, services.Log);
        if (!request.DryRun)
        {
            FailureCounter counter = FailureCounter.LoadBeside(sidecarPath);
            CalendarImportRunner.Track(fetched, counter, services.Notifier ?? new LogOnlyNotifier(services), services.Clock(), services.Log);
        }

        IReadOnlyList<string> failedSources = fetched.Failures.Select(f => f.Label + ": " + f.Message).ToList();
        if (fetched.AllSourcesFailed)
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

        CalendarDiffResult diff = CalendarDiff.Diff(today, horizon, sidecar.FrozenThrough, previous, desired);
        List<PlannedChange> changes = new();
        foreach (PlannedChange change in diff.Changes)
        {
            if (fetched.AnyFailed && OnlyDropsLines(change))
            {
                continue;
            }

            changes.Add(Check(change, plan, catalog, pages, sidecar));
        }

        if (fetched.AnyFailed)
        {
            warnings.Add("Some calendar sources failed; their events were kept on the pages and no event lines were removed this run.");
        }

        services.Log.Information("Calendar: {Events} events from {From} to {To}, {Changes} slot changes ({Creates} create, {Updates} update, {Removes} remove)", fetched.Events.Count, from, to, changes.Count, diff.Creates, diff.Updates, diff.Removes);
        return new CalendarOutcome(changes, diff.FrozenThrough, failedSources, false);
    }

    /// <summary>A Remove, or an Update whose new lines are all among the old ones (only lines dropped), which a failed source must not cause.</summary>
    private static bool OnlyDropsLines(PlannedChange change)
    {
        if (change.Kind == ChangeKind.Remove)
        {
            return true;
        }

        if (change.Kind != ChangeKind.Update || change.OldText is null)
        {
            return false;
        }

        HashSet<string> old = change.OldText.Split('\n').ToHashSet(StringComparer.Ordinal);
        return change.NewText.Split('\n').All(old.Contains);
    }

    /// <summary>Applies the ownership rules to a calendar change: a user object in the slot or a moved tool object blocks it.</summary>
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

    private RefreshResult Fail(int exitCode, string error, Stopwatch clock) =>
        new() { Success = false, ExitCode = exitCode, Error = error, Elapsed = clock.Elapsed, LogPath = services.LogPath };

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private sealed record CalendarOutcome(IReadOnlyList<PlannedChange> Changes, DateOnly FrozenThrough, IReadOnlyList<string> FailedSources, bool AllSourcesFailed);

    private sealed record WriteOutcome(
        List<PlannedChange> Changes,
        Dictionary<string, SidecarPage> Pages,
        int PagesTouched,
        int ValuesWritten,
        int EventsWritten,
        List<string> Warnings,
        string? Error);

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
