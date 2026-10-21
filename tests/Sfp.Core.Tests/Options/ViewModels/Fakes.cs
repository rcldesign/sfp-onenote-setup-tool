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
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// The tests' stand-in for WPF's dispatcher: the <see cref="IUiThread"/> the view models get, and the synchronization
/// context their awaits resume on. A view-model test ([UiFact] or [UiTheory]) runs from start to finish on the
/// dispatcher's own thread, which runs one callback at a time, in the order they were queued: the test's code, the view
/// models' code and await continuations, and every action posted through <see cref="IUiThread"/>. Post and Send only
/// queue; nothing runs inline on the thread that posts. Background work (Task.Run bodies, the fakes' orchestrators,
/// validator and renderer) runs on the thread pool and comes back through the queue, so a view model is never touched
/// from two threads at once, and a result that finished work posts waits until the test yields (any await,
/// <see cref="Harness.Settle(GenerateViewModel)"/>, <see cref="Drain"/>), as it would on a busy dispatcher. A posted
/// action that throws fails the test, as it would crash the app.
/// </summary>
public sealed class TestDispatcher : SynchronizationContext, IUiThread
{
    /// <summary>How long <see cref="Drain"/> and <see cref="Harness.Settle(GenerateViewModel)"/> wait before they fail the test.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [ThreadStatic]
    private static TestDispatcher? t_current;

    private readonly object _gate = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;
    private int _posted;
    private bool _closed;
    private Exception? _error;

    private TestDispatcher(Thread thread)
    {
        _thread = thread;
    }

    /// <summary>The dispatcher of the test running on this thread.</summary>
    public static TestDispatcher OfThisThread => t_current ?? throw new InvalidOperationException(
        "A view-model test runs on the test dispatcher: mark it [UiFact] or [UiTheory] instead of [Fact] or [Theory].");

    /// <summary>The number of actions posted through <see cref="IUiThread.Post"/>.</summary>
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

