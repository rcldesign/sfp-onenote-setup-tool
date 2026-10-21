using System.Diagnostics;
using System.Globalization;
using Serilog;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Generate;

/// <summary>Run-wide settings of the orchestrator that are not part of the request: font, zone, version, log, parallelism, clock.</summary>
public sealed record GenerateOrchestratorOptions
{
    /// <summary>The typeface in use for anchors (the font service's FamilyInUse).</summary>
    public required string FontFamily { get; init; }

    /// <summary>Zone for one:Page dateTime. The CLI passes the local zone; tests pass a fixed one.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>Version written into the sidecar.</summary>
    public string ToolVersion { get; init; } = Generate.ToolVersion.Current;

    /// <summary>This run's log file; rejected page XML is saved beside it.</summary>
    public string? LogPath { get; init; }

    /// <summary>The most render workers the default asks for (shared with Sfp.Render).</summary>
    public const int MaxDefaultParallelism = RenderParallelism.MaxDefault;

    /// <summary>Render workers for the notebook pipeline and for render-only; <see cref="DefaultParallelism"/> unless set.</summary>
    public int MaxDegreeOfParallelism { get; init; } = DefaultParallelism;

    /// <summary>
    /// The default worker count: the processor count capped at <see cref="MaxDefaultParallelism"/>. Sfp.Render exposes no
    /// parallelism default to share yet, so the cap lives here: the pipeline holds <see cref="RenderPipeline.DefaultCapacity"/>
    /// (4) pages at once, so more workers would only wait for a slot, the rasterizer serializes PDFium anyway, and every
    /// worker holds sheet bitmaps of about 32 MB at 220 DPI.
    /// </summary>
    public static int DefaultParallelism => RenderParallelism.Default;

    /// <summary>The default worker count on a machine with <paramref name="processorCount"/> processors: 1 to <see cref="MaxDefaultParallelism"/>.</summary>
    public static int DefaultParallelismFor(int processorCount) => RenderParallelism.For(processorCount);

    /// <summary>Rendered pages held at once.</summary>
    public int RenderBufferCapacity { get; init; } = RenderPipeline.DefaultCapacity;

    /// <summary>The clock for the sidecar's Generated stamp; injected so tests are deterministic.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;
}

/// <summary>
/// The production <see cref="IGenerateOrchestrator"/>. Phases: preflight, plan, then for render-only the PNG files and
/// pages.json through the render-only verb's <see cref="RenderOnlyRunner"/>; otherwise create the notebook (its first
/// OpenHierarchy decides the PRD's not-signed-in case), every section (each section group once, before its first
/// section), every page, the hierarchy per section and every hyperlink first (page IDs must exist before any content
/// embeds them), then write each page as its render arrives from the parallel pipeline on this one thread, the
/// Contents page, the sidecar, the previous notebook's Next quarter link (with --previous; a warning, never a
/// failure), SyncHierarchy and NavigateTo the cover.
/// Cancellation is checked between pages; on cancel, and after a page fails to render, the partial notebook is closed
/// and its folder deleted, so the same command can run again. When OneNote does not close it, the folder is left in
/// place (OneNote still lists the notebook there) and a warning says to close it in OneNote and delete the folder.
/// Expected failures never throw: the result names the phase, the page and the HRESULT or message with the PRD exit code.
/// </summary>
public sealed class GenerateOrchestrator : IGenerateOrchestrator
{
    private const int ValidationError = 2;
    private const int OneNoteUnavailable = 3;
    private const int RenderError = 4;
    private const int WriteError = 5;

    private readonly IGeneratePreflight _preflight;
    private readonly IDateEngine _dates;
    private readonly IPlanBuilder _plans;
    private readonly IPageRenderer _renderer;
    private readonly Func<INotebookWriter> _writerFactory;
    private readonly ISidecarStore _sidecars;
    private readonly GenerateOrchestratorOptions _options;
    private readonly ILogger _log;
    private readonly Func<INextQuarterLinkWriter>? _nextQuarterLinks;

