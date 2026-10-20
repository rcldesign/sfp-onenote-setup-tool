using System.IO;
using System.Windows.Threading;
using Serilog;
using Sfp.App.ViewModels;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Refresh;
using Sfp.App.ViewModels.Services;
using Sfp.Cli.Logging;
using Sfp.Cli.Verbs;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Scheduler;
using Sfp.Render;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;

namespace Sfp.App.Composition;

/// <summary>
/// The composition root: the one place that constructs concrete services and hands them to the view models by
/// constructor. No DI container. The orchestrators are the same production compositions the CLI verbs use
/// (GenerateCommand.CreateProductionOrchestrator and RefreshCommand.CreateProductionOrchestrator), created per run
/// with that run's logger and disposed after the run.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>Exit code when the window cannot be built (the PRD's validation error code).</summary>
    public const int StartupFailureExitCode = 2;

    private readonly IReadOnlyList<IDisposable> _owned;

    private AppServices(MainViewModel main, AppEnvironment environment, Manifest manifest, IReadOnlyList<IDisposable> owned)
    {
        Main = main;
        Environment = environment;
        Manifest = manifest;
        _owned = owned;
    }

    public MainViewModel Main { get; }

    public AppEnvironment Environment { get; }

    public Manifest Manifest { get; }

    /// <summary>Builds every service from the machine: templates beside the executable, settings under %APPDATA%, today's date.</summary>
    public static AppServices Create(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        string baseDir = AppContext.BaseDirectory;
        string templatesDir = Path.Combine(baseDir, "templates");
        AppEnvironment env = new(
            DateOnly.FromDateTime(DateTime.Today),
            templatesDir,
            GenerateFormDefaults.OutputFolder(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)),
            OneDriveRoots(),
            RefreshExecutable(baseDir));

        Manifest manifest = new ManifestLoader().LoadShipped(templatesDir);
        IDateEngine dates = new DateEngine();
        IManifestValidator pdfValidator = new ManifestValidator();
        IPathProbe paths = new PathProbe();
        GenerateFormValidator validator = new(pdfValidator, paths, env.OneDriveRoots);
        IUiThread ui = new DispatcherUiThread(dispatcher);
        IDialogService dialogs = new WpfDialogService();
        IShellService shell = new WpfShellService();
        IRunLogFactory logs = new RunLogFactory();
        ISettingsStore settings = new SettingsStore();

        List<IDisposable> owned = new();
        IPageRenderer previewRenderer = CreatePageRenderer(manifest, templatesDir, dates, owned);
        PreviewStripViewModel previews = new(new PlanBuilder(dates), previewRenderer, manifest, dates, ui);
        GenerateRunViewModel run = new(logs, logger => CreateGenerateOrchestrator(logger), ui, shell, dialogs);
        GenerateViewModel generate = new(manifest, dates, validator, previews, run, dialogs, ui, env);
        RefreshViewModel refresh = new(
            new NotebookRegistry(new SidecarStore()),
            new ScheduledTaskService(new ProcessRunner(), Serilog.Core.Logger.None),
            logs,
            logger => CreateRefreshOrchestrator(logger, manifest, templatesDir),
            ui,
            env);
        MainViewModel main = new(generate, refresh, settings);
        return new AppServices(main, env, manifest, owned);
    }

    /// <summary>The OneDrive sync folders from the environment variables OneDrive sets, for the output folder warning.</summary>
    public static IReadOnlyList<string> OneDriveRoots() => new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
        .Select(System.Environment.GetEnvironmentVariable)
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>The sfp2onenote.exe beside the app that the daily scheduled task runs (M6.6 packaging places it there).</summary>
    public static string RefreshExecutable(string baseDir) => Path.Combine(baseDir, "sfp2onenote.exe");

    public void Dispose()
    {
        Main.Dispose();
        foreach (IDisposable d in _owned)
        {
            d.Dispose();
        }
    }

    /// <summary>The preview renderer: the fallback font warning is shown by the form, so it is only logged here.</summary>
    private static IPageRenderer CreatePageRenderer(Manifest manifest, string templatesDir, IDateEngine dates, List<IDisposable> owned)
    {
        FontService fonts = FontService.Load(manifest.Font, templatesDir);
        owned.Add(fonts);
        Rasterizer rasterizer = new(ColorParser.Parse(manifest.Colors.Background));
        owned.Add(rasterizer);
        return new PageRenderer(manifest, rasterizer, fonts, dates);
    }

    /// <summary>Rejected page XML is saved beside the run logs; the run's own file name is not known to the factory.</summary>
    private static string RejectedXmlLogPath() => Path.Combine(RunLog.DefaultDirectory, "rejected-page.log");

    private static IGenerateOrchestrator CreateGenerateOrchestrator(ILogger logger) =>
        new DeferredGenerateOrchestrator(request => GenerateCommand.CreateProductionOrchestrator(request, logger, RejectedXmlLogPath()));

    private static IRefreshOrchestrator CreateRefreshOrchestrator(ILogger logger, Manifest manifest, string templatesDir) =>
        new DeferredRefreshOrchestrator(request => RefreshCommand.CreateProductionOrchestrator(request, manifest, templatesDir, logger, RejectedXmlLogPath()));

    /// <summary>Builds the production orchestrator when the request is known, runs it once and disposes it (the COM session and fonts).</summary>
    private sealed class DeferredGenerateOrchestrator : IGenerateOrchestrator
    {
        private readonly Func<GenerateRequest, IGenerateOrchestrator> _create;

        public DeferredGenerateOrchestrator(Func<GenerateRequest, IGenerateOrchestrator> create)
        {
            _create = create;
        }

        public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken)
        {
            IGenerateOrchestrator inner = _create(request);
            try
            {
                return inner.Run(request, progress, cancellationToken);
            }
            finally
            {
                (inner as IDisposable)?.Dispose();
            }
        }
    }

    /// <summary>Same deferral for Refresh; an unavailable OneNote surfaces as the PRD's exit-3 result instead of an exception.</summary>
    private sealed class DeferredRefreshOrchestrator : IRefreshOrchestrator
    {
        private readonly Func<RefreshRequest, IRefreshOrchestrator> _create;

        public DeferredRefreshOrchestrator(Func<RefreshRequest, IRefreshOrchestrator> create)
        {
            _create = create;
        }

        public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            IRefreshOrchestrator inner;
            try
            {
                inner = _create(request);
            }
            catch (OneNoteUnavailableException ex)
            {
                return new RefreshResult { Success = false, ExitCode = Sfp.Cli.ExitCodes.OneNoteUnavailable, Error = ex.Message };
            }

            try
            {
                return inner.Run(request, progress, cancellationToken);
            }
            finally
            {
                (inner as IDisposable)?.Dispose();
            }
        }
    }
}