    /// <summary>The number of callbacks queued and not run yet.</summary>
    public int Queued
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            _posted++;
        }

        Enqueue(_ => action(), null);
    }

    public override void Post(SendOrPostCallback d, object? state) => Enqueue(d, state);

    /// <summary>Queues like <see cref="Post(SendOrPostCallback, object?)"/>: nothing runs inline. The view models never send.</summary>
    public override void Send(SendOrPostCallback d, object? state) => Enqueue(d, state);

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Lets the dispatcher run everything queued, and whatever that queues in turn, until nothing is queued; then rethrows
    /// what a posted action threw. Awaits, never blocks: each turn yields to the queue.
    /// </summary>
    public async Task Drain()
    {
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        while (Queued > 0)
        {
            if (clock.Elapsed > Timeout)
            {
                throw new TimeoutException($"The test dispatcher still had {Queued} callbacks queued after {Timeout.TotalSeconds} s.");
            }

            await Task.Yield();
            if (Thread.CurrentThread != _thread)
            {
                throw new InvalidOperationException("Drain resumed off the test dispatcher's thread; the test is not a [UiFact] or [UiTheory].");
            }
        }

        if (TakeError() is { } error)
        {
            throw new InvalidOperationException("An action posted to the UI thread threw.", error);
        }
    }

    /// <summary>
    /// Blocks the calling background thread until the dispatcher has run everything queued so far. A fake run that
    /// reported progress and returns at once calls it: on a dispatcher, a run that finishes before the view model awaits
    /// it has its result applied first, and the view model then ignores the reports still queued. A real run reports long
    /// before it returns.
    /// </summary>
    public void WaitUntilQueuedHaveRun()
    {
        if (Thread.CurrentThread == _thread)
        {
            throw new InvalidOperationException("The dispatcher's own thread cannot wait for its queue; await Drain instead.");
        }

        TaskCompletionSource ran = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Enqueue(_ => ran.TrySetResult(), null) && !ran.Task.Wait(Timeout))
        {
            throw new TimeoutException($"A fake run waited {Timeout.TotalSeconds} s for its progress reports to reach the view model; the test never yielded to the dispatcher.");
        }
    }

    /// <summary>
    /// Runs one test case on a new thread whose synchronization context is a new dispatcher, pumping the queue until the
    /// test case has finished. The message bus reports a passed test as failed when a posted action threw meanwhile.
    /// </summary>
    internal static Task<RunSummary> Run(IMessageBus messageBus, Func<IMessageBus, Task<RunSummary>> testCase)
    {
        TaskCompletionSource<RunSummary> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            TestDispatcher dispatcher = new(Thread.CurrentThread);
            t_current = dispatcher;
            SetSynchronizationContext(dispatcher);
            try
            {
                FailingBus bus = new(messageBus, dispatcher);
                Task<RunSummary> run = testCase(bus);
                dispatcher.PumpUntil(run);
                RunSummary summary = run.GetAwaiter().GetResult();
                summary.Failed += bus.Converted;
                done.SetResult(summary);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
            finally
            {
                dispatcher.Close();
                SetSynchronizationContext(null);
                t_current = null;
            }
        })
        {
            IsBackground = true,
            Name = "Test dispatcher",
        };
        thread.Start();
        return done.Task;
    }

    private bool Enqueue(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            // Background work that finishes after its test was reported is dropped, as a shut-down dispatcher drops it.
            if (_closed)
            {
                return false;
            }

            _queue.Enqueue((callback, state));
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    private void PumpUntil(Task testCase)
    {
        testCase.ContinueWith(_ => Wake(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        while (true)
        {
            (SendOrPostCallback Callback, object? State) next;
            lock (_gate)
            {
                while (_queue.Count == 0 && !testCase.IsCompleted)
                {
                    Monitor.Wait(_gate);
                }

                if (testCase.IsCompleted)
                {
                    return;
                }

                next = _queue.Dequeue();
            }

            try
            {
                next.Callback(next.State);
            }
            catch (Exception ex)
            {
                // An await continuation never throws here (its task keeps the exception); a posted action can.
                lock (_gate)
                {
                    _error ??= ex;
                }
            }
        }
    }

    private void Wake()
    {
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }
    }

    private void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _queue.Clear();
        }
    }

    private Exception? TakeError()
    {
        lock (_gate)
        {
            Exception? error = _error;
            _error = null;
            return error;
        }
    }

    /// <summary>Passes every message on, reporting a passed test as failed when a posted action threw during it.</summary>
    private sealed class FailingBus : IMessageBus
    {
        private readonly IMessageBus _inner;
        private readonly TestDispatcher _dispatcher;

        public FailingBus(IMessageBus inner, TestDispatcher dispatcher)
        {
            _inner = inner;
            _dispatcher = dispatcher;
        }

        public int Converted { get; private set; }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            if (message is ITestResultMessage result && _dispatcher.TakeError() is { } error && result is ITestPassed)
            {
                Converted++;
                return _inner.QueueMessage(new TestFailed(result.Test, result.ExecutionTime, result.Output, new InvalidOperationException("An action posted to the UI thread threw.", error)));
            }

            return _inner.QueueMessage(message);
        }

        public void Dispose()
        {
            // The runner owns the inner bus and disposes it.
        }
    }
}

/// <summary>A fact that runs on its own <see cref="TestDispatcher"/> thread, as every view-model test does.</summary>
[XunitTestCaseDiscoverer("Sfp.Core.Tests.Options.ViewModels.UiFactDiscoverer", "Sfp.Core.Tests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UiFactAttribute : FactAttribute
{
}

/// <summary>A theory whose rows each run on their own <see cref="TestDispatcher"/> thread.</summary>
[XunitTestCaseDiscoverer("Sfp.Core.Tests.Options.ViewModels.UiTheoryDiscoverer", "Sfp.Core.Tests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UiTheoryAttribute : TheoryAttribute
{
}

public sealed class UiFactDiscoverer : FactDiscoverer
{
    public UiFactDiscoverer(IMessageSink diagnosticMessageSink)
        : base(diagnosticMessageSink)
    {
    }

    protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute) =>
        new UiTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
}

public sealed class UiTheoryDiscoverer : TheoryDiscoverer
{
    public UiTheoryDiscoverer(IMessageSink diagnosticMessageSink)
        : base(diagnosticMessageSink)
    {
    }

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow) =>
        new IXunitTestCase[] { new UiTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow) };

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute) =>
        new IXunitTestCase[] { new UiTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod) };
}

public sealed class UiTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer only.", error: true)]
    public UiTestCase()
    {
    }

    public UiTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        TestDispatcher.Run(messageBus, bus => base.RunAsync(diagnosticMessageSink, bus, constructorArguments, aggregator, cancellationTokenSource));
}

