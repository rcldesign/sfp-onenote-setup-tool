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
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Runs posted actions inline, so a test sees every UI update as soon as the background work finishes. Inline means on
/// the background thread, the moment the work ends: a test that looks at a state the result will replace holds the
/// posts first (<see cref="Hold"/>), as a busy dispatcher would, and releases them after looking.
/// </summary>
public sealed class ImmediateUiThread : IUiThread
{
    private readonly object _gate = new();
    private List<Action>? _held;
    private int _posted;

    public int Posted
    {
        get
        {
            lock (_gate)
            {
                return _posted;
            }
        }
    }

    /// <summary>From now on posted actions wait until <see cref="Release"/>.</summary>
    public void Hold()
    {
        lock (_gate)
        {
            _held ??= new List<Action>();
        }
    }

    /// <summary>Runs the held actions on the calling thread, in the order they were posted, and posts inline again.</summary>
    public void Release()
    {
        List<Action> held;
        lock (_gate)
        {
            held = _held ?? new List<Action>();
            _held = null;
        }

        foreach (Action action in held)
        {
            action();
        }
    }

    public void Post(Action action)
    {
        lock (_gate)
        {
            _posted++;
            if (_held is not null)
            {
                _held.Add(action);
                return;
            }
        }

        action();
    }
}

public sealed class FakeDialogService : IDialogService
{
    public string? NextFile { get; set; }

    public string? NextFolder { get; set; }

    public string? NextSaveFile { get; set; }

    public List<string> Titles { get; } = new();

    public string? LastInitialPath { get; private set; }

    public string? PickFile(string title, string filter, string? initialPath)
    {
        Titles.Add(title);
        LastInitialPath = initialPath;
        return NextFile;
    }

    public string? PickFolder(string title, string? initialFolder)
    {
        Titles.Add(title);
        LastInitialPath = initialFolder;
        return NextFolder;
    }

    public string? PickSaveFile(string title, string filter, string suggestedFileName)
    {
        Titles.Add(title);
        LastInitialPath = suggestedFileName;
        return NextSaveFile;
    }

    /// <summary>The answer every yes/no question gets.</summary>
    public bool ConfirmAnswer { get; set; } = true;

    /// <summary>The questions asked, in order.</summary>
    public List<string> Questions { get; } = new();

    public bool Confirm(string title, string question)
    {
        Titles.Add(title);
        Questions.Add(question);
        return ConfirmAnswer;
    }
}

public sealed class FakeShellService : IShellService
{
    public List<string> Clipboard { get; } = new();

    public List<string> Opened { get; } = new();

    public List<(string Source, string Destination)> Copies { get; } = new();

    public Exception? Throw { get; set; }

    public void CopyToClipboard(string text)
    {
        ThrowIfScripted();
        Clipboard.Add(text);
    }

    public void OpenInOneNote(string notebookPath)
    {
        ThrowIfScripted();
        Opened.Add(notebookPath);
    }

    public void CopyFile(string sourcePath, string destinationPath)
    {
        ThrowIfScripted();
        Copies.Add((sourcePath, destinationPath));
    }

    private void ThrowIfScripted()
    {
        if (Throw is not null)
        {
            throw Throw;
        }
    }
}

/// <summary>A run log with a fixed path and its own logger instance, so a test can tell which log an orchestrator was given.</summary>
public sealed class FakeRunLog : IRunLog
{
    private readonly Serilog.Core.Logger _logger = new Serilog.LoggerConfiguration().CreateLogger();

    public FakeRunLog(string path)
    {
        Path = path;
    }

    public Serilog.ILogger Logger => _logger;

    public string Path { get; }

    public bool Disposed { get; private set; }

    public void Dispose()
    {
        Disposed = true;
        _logger.Dispose();
    }
}

/// <summary>Starts <see cref="FakeRunLog"/>s with paths like the real log folder's, keeping every one started.</summary>
public sealed class FakeRunLogFactory : IRunLogFactory
{
    public List<FakeRunLog> Started { get; } = new();

    public IRunLog Start(string verb)
    {
        FakeRunLog log = new(@"C:\Users\me\AppData\Local\sfp2onenote\logs\20261020-0930" + Started.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + verb + ".log");
        Started.Add(log);
        return log;
    }
}

public sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings Stored { get; set; } = new();

    public int Saves { get; private set; }

    public Exception? ThrowOnSave { get; set; }

    public Exception? ThrowOnLoad { get; set; }

    public string Path => "/fake/settings.json";

    public AppSettings Load() => ThrowOnLoad is null ? Stored : throw ThrowOnLoad;

    public void Save(AppSettings settings)
    {
        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }

        Saves++;
        Stored = settings;
    }
}

public sealed class ScriptedManifestValidator : IManifestValidator
{
    private TaskCompletionSource? _gate;

    public ValidationResult Result { get; set; } = ValidationResult.Ok;

    public int Calls { get; private set; }

    public string? LastPath { get; private set; }

    /// <summary>
    /// From now on every check waits in the validator until <see cref="Release"/>, so a test can look at the form while
    /// the check is still running. A check that is not held can finish, and post its result inline through
    /// <see cref="ImmediateUiThread"/>, before the test looks.
    /// </summary>
    public void Hold() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets the held check, and every later one, finish.</summary>
    public void Release() => _gate?.TrySetResult();

    public ValidationResult Validate(Manifest m, string pdfPath)
    {
        Calls++;
        LastPath = pdfPath;
        if (_gate is { } gate && !gate.Task.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The test held the PDF check and never released it.");
        }

        return Result;
    }
}

/// <summary>A tiny plan: a cover, one calendar page, one out-of-quarter day page and one in-quarter day page.</summary>
public sealed class FakePlanBuilder : IPlanBuilder
{
    public int Calls { get; private set; }

    public Exception? Throw { get; set; }

    public GenerateOptions? LastOptions { get; private set; }

    public NotebookPlan Build(Manifest m, Quarter q, GenerateOptions o)
    {
        Calls++;
        LastOptions = o;
        if (Throw is not null)
        {
            throw Throw;
        }

        Dictionary<string, string> tokens = new(StringComparer.Ordinal);
        DateOnly before = q.Start.AddDays(-1);
        PageSpec cover = new(PageKey.Cover, PageType.Cover, "Cover", 1, 1, q.Start, tokens);
        PageSpec calendar = new(PageKey.Calendar(q.Start.Year, q.Start.Month), PageType.Calendar, PlannerText.MonthYear(q.Start.Year, q.Start.Month), 1, 13, q.Start, tokens);
        PageSpec outside = new(PageKey.Day(before), PageType.Day, PlannerText.DayTitle(before), 2, 17, before, tokens);
        PageSpec first = new(PageKey.Day(q.Start), PageType.Day, PlannerText.DayTitle(q.Start), 2, 17, q.Start, tokens);
        return new NotebookPlan(
            o.NotebookName ?? "SFP",
            q,
            new[]
            {
                new SectionSpec("Quarter", null, new[] { cover }),
                new SectionSpec("Calendar", null, new[] { calendar }),
                new SectionSpec("Week 01", null, new[] { outside, first }),
            },
            Array.Empty<LinkSpec>());
    }
}

public sealed class FakePageRenderer : IPageRenderer
{
    public static readonly byte[] PngStub = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public List<string> Rendered { get; } = new();

    public Exception? Throw { get; set; }

    /// <summary>When set, every render waits here first (released by the test).</summary>
    public ManualResetEventSlim? Gate { get; set; }

    public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o)
    {
        Gate?.Wait();
        if (Throw is not null)
        {
            throw Throw;
        }

        lock (Rendered)
        {
            Rendered.Add(page.Title);
        }

        return new RenderedPage(page.Key, PngStub, 1112 * o.Dpi / 72, 770 * o.Dpi / 72, Array.Empty<AnchorPlacement>());
    }
}

public sealed class FakeGenerateOrchestrator : IGenerateOrchestrator
{
    public GenerateResult Result { get; set; } = new() { Success = true, ExitCode = 0, Phase = GeneratePhase.Done, PagesWritten = 144, PagesRendered = 144, OutputPath = @"C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027", NotebookName = "SFP Q1 2027", Elapsed = TimeSpan.FromSeconds(102) };

