using System.IO;
using System.Windows.Threading;
using Serilog;
using Sfp.App.ViewModels;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Refresh;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.Render;

namespace Sfp.App.Composition;

/// <summary>
/// The composition root: the one place that constructs concrete services and hands them to the view models by
/// constructor. No DI container. The orchestrators, the plan builder, the page renderer, the notebook registry and the
/// scheduled task service are built by the factory methods below, which the integrator fills in as those modules land
/// (each is marked "TODO integrator"); until then a NotAvailable stand-in reports the missing card as an error line.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>Exit code when the window cannot be built (the PRD's validation error code).</summary>
    public const int StartupFailureExitCode = 2;

    private AppServices(MainViewModel main, AppEnvironment environment, Manifest manifest)
    {
        Main = main;
        Environment = environment;
        Manifest = manifest;
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

        PreviewStripViewModel previews = new(CreatePlanBuilder(), CreatePageRenderer(manifest, templatesDir), manifest, dates, ui);
        GenerateRunViewModel run = new(logs, logger => CreateGenerateOrchestrator(logger, manifest, templatesDir), ui, shell, dialogs);
        GenerateViewModel generate = new(manifest, dates, validator, previews, run, dialogs, ui, env);
        RefreshViewModel refresh = new(
            CreateNotebookRegistry(),
            CreateScheduledTaskService(),
            logs,
            logger => CreateRefreshOrchestrator(logger, manifest, templatesDir),
            ui,
            env);
        MainViewModel main = new(generate, refresh, settings);
        return new AppServices(main, env, manifest);
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

    public void Dispose() => Main.Dispose();

    private static IPlanBuilder CreatePlanBuilder()
    {
        // TODO integrator: return the M1.6 plan builder, for example new PlanBuilder(new DateEngine()).
        return new NotAvailablePlanBuilder();
    }

    private static IPageRenderer CreatePageRenderer(Manifest manifest, string templatesDir)
    {
        // TODO integrator: return the M2.9 page renderer built from the manifest, a Rasterizer and FontService.Load(manifest.Font, templatesDir).
        _ = manifest;
        _ = templatesDir;
        return new NotAvailablePageRenderer();
    }

    private static IGenerateOrchestrator CreateGenerateOrchestrator(ILogger logger, Manifest manifest, string templatesDir)
    {
        // TODO integrator: return the M3.9 GenerateOrchestrator with this run's logger (the one the generate verb builds).
        _ = logger;
        _ = manifest;
        _ = templatesDir;
        return new NotAvailableGenerateOrchestrator();
    }

    private static IRefreshOrchestrator CreateRefreshOrchestrator(ILogger logger, Manifest manifest, string templatesDir)
    {
        // TODO integrator: return the M4.7 RefreshOrchestrator with this run's logger (the one the refresh verb builds).
        _ = logger;
        _ = manifest;
        _ = templatesDir;
        return new NotAvailableRefreshOrchestrator();
    }

    private static INotebookRegistry CreateNotebookRegistry()
    {
        // TODO integrator: return the M4.3 sidecar-scanning registry (SidecarStore based).
        return new NotAvailableNotebookRegistry();
    }

    private static IScheduledTaskService CreateScheduledTaskService()
    {
        // TODO integrator: return the M5.4 Task Scheduler service.
        return new NotAvailableScheduledTaskService();
    }
}