    /// <param name="preflight">The pre-run checks.</param>
    /// <param name="dates">Date engine for the quarter.</param>
    /// <param name="plans">Plan builder.</param>
    /// <param name="renderer">Page renderer (called from several threads).</param>
    /// <param name="writerFactory">Creates the notebook writer once preflight has passed; never called for render-only.</param>
    /// <param name="sidecars">Sidecar store.</param>
    /// <param name="options">Run-wide settings.</param>
    /// <param name="log">Run log.</param>
    /// <param name="nextQuarterLinks">
    /// Creates the writer that fills the previous notebook's Next quarter link; called only when the request names a
    /// previous notebook, after the new notebook is complete. Null reports a warning instead.
    /// </param>
    public GenerateOrchestrator(
        IGeneratePreflight preflight,
        IDateEngine dates,
        IPlanBuilder plans,
        IPageRenderer renderer,
        Func<INotebookWriter> writerFactory,
        ISidecarStore sidecars,
        GenerateOrchestratorOptions options,
        ILogger log,
        Func<INextQuarterLinkWriter>? nextQuarterLinks = null)
    {
        _preflight = preflight;
        _dates = dates;
        _plans = plans;
        _renderer = renderer;
        _writerFactory = writerFactory;
        _sidecars = sidecars;
        _options = options;
        _log = log;
        _nextQuarterLinks = nextQuarterLinks;
    }