    public List<GenerateProgress> ProgressToReport { get; } = new();

    public GenerateRequest? LastRequest { get; private set; }

    public Exception? Throw { get; set; }

    /// <summary>When true the run waits for cancellation and then returns <see cref="CancelledResult"/>.</summary>
    public bool WaitForCancel { get; set; }

    /// <summary>What a cancelled run returns.</summary>
    public GenerateResult CancelledResult { get; set; } = new() { Success = false, Cancelled = true, ExitCode = 0, Phase = GeneratePhase.Cancelled, PagesRendered = 10, PagesWritten = 3 };

    /// <summary>Reported after the cancel was seen (the page in flight finishing, then the Cancelled phase).</summary>
    public List<GenerateProgress> ProgressAfterCancel { get; } = new();

    /// <summary>When set, a cancelled run waits here before returning, as the real one does while it deletes the partial notebook.</summary>
    public ManualResetEventSlim? AfterCancelGate { get; set; }

    /// <summary>True once the run saw the cancellation.</summary>
    public bool SawCancel { get; private set; }

    /// <summary>Called inside Run, on the worker thread, before anything else.</summary>
    public Action? OnRun { get; set; }

    public int Runs { get; private set; }

    public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken)
    {
        Runs++;
        LastRequest = request;
        OnRun?.Invoke();
        if (Throw is not null)
        {
            throw Throw;
        }

        foreach (GenerateProgress p in ProgressToReport)
        {
            progress?.Report(p);
        }

        if (WaitForCancel)
        {
            cancellationToken.WaitHandle.WaitOne();
            SawCancel = true;
            foreach (GenerateProgress p in ProgressAfterCancel)
            {
                progress?.Report(p);
            }

            AfterCancelGate?.Wait();
            return CancelledResult;
        }

        return Result;
    }
}

public sealed class FakeRefreshOrchestrator : IRefreshOrchestrator
{
    public RefreshResult Result { get; set; } = new() { Success = true, ExitCode = 0, Elapsed = TimeSpan.FromSeconds(12) };

    public RefreshRequest? LastRequest { get; private set; }

    public List<string> ProgressToReport { get; } = new();

    public Exception? Throw { get; set; }

    public int Runs { get; private set; }

    /// <summary>When true the run reports <see cref="ProgressToReport"/>, waits for cancellation and then returns <see cref="CancelledResult"/>.</summary>
    public bool WaitForCancel { get; set; }

    /// <summary>What a cancelled run returns: like the production orchestrator's, exit 5, "Cancelled." and no summary.</summary>
    public RefreshResult CancelledResult { get; set; } = new() { Success = false, ExitCode = 5, Error = "Cancelled.", Elapsed = TimeSpan.FromSeconds(4) };

    /// <summary>Reported after the cancel was seen: the page that was already being written when Cancel was pressed.</summary>
    public List<string> ProgressAfterCancel { get; } = new();

    /// <summary>When set, a cancelled run waits here before returning, as the real one does while it finishes the page in flight.</summary>
    public ManualResetEventSlim? AfterCancelGate { get; set; }

    /// <summary>True once the run has reported <see cref="ProgressToReport"/> and waits for the cancel.</summary>
    public bool WaitingForCancel { get; private set; }

    /// <summary>True once the run saw the cancellation and reported <see cref="ProgressAfterCancel"/>.</summary>
    public bool SawCancel { get; private set; }

    public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Runs++;
        LastRequest = request;
        if (Throw is not null)
        {
            throw Throw;
        }

        foreach (string p in ProgressToReport)
        {
            progress?.Report(p);
        }

        if (WaitForCancel)
        {
            WaitingForCancel = true;
            if (!cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(15)))
            {
                // A token that never cancels (the run was not given the one Cancel cancels) fails the test instead of hanging it.
                throw new TimeoutException("The refresh was never cancelled.");
            }

            foreach (string p in ProgressAfterCancel)
            {
                progress?.Report(p);
            }

            SawCancel = true;
            AfterCancelGate?.Wait();
            return CancelledResult;
        }

        return Result;
    }
}

public sealed class FakeNotebookRegistry : INotebookRegistry
{
    public List<KnownNotebook> Notebooks { get; } = new();

