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

    /// <summary>Render workers.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>Rendered pages held at once.</summary>
    public int RenderBufferCapacity { get; init; } = RenderPipeline.DefaultCapacity;

    /// <summary>The clock for the sidecar's Generated stamp; injected so tests are deterministic.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;
}

/// <summary>
/// The production <see cref="IGenerateOrchestrator"/>. Phases: preflight, plan, then for render-only the PNG files and
/// pages.json; otherwise create the notebook, every section (each section group once, before its first section), every
/// page, the hierarchy per section and every hyperlink first (page IDs must exist before any content embeds them), then
/// write each page as its render arrives from the parallel pipeline on this one thread, the Contents page, the sidecar,
/// SyncHierarchy and NavigateTo the cover.
/// Cancellation is checked between pages; on cancel the partial notebook is closed and its folder deleted. Expected
/// failures never throw: the result names the phase, the page and the HRESULT or message with the PRD exit code.
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

    /// <param name="preflight">The pre-run checks.</param>
    /// <param name="dates">Date engine for the quarter.</param>
    /// <param name="plans">Plan builder.</param>
    /// <param name="renderer">Page renderer (called from several threads).</param>
    /// <param name="writerFactory">Creates the notebook writer once preflight has passed; never called for render-only.</param>
    /// <param name="sidecars">Sidecar store.</param>
    /// <param name="options">Run-wide settings.</param>
    /// <param name="log">Run log.</param>
    public GenerateOrchestrator(
        IGeneratePreflight preflight,
        IDateEngine dates,
        IPlanBuilder plans,
        IPageRenderer renderer,
        Func<INotebookWriter> writerFactory,
        ISidecarStore sidecars,
        GenerateOrchestratorOptions options,
        ILogger log)
    {
        _preflight = preflight;
        _dates = dates;
        _plans = plans;
        _renderer = renderer;
        _writerFactory = writerFactory;
        _sidecars = sidecars;
        _options = options;
        _log = log;
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
        List<PageSpec> renderable = plan.Pages.Where(p => p.Type != PageType.Contents).ToList();
        _log.Information("Plan {Name}: {Sections} sections, {Pages} pages ({Renderable} rendered), {Links} links", plan.Name, plan.Sections.Count, plan.Pages.Count(), renderable.Count, plan.Links.Count);
        state.Cancellation.ThrowIfCancellationRequested();

        return request.RenderOnly ? RunRenderOnly(state, plan, renderable) : RunNotebook(state, plan, renderable);
    }

    private GenerateResult RunRenderOnly(RunState state, NotebookPlan plan, IReadOnlyList<PageSpec> renderable)
    {
        string dir = state.Request.RenderOutputDir ?? throw new InvalidOperationException("Render-only needs an output folder.");
        Directory.CreateDirectory(dir);
        state.Enter(GeneratePhase.Rendering);
        state.Report(GeneratePhase.Rendering, 0, renderable.Count, null);
        Dictionary<PageKey, string> sectionOf = SectionNames(plan);
        List<PageIndexEntry> entries = new(renderable.Count);
        using (RenderPipeline pipeline = CreatePipeline(state, plan, renderable))
        {
            foreach (RenderedPage art in pipeline.Pages())
            {
                PageSpec spec = plan.Page(art.Key);
                state.CurrentPage = spec;
                entries.Add(RenderOnlyOutput.WritePage(dir, spec, sectionOf[art.Key], art));
                state.Report(GeneratePhase.Rendering, entries.Count, renderable.Count, spec.Title);
                state.Cancellation.ThrowIfCancellationRequested();
            }
        }

        state.CurrentPage = null;
        state.Enter(GeneratePhase.Finishing);
        RenderOnlyOutput.WriteIndex(dir, plan, state.Request.Options.Dpi, entries);
        state.Enter(GeneratePhase.Done);
        return new GenerateResult
        {
            Success = true,
            ExitCode = 0,
            Phase = GeneratePhase.Done,
            OutputPath = dir,
            NotebookName = plan.Name,
            PagesRendered = entries.Count,
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
        NotebookHandle nb = writer.CreateNotebook(folder, plan.Name);
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
        writer.Sync(nb);
        PageSpec? cover = plan.Pages.FirstOrDefault();
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
        };
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
            Elapsed = state.Clock.Elapsed,
            LogPath = _options.LogPath,
            Warnings = state.Warnings,
        };
    }

    private void DeletePartialNotebook(RunState state)
    {
        NotebookHandle nb = state.Notebook!;
        try
        {
            state.Writer!.CloseNotebook(nb);
        }
        catch (OneNoteException ex)
        {
            _log.Warning("CloseNotebook failed while cancelling ({HResult}); deleting the folder anyway", ex.HResultHex);
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string note = string.Format(CultureInfo.InvariantCulture, "The partial notebook at {0} could not be deleted: {1}", nb.Path, ex.Message);
            _log.Warning(ex, "Partial notebook {Path} could not be deleted", nb.Path);
            state.Warnings.Add(note);
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
            case RenderFailedException render:
                phase = GeneratePhase.Rendering;
                page = render.Page ?? page;
                exitCode = RenderError;
                message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Rendering {0} page '{1}' (PDF page {2}) failed: {3}",
                    page?.Type,
                    page?.Title,
                    page?.PdfPage?.ToString(CultureInfo.InvariantCulture) ?? "none",
                    (render.InnerException ?? render).Message);
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

        string error = string.Format(CultureInfo.InvariantCulture, "{0}{1}: {2}", PhaseLabel(phase), page is null ? "" : ", page " + page.Title, message);
        _log.Error(ex, "Generate failed during {Phase} on page {Page}: {Message}", phase, page?.Title, message);
        state.Report(GeneratePhase.Failed, 0, 0, error);
        return new GenerateResult
        {
            Success = false,
            ExitCode = exitCode,
            Phase = phase,
            OutputPath = state.Notebook?.Path ?? (state.Request.RenderOnly ? state.Request.RenderOutputDir : null),
            NotebookName = state.Notebook?.Name,
            PagesRendered = 0,
            Error = error,
            FailedPage = page?.Title,
            Elapsed = state.Clock.Elapsed,
            LogPath = _options.LogPath,
            Warnings = state.Warnings,
            SidecarPath = state.SidecarPath,
        };
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

    private static Dictionary<PageKey, string> SectionNames(NotebookPlan plan)
    {
        Dictionary<PageKey, string> result = new();
        foreach (SectionSpec section in plan.Sections)
        {
            foreach (PageSpec page in section.Pages)
            {
                result[page.Key] = section.Name;
            }
        }

        return result;
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