    public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RunState state = new(request, progress, cancellationToken);
        try
        {
            return RunCore(state);
        }
        catch (OperationCanceledException)
        {
            return Cancel(state);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The orchestrator's contract: expected failures become a result with the PRD exit code, never an exception.
            return Fail(state, ex);
        }
    }

    private GenerateResult RunCore(RunState state)
    {
        GenerateRequest request = state.Request;
        GenerateOptions o = request.Options;

        state.Enter(GeneratePhase.CheckingOneNote);
        PreflightResult preflight = _preflight.Check(request);
        state.Warnings.AddRange(preflight.Warnings);
        state.NoOpenNotebooks = preflight.NoOpenNotebooks;
        if (preflight.NoOpenNotebooks)
        {
            _log.Information("OneNote lists no open notebooks; if creating the notebook fails, OneNote is taken to be not signed in");
        }

        if (!preflight.Ok)
        {
            int exitCode = preflight.Errors[0].ExitCode;
            string message = string.Join(Environment.NewLine, preflight.Errors.Select(e => e.Message));
            _log.Error("Preflight failed: {Errors}", message);
            state.Report(GeneratePhase.Failed, 0, 0, message);
            return new GenerateResult
            {
                Success = false,
                ExitCode = exitCode,
                Phase = exitCode == OneNoteUnavailable ? GeneratePhase.CheckingOneNote : GeneratePhase.Validating,
                Error = message,
                Elapsed = state.Clock.Elapsed,
                LogPath = _options.LogPath,
                Warnings = state.Warnings,
            };
        }

        state.Enter(GeneratePhase.Validating);
        Quarter quarter = _dates.Build(o.Start);
        NotebookPlan plan = _plans.Build(request.Manifest, quarter, o);
        state.PlanPages = plan.Pages.Count();
        List<PageSpec> renderable = plan.Pages.Where(p => p.Type != PageType.Contents).ToList();
        _log.Information("Plan {Name}: {Sections} sections, {Pages} pages ({Renderable} rendered), {Links} links", plan.Name, plan.Sections.Count, plan.Pages.Count(), renderable.Count, plan.Links.Count);
        state.Cancellation.ThrowIfCancellationRequested();

        return request.RenderOnly ? RunRenderOnly(state, plan) : RunNotebook(state, plan, renderable);
    }

    /// <summary>
    /// Render-only goes through <see cref="RenderOnlyRunner"/>, the render-only verb's runner, so both write the same
    /// output: one {key}.png per page that has an image (none for Contents and the blank parent page) and the
    /// pages.json index (<see cref="RenderIndex"/>) listing every page of the plan. A page that fails to render is
    /// named in the result (exit 4); cancellation stops after the pages in flight and writes no pages.json.
    /// </summary>
    private GenerateResult RunRenderOnly(RunState state, NotebookPlan plan)
    {
        string dir = state.Request.RenderOutputDir ?? throw new InvalidOperationException("Render-only needs an output folder.");
        GenerateOptions o = state.Request.Options;
        state.Enter(GeneratePhase.Rendering);
        state.Report(GeneratePhase.Rendering, 0, plan.Pages.Count(), null);
        RenderOnlyRunner runner = new(new PageNamingRenderer(_renderer), Math.Max(1, _options.MaxDegreeOfParallelism));
        RenderOnlyResult rendered = runner.Run(
            plan,
            new RenderOptions(o.PdfPath, o.Dpi, o),
            dir,
            new SynchronousProgress<RenderProgress>(p =>
            {
                _log.Information("Rendered page {Key} ({Title})", p.Key.Value, p.Title);
                state.Report(GeneratePhase.Rendering, p.Done, p.Total, p.Title);
            }),
            state.Cancellation);
        _log.Information("Render-only wrote {Files} PNG files for {Pages} pages and {Index}", rendered.PngFiles, rendered.PagesRendered, rendered.IndexPath);

        state.Enter(GeneratePhase.Finishing);
        state.Enter(GeneratePhase.Done);
        return new GenerateResult
        {
            Success = true,
            ExitCode = 0,
            Phase = GeneratePhase.Done,
            OutputPath = dir,
            NotebookName = plan.Name,
            PagesRendered = rendered.PagesRendered,
            PngFiles = rendered.PngFiles,
            Elapsed = state.Clock.Elapsed,
            OutputBytes = FolderSize(dir),
            LogPath = _options.LogPath,
            Warnings = state.Warnings,
        };
    }

    private GenerateResult RunNotebook(RunState state, NotebookPlan plan, IReadOnlyList<PageSpec> renderable)
    {
        GenerateRequest request = state.Request;
        GenerateOptions o = request.Options;
        CancellationToken ct = state.Cancellation;
        int pageTotal = plan.Pages.Count();

        state.Enter(GeneratePhase.CreatingNotebook);
        INotebookWriter writer = _writerFactory();
        state.Writer = writer;
        string folder = string.IsNullOrWhiteSpace(o.OutputFolder) ? OutputFolders.Default() : o.OutputFolder;
        NotebookHandle nb = CreateNotebook(state, writer, folder, plan.Name);
        state.Notebook = nb;

        // A section group is created once, at the position of its first section, and holds every section that names it.
        Dictionary<string, SectionHandle> sections = new(StringComparer.Ordinal);
        Dictionary<string, SectionGroupHandle> groups = new(StringComparer.Ordinal);
        foreach (SectionSpec section in plan.Sections)
        {
            SectionGroupHandle? group = null;
            if (!string.IsNullOrWhiteSpace(section.GroupName) && !groups.TryGetValue(section.GroupName, out group))
            {
                group = writer.CreateSectionGroup(nb, section.GroupName);
                groups[section.GroupName] = group;
            }

            sections[section.Name] = writer.CreateSection(nb, section, group);
            ct.ThrowIfCancellationRequested();
        }

        Dictionary<PageKey, PageHandle> pages = new();
        int created = 0;
        foreach (SectionSpec section in plan.Sections)
        {
            SectionHandle handle = sections[section.Name];
            foreach (PageSpec page in section.Pages)
            {
                state.CurrentPage = page;
                pages[page.Key] = writer.CreatePage(handle, page);
                state.Report(GeneratePhase.CreatingNotebook, ++created, pageTotal, page.Title);
                ct.ThrowIfCancellationRequested();
            }
        }

        state.CurrentPage = null;
        foreach (SectionSpec section in plan.Sections)
        {
            writer.ApplyHierarchy(sections[section.Name], section.Pages.Select(p => (pages[p.Key], p)).ToList());
            ct.ThrowIfCancellationRequested();
        }

        Dictionary<string, string> sectionHrefs = new(StringComparer.Ordinal);
        Dictionary<PageKey, string> pageHrefs = new();
        foreach (SectionSpec section in plan.Sections)
        {
            sectionHrefs[section.Name] = writer.HyperlinkTo(sections[section.Name]);
            foreach (PageSpec page in section.Pages)
            {
                pageHrefs[page.Key] = writer.HyperlinkTo(pages[page.Key]);
            }

            ct.ThrowIfCancellationRequested();
        }

        state.Enter(GeneratePhase.WritingPages);
        state.Report(GeneratePhase.WritingPages, 0, renderable.Count, null);
        IReadOnlyDictionary<PageKey, IReadOnlyDictionary<string, LinkSpec>> linksByPage = AnchorResolver.LinksByPage(plan);
        Dictionary<PageKey, IReadOnlyDictionary<string, string>> anchorIds = new();
        int written = 0;
        using (RenderPipeline pipeline = CreatePipeline(state, plan, renderable))
        {
            foreach (RenderedPage art in pipeline.Pages())
            {
                PageSpec spec = plan.Page(art.Key);
                state.CurrentPage = spec;
                IReadOnlyDictionary<string, LinkSpec> links = linksByPage.TryGetValue(art.Key, out IReadOnlyDictionary<string, LinkSpec>? l) ? l : AnchorResolver.None;
                IReadOnlyList<ResolvedAnchor> anchors = AnchorResolver.Resolve(spec, art, links, pageHrefs, o.Links, state.Warnings);
                PageHandle handle = pages[art.Key];
                writer.WritePage(handle, spec, art, anchors);
                if (anchors.Count > 0)
                {
                    anchorIds[art.Key] = writer.ReadAnchorIds(handle, anchors);
                }

                written++;
                state.PagesWritten = written;
                state.PagesRendered = pipeline.RenderedCount;
                _log.Information("Wrote page {Key} ({Title}) with {Anchors} anchors", spec.Key.Value, spec.Title, anchors.Count);
                state.Report(GeneratePhase.WritingPages, written, renderable.Count, spec.Title);
                ct.ThrowIfCancellationRequested();
            }
        }

        state.CurrentPage = null;
        state.Enter(GeneratePhase.BuildingContents);
        PageSpec? contents = plan.Pages.FirstOrDefault(p => p.Type == PageType.Contents);
        if (contents is not null)
        {
            state.CurrentPage = contents;
            IReadOnlyList<ContentsSectionEntry> entries = ContentsPageWriter.Entries(plan, sectionHrefs, pageHrefs);
            ContentsPageWriter.Write(writer, pages[contents.Key], contents, entries);
            written++;
            state.CurrentPage = null;
        }

        ct.ThrowIfCancellationRequested();
        state.Enter(GeneratePhase.Finishing);
        Sidecar sidecar = SidecarBuilder.Build(request, plan, nb, sections, pages, anchorIds, _options.ToolVersion, _options.Now());
        string sidecarPath = _sidecars.PathFor(nb.Path);
        _sidecars.Save(sidecarPath, sidecar);
        state.SidecarPath = sidecarPath;
        PageSpec? cover = plan.Pages.FirstOrDefault();
        string? nextQuarterLink = LinkPreviousQuarter(state, nb, cover is null ? null : pageHrefs.GetValueOrDefault(cover.Key));
        writer.Sync(nb);
        if (cover is not null)
        {
            writer.NavigateTo(pages[cover.Key]);
        }

        state.Warnings.AddRange(writer.Warnings);

        state.Enter(GeneratePhase.Done);
        _log.Information("Generate finished: {Pages} pages written to {Path} in {Elapsed}", written, nb.Path, state.Clock.Elapsed);
        return new GenerateResult
        {
            Success = true,
            ExitCode = 0,
            Phase = GeneratePhase.Done,
            OutputPath = nb.Path,
            NotebookName = nb.Name,
            PagesRendered = renderable.Count,
            PagesWritten = written,
            Elapsed = state.Clock.Elapsed,
            OutputBytes = FolderSize(nb.Path),
            LogPath = _options.LogPath,
            Warnings = state.Warnings,
            SidecarPath = sidecarPath,
            NextQuarterLink = nextQuarterLink,
        };
    }

    /// <summary>
    /// The run's first OpenHierarchy (CreateNotebook). The PRD's not-signed-in row has two parts: OneNote lists no open
    /// notebooks (the preflight records it) and this first OpenHierarchy fails. Only then is the failure reported with
    /// the sign-in message and exit 3; with notebooks open it stays a write error (exit 5), and an unavailable OneNote
    /// keeps its own message (exit 3 as well).
    /// </summary>
    private NotebookHandle CreateNotebook(RunState state, INotebookWriter writer, string folder, string name)
    {
        try
        {
            return writer.CreateNotebook(folder, name);
        }
        catch (OneNoteException ex) when (state.NoOpenNotebooks && ex is not OneNoteUnavailableException)
        {
            _log.Warning("OneNote lists no open notebooks and creating the notebook failed ({HResult}); reporting OneNote as not signed in", ex.HResultHex);
            throw new OneNoteUnavailableException(ex.Call ?? "OpenHierarchy", ex.HResult, GenerateText.SignInMessage + " " + ex.Message, ex);
        }
    }

    /// <summary>
    /// The --previous step: points the previous notebook's Next quarter link at the new cover. The new notebook is
    /// complete by now, so nothing here fails the run: every problem becomes a warning. Returns the summary line when
    /// the link was written, else null.
    /// </summary>
    private string? LinkPreviousQuarter(RunState state, NotebookHandle nb, string? coverHref)
    {
        string? previous = state.Request.Options.PreviousNotebook;
        if (string.IsNullOrWhiteSpace(previous))
        {
            return null;
        }

        state.Report(GeneratePhase.Finishing, 0, 0, "Next quarter link on " + previous);
        if (string.IsNullOrEmpty(coverHref))
        {
            state.Warnings.Add(NextQuarterLinkWriter.WarningFor(previous, "the new notebook has no cover page to link to"));
            return null;
        }

        if (_nextQuarterLinks is null)
        {
            state.Warnings.Add(NextQuarterLinkWriter.WarningFor(previous, "this build composes no writer for the previous notebook"));
            return null;
        }

        try
        {
            NextQuarterLinkResult result = _nextQuarterLinks().Link(previous, nb, coverHref);
            if (result.Warning is not null)
            {
                state.Warnings.Add(result.Warning);
            }

            _log.Information("Next quarter link on {Previous}: {Outcome}", previous, result.Outcome);
            return result.Summary;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The link is a courtesy to the previous notebook; an unexpected failure is reported, never fatal.
            _log.Warning(ex, "The Next quarter link step failed for {Previous}", previous);
            state.Warnings.Add(NextQuarterLinkWriter.WarningFor(previous, ex.Message));
            return null;
        }
    }

    private RenderPipeline CreatePipeline(RunState state, NotebookPlan plan, IReadOnlyList<PageSpec> renderable)
    {
        GenerateOptions o = state.Request.Options;
        RenderOptions renderOptions = new(o.PdfPath, o.Dpi, o);
        return new RenderPipeline(
            renderable,
            page => _renderer.Render(page, plan, renderOptions),
            Math.Max(1, _options.MaxDegreeOfParallelism),
            Math.Max(1, _options.RenderBufferCapacity),
            state.Cancellation,
            (page, count) =>
            {
                _log.Information("Rendered page {Key} ({Title})", page.Key.Value, page.Title);
                state.Report(GeneratePhase.Rendering, count, renderable.Count, page.Title);
            });
    }

    private GenerateResult Cancel(RunState state)
    {
        _log.Warning("Generate cancelled during {Phase}", state.Phase);
        if (state.Notebook is not null && state.Writer is not null)
        {
            DeletePartialNotebook(state);
        }

        state.Report(GeneratePhase.Cancelled, 0, 0, "Cancelled");
        return new GenerateResult
        {
            Success = false,
            Cancelled = true,
            ExitCode = 0,
            Phase = GeneratePhase.Cancelled,
            Error = "Cancelled",
            NotebookName = state.Notebook?.Name,
            PagesRendered = state.PagesRendered,
            PagesWritten = state.PagesWritten,
            Elapsed = state.Clock.Elapsed,
            LogPath = _options.LogPath,
            Warnings = state.Warnings,
        };
    }

    /// <summary>
    /// Closes the partial notebook, then deletes its folder and state file (on cancel, and after a render failure). The
    /// close and the delete are reported apart. When OneNote does not close the notebook, nothing is deleted: OneNote
    /// still lists the notebook at that folder, and deleting the folder under it would leave OneNote pointing at nothing;
    /// a warning then tells the user to close the notebook in OneNote and delete the folder before running the same
    /// command again. A folder that cannot be deleted is a warning too. Both warnings are
    /// <see cref="GenerateMessages.PartialNotebookNotDeleted"/>, so the cancelled summaries never say it was deleted.
    /// </summary>
    private PartialCleanup DeletePartialNotebook(RunState state)
    {
        NotebookHandle nb = state.Notebook!;
        try
        {
            state.Writer!.CloseNotebook(nb);
        }
        catch (OneNoteException ex)
        {
            _log.Warning(ex, "CloseNotebook failed while removing the partial notebook ({HResult}); its folder {Path} is left in place", ex.HResultHex, nb.Path);
            state.Warnings.Add(GenerateMessages.PartialNotebookNotDeleted(
                nb.Path,
                "OneNote did not close it, so its folder was left in place. Close the notebook in OneNote and delete the folder before running the same command again. OneNote said: " + ex.Message));
            return PartialCleanup.NotClosed;
        }

        try
        {
            if (Directory.Exists(nb.Path))
            {
                Directory.Delete(nb.Path, recursive: true);
            }

            if (state.SidecarPath is not null && File.Exists(state.SidecarPath))
            {
                File.Delete(state.SidecarPath);
            }

            _log.Information("Partial notebook {Path} deleted", nb.Path);
            return PartialCleanup.Deleted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string note = GenerateMessages.PartialNotebookNotDeleted(nb.Path, ex.Message);
            _log.Information(ex, "Partial notebook {Path} could not be deleted", nb.Path);
            state.Warnings.Add(note);
            return PartialCleanup.NotDeleted;
        }
    }

    private GenerateResult Fail(RunState state, Exception ex)
    {
        GeneratePhase phase = state.Phase;
        PageSpec? page = state.CurrentPage;
        int exitCode;
        string message;
        switch (ex)
        {
            case Exception when DiskSpace.IsDiskFull(ex):
                // PRD error table, "Disk full": the space the run needs; exit 4 while rendering, 5 while writing.
                if (ex is RenderFailedException failedRender)
                {
                    phase = GeneratePhase.Rendering;
                    page = failedRender.Page ?? page;
                }

                exitCode = phase == GeneratePhase.Rendering ? RenderError : WriteError;
                message = DiskFullMessage(state);
                break;
            case RenderFailedException render:
                phase = GeneratePhase.Rendering;
                page = render.Page ?? page;
                exitCode = RenderError;

                // The PRD sentence has one copy (GenerateText.RenderFailed); without a page to name only the reason is left.
                string reason = (render.InnerException ?? render).Message;
                message = page is null ? reason : GenerateText.RenderFailed(page, reason);
                break;
            case OneNoteUnavailableException unavailable:
                exitCode = OneNoteUnavailable;
                message = unavailable.Message;
                break;
            case OneNoteXmlRejectedException rejected:
                exitCode = WriteError;
                message = string.Format(CultureInfo.InvariantCulture, "OneNote rejected the XML for page '{0}' ({1}): {2}", page?.Title ?? "?", rejected.HResultHex, rejected.Message);
                string? saved = SaveRejectedXml(page, rejected.Xml);
                if (saved is not null)
                {
                    message += " The XML was saved to " + saved + ".";
                }

                break;
            case OneNoteException oneNote:
                exitCode = WriteError;
                message = oneNote.Message + " (" + oneNote.HResultHex + ")";
                break;
            case ManifestException or ArgumentOutOfRangeException or ArgumentException or NotImplementedException when phase is GeneratePhase.Validating or GeneratePhase.CheckingOneNote:
                exitCode = ValidationError;
                message = ex.Message;
                break;
            case RenderFolderInUseException inUse:
                // Another render holds the output folder (render-only mode): refused before anything was deleted or
                // written, so it is a usage error like a bad option, as the render-only verb reports it.
                exitCode = ValidationError;
                message = inUse.Message;
                break;
            default:
                exitCode = phase switch
                {
                    GeneratePhase.CheckingOneNote or GeneratePhase.Validating => ValidationError,
                    GeneratePhase.Rendering => RenderError,
                    _ => WriteError,
                };
                message = ex.Message;
                break;
        }

        // A render failure leaves nothing to inspect: the partial notebook is removed as on cancel, so the same command
        // can be run again (the PRD's render error row: exit 4, nothing written to OneNote). When OneNote does not close
        // it, it is left in place (and its path kept) with a warning saying what to do.
        bool partialDeleted = false;
        if (ex is RenderFailedException && !state.Request.RenderOnly && state.Notebook is not null && state.Writer is not null)
        {
            PartialCleanup cleanup = DeletePartialNotebook(state);
            partialDeleted = cleanup == PartialCleanup.Deleted;
            message = message.TrimEnd().TrimEnd('.') + cleanup switch
            {
                PartialCleanup.Deleted => ". The partial notebook was closed and deleted, so the same command can be run again.",
                PartialCleanup.NotClosed => ". The partial notebook was left in place because OneNote did not close it; the warnings say what to do before running the same command again.",
                _ => ". The partial notebook could not be deleted; the warnings say why.",
            };
        }

        string error = string.Format(CultureInfo.InvariantCulture, "{0}{1}: {2}", PhaseLabel(phase), page is null ? "" : ", page " + page.Title, message);
        _log.Error(ex, "Generate failed during {Phase} on page {Page}: {Message}", phase, page?.Title, message);
        state.Report(GeneratePhase.Failed, 0, 0, error);
        return new GenerateResult
        {
            Success = false,
            ExitCode = exitCode,
            Phase = phase,
            OutputPath = partialDeleted ? null : state.Notebook?.Path ?? (state.Request.RenderOnly ? state.Request.RenderOutputDir : null),
            NotebookName = state.Notebook?.Name,
            PagesRendered = 0,
            Error = error,
            FailedPage = page?.Title,
            Elapsed = state.Clock.Elapsed,
            LogPath = _options.LogPath,
            Warnings = state.Warnings,
            SidecarPath = partialDeleted ? null : state.SidecarPath,
        };
    }

    /// <summary>
    /// The disk-full sentence for the run's output folder (the render folder for render-only, else the notebook's parent
    /// folder), sized for the plan's pages at the run's DPI.
    /// </summary>
    private static string DiskFullMessage(RunState state)
    {
        GenerateOptions o = state.Request.Options;
        string folder = state.Request.RenderOnly
            ? state.Request.RenderOutputDir ?? ""
            : string.IsNullOrWhiteSpace(o.OutputFolder) ? OutputFolders.Default() : o.OutputFolder;
        return state.PlanPages is int pages
            ? DiskSpace.Message(folder, pages, o.Dpi)
            : string.Format(CultureInfo.InvariantCulture, "Not enough disk space in {0}.", folder);
    }

    /// <summary>Saves the rejected XML as {page key}.xml beside the log (or in the temp folder when there is no log) and returns the path.</summary>
    private string? SaveRejectedXml(PageSpec? page, string? xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return null;
        }

        string dir = Path.GetDirectoryName(_options.LogPath) is { Length: > 0 } logDir ? logDir : Path.GetTempPath();
        string name = (page?.Key.Value ?? "rejected-page").Replace(':', '-') + ".xml";
        string path = Path.Combine(dir, name);
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, xml);
            _log.Information("Rejected page XML saved to {Path}", path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(ex, "Could not save the rejected XML to {Path}", path);
            return null;
        }
    }

    /// <summary>The phase as the progress bar labels it.</summary>
    public static string PhaseLabel(GeneratePhase phase) => phase switch
    {
        GeneratePhase.CheckingOneNote => "Checking OneNote",
        GeneratePhase.Validating => "Validating",
        GeneratePhase.Rendering => "Rendering pages",
        GeneratePhase.CreatingNotebook => "Creating notebook",
        GeneratePhase.WritingPages => "Writing pages",
        GeneratePhase.BuildingContents => "Building contents",
        GeneratePhase.Finishing => "Finishing",
        GeneratePhase.Done => "Done",
        GeneratePhase.Cancelled => "Cancelled",
        GeneratePhase.Failed => "Failed",
        _ => phase.ToString(),
    };

    /// <summary>Sum of file sizes under a folder, or null when it cannot be read.</summary>
    public static long? FolderSize(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Wraps the renderer for render-only so a page that fails is named (<see cref="RenderFailedException"/>), as the
    /// render pipeline does for a notebook run.
    /// </summary>
    private sealed class PageNamingRenderer : IPageRenderer
    {
        private readonly IPageRenderer _inner;

        public PageNamingRenderer(IPageRenderer inner)
        {
            _inner = inner;
        }

        public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o)
        {
            try
            {
                return _inner.Render(page, plan, o);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                throw new RenderFailedException(page, ex);
            }
        }
    }

    /// <summary>What removing the partial notebook left behind.</summary>
    private enum PartialCleanup
    {
        /// <summary>Closed in OneNote, and its folder and state file deleted.</summary>
        Deleted,

        /// <summary>OneNote did not close it, so nothing was deleted; a warning says what to do.</summary>
        NotClosed,

        /// <summary>Closed, but its folder or state file could not be deleted; a warning says why.</summary>
        NotDeleted,
    }

    /// <summary>Reports on the calling thread (Progress&lt;T&gt; would post to the thread pool and could report after the run ended).</summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public SynchronousProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value) => _report(value);
    }

    /// <summary>Mutable state of one run: where it is, what it holds, what to clean up.</summary>
    private sealed class RunState
    {
        private readonly IProgress<GenerateProgress>? _progress;

        public RunState(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellation)
        {
            Request = request;
            _progress = progress;
            Cancellation = cancellation;
            Clock = Stopwatch.StartNew();
        }

        public GenerateRequest Request { get; }

        public CancellationToken Cancellation { get; }

        public Stopwatch Clock { get; }

        public GeneratePhase Phase { get; private set; } = GeneratePhase.CheckingOneNote;

        public PageSpec? CurrentPage { get; set; }

        public INotebookWriter? Writer { get; set; }

        public NotebookHandle? Notebook { get; set; }

        public string? SidecarPath { get; set; }

        /// <summary>The preflight found no open notebook (half of the PRD's not-signed-in test).</summary>
        public bool NoOpenNotebooks { get; set; }

        /// <summary>Pages rendered so far (the pipeline's count), for the cancelled summary.</summary>
        public int PagesRendered { get; set; }

        /// <summary>Pages written so far, for the cancelled summary.</summary>
        public int PagesWritten { get; set; }

        /// <summary>Pages in the plan once it is built (for the disk-full estimate), else null.</summary>
        public int? PlanPages { get; set; }

        public List<string> Warnings { get; } = new();

        /// <summary>Moves to a phase and reports it (phases without a page counter report 0 of 0).</summary>
        public void Enter(GeneratePhase phase)
        {
            Phase = phase;
            Report(phase, 0, 0, null);
        }

        public void Report(GeneratePhase phase, int current, int total, string? detail) =>
            _progress?.Report(new GenerateProgress(phase, current, total, detail));
    }
}