public sealed class UiTheoryTestCase : XunitTheoryTestCase
{
    [Obsolete("Called by the de-serializer only.", error: true)]
    public UiTheoryTestCase()
    {
    }

    public UiTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
    }

    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        TestDispatcher.Run(messageBus, bus => base.RunAsync(diagnosticMessageSink, bus, constructorArguments, aggregator, cancellationTokenSource));
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
    public ValidationResult Result { get; set; } = ValidationResult.Ok;

    public int Calls { get; private set; }

    public string? LastPath { get; private set; }

    public ValidationResult Validate(Manifest m, string pdfPath)
    {
        Calls++;
        LastPath = pdfPath;
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

    /// <summary>The test's dispatcher: a run that reports and returns at once waits for its reports to be applied first.</summary>
    public TestDispatcher? Dispatcher { get; set; }

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

        if (!WaitForCancel && ProgressToReport.Count > 0)
        {
            Dispatcher?.WaitUntilQueuedHaveRun();
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

    /// <summary>The test's dispatcher: a run that reports and returns at once waits for its reports to be applied first.</summary>
    public TestDispatcher? Dispatcher { get; set; }

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

        if (!WaitForCancel && ProgressToReport.Count > 0)
        {
            Dispatcher?.WaitUntilQueuedHaveRun();
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

    /// <summary>
    /// When set, every listing waits here once it is counted (released by the test), as the listing of a slow or
    /// unreachable folder does; a listing never released fails after <see cref="TestDispatcher.Timeout"/>.
    /// </summary>
    public ManualResetEventSlim? Gate { get; set; }

    /// <summary>The listings started so far; safe to read while one waits at <see cref="Gate"/> on the thread pool.</summary>
    public int Listings
    {
        get
        {
            lock (Queries)
            {
                return Queries.Count;
            }
        }
    }

    public IReadOnlyList<KnownNotebook> List(IEnumerable<string> folders)
    {
        lock (Queries)
        {
            Queries.Add(folders.ToList());
        }

        if (Gate is { } gate && !gate.Wait(TestDispatcher.Timeout))
        {
            throw new TimeoutException("The test never released the folder listing.");
        }

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
        Generator.Dispatcher = Ui;
        Refresher.Dispatcher = Ui;
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

    /// <summary>The dispatcher of the running test; the harness can only be built inside a [UiFact] or [UiTheory].</summary>
    public TestDispatcher Ui { get; } = TestDispatcher.OfThisThread;

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

    /// <summary>
    /// Waits, without blocking the dispatcher, until the form's PDF check and preview render have finished and every
    /// result they posted has been applied; fails the test after <see cref="TestDispatcher.Timeout"/>.
    /// </summary>
    public async Task Settle(GenerateViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        do
        {
            await Settle(vm.PendingPdfCheck, vm.Previews.Pending);
        }
        while (!vm.PendingPdfCheck.IsCompleted || !vm.Previews.Pending.IsCompleted);
    }

    /// <summary>Waits, without blocking the dispatcher, until <paramref name="work"/> has finished and the dispatcher has run everything queued.</summary>
    public async Task Settle(params Task[] work)
    {
        try
        {
            await Task.WhenAll(work).WaitAsync(TestDispatcher.Timeout);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"The view model's background work did not finish within {TestDispatcher.Timeout.TotalSeconds} s.", ex);
        }

        await Ui.Drain();
    }

    /// <summary>
    /// Waits, without blocking the dispatcher, until <paramref name="condition"/> holds: each turn runs everything queued
    /// and then yields a millisecond, so background work, the view models' await continuations and their timers move on
    /// while the test waits (a SpinWait on the dispatcher's thread would starve them). Fails the test after
    /// <see cref="TestDispatcher.Timeout"/>, naming the condition and <paramref name="because"/>.
    /// </summary>
    public async Task WaitUntilAsync(Func<bool> condition, string? because = null, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string conditionText = "")
    {
        ArgumentNullException.ThrowIfNull(condition);
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            await Ui.Drain();
            if (condition())
            {
                return;
            }

            if (clock.Elapsed > TestDispatcher.Timeout)
            {
                throw new TimeoutException($"Waited {TestDispatcher.Timeout.TotalSeconds} s for {conditionText}" + (because is null ? "." : ": " + because + "."));
            }

            await Task.Delay(1);
        }
    }
}