    public List<IReadOnlyList<string>> Queries { get; } = new();

    public Exception? Throw { get; set; }

    public IReadOnlyList<KnownNotebook> List(IEnumerable<string> folders)
    {
        Queries.Add(folders.ToList());
        if (Throw is not null)
        {
            throw Throw;
        }

        return Notebooks.ToList();
    }
}

public sealed class FakeScheduledTaskService : IScheduledTaskService
{
    public HashSet<string> Registered { get; } = new(StringComparer.Ordinal);

    public List<(string Name, string Exe, int Horizon)> Registrations { get; } = new();

    public List<string> Removals { get; } = new();

    public Exception? Throw { get; set; }

    public bool IsRegistered(string notebookName) => Registered.Contains(notebookName);

    public List<string?> Folders { get; } = new();

    public void Register(string notebookName, string executablePath, int horizonDays) =>
        Register(notebookName, executablePath, horizonDays, null);

    public void Register(string notebookName, string executablePath, int horizonDays, string? notebooksFolder)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        Registered.Add(notebookName);
        Registrations.Add((notebookName, executablePath, horizonDays));
        Folders.Add(notebooksFolder);
    }

    public void Remove(string notebookName)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        Registered.Remove(notebookName);
        Removals.Add(notebookName);
    }
}

/// <summary>Builds view models wired to fakes, with every fake reachable for assertions.</summary>
public sealed class Harness
{
    public const string Pdf = @"C:\planner\SFP_No_Dates.pdf";
    public const string DefaultFolder = @"C:\Users\me\Documents\OneNote Notebooks";
    public const string Exe = @"C:\Program Files\sfp2onenote\sfp2onenote.exe";

    public Harness()
    {
        Paths.Files.Add(Pdf);
        Paths.Directories.Add(DefaultFolder);
        Env = new AppEnvironment(new DateOnly(2026, 10, 20), TestPaths.TemplatesDir, DefaultFolder, new[] { @"C:\Users\me\OneDrive" }, Exe)
        {
            Clock = () => Now,
        };
        Validator = new GenerateFormValidator(PdfValidator, Paths, Env.OneDriveRoots);
    }

    /// <summary>The date <see cref="Env"/>'s clock reads; a test moves it to simulate the window staying open past midnight.</summary>
    public DateOnly Now { get; set; } = new(2026, 10, 20);

    public Manifest Manifest { get; } = TestManifest.Shipped;

    public DateEngine Dates { get; } = new();

    /// <summary>The machine facts the view models get; a test may replace it (with <c>with</c>) before creating them.</summary>
    public AppEnvironment Env { get; set; }

    public FakePathProbe Paths { get; } = new();

    public ScriptedManifestValidator PdfValidator { get; } = new();

    public GenerateFormValidator Validator { get; }

    public ImmediateUiThread Ui { get; } = new();

    public FakeDialogService Dialogs { get; } = new();

    public FakeShellService Shell { get; } = new();

    public SilentRunLogFactory Logs { get; } = new();

    public FakePlanBuilder Plans { get; } = new();

    public FakePageRenderer Renderer { get; } = new();

    public FakeGenerateOrchestrator Generator { get; } = new();

    public FakeRefreshOrchestrator Refresher { get; } = new();

    public FakeNotebookRegistry Registry { get; } = new();

    public FakeScheduledTaskService Tasks { get; } = new();

    public FakeSettingsStore Settings { get; } = new();

    public PreviewStripViewModel NewPreviews(TimeSpan? debounce = null) =>
        new(Plans, Renderer, Manifest, Dates, Ui, debounce ?? TimeSpan.Zero);

    public GenerateRunViewModel NewRun() => new(Logs, (_, _) => Generator, Ui, Shell, Dialogs);

    public GenerateViewModel NewGenerate(TimeSpan? debounce = null) =>
        new(Manifest, Dates, Validator, NewPreviews(debounce), NewRun(), Dialogs, Ui, Env);

    public RefreshViewModel NewRefresh() => new(Registry, Tasks, Logs, (_, _) => Refresher, Dialogs, Ui, Env);

    public MainViewModel NewMain() => new(NewGenerate(), NewRefresh(), Settings);
}
