using Sfp.Core.Plan;
using Sfp.Render;

namespace Sfp.OneNote.Generate;

/// <summary>A page could not be rendered. <see cref="Page"/> names it; the inner exception is the renderer's.</summary>
public sealed class RenderFailedException : Exception
{
    public RenderFailedException()
    {
    }

    public RenderFailedException(string message)
        : base(message)
    {
    }

    public RenderFailedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public RenderFailedException(PageSpec page, Exception innerException)
        : base($"Rendering page '{page?.Title}' failed: {innerException?.Message}", innerException)
    {
        Page = page;
    }

    public PageSpec? Page { get; }
}

/// <summary>
/// Renders pages on every core and hands them to one consumer in page order while holding only a few at a time.
/// Workers take a slot from a bounded pool, then claim the next page index, so the pages in flight are always the
/// next few in order and the consumer never waits on a page whose worker is starved of a slot. The rendered callback
/// of a page has finished before the page is handed to the consumer, so once the consumer holds page i the callbacks
/// of pages 0 to i have all run and <see cref="RenderedCount"/> counts them. A render failure (or a callback that
/// throws) stops the workers and surfaces to the consumer as <see cref="RenderFailedException"/>; cancellation
/// surfaces as <see cref="OperationCanceledException"/>. Dispose stops the workers.
/// </summary>
public sealed class RenderPipeline : IDisposable
{
    /// <summary>Rendered pages held at once (rendering or waiting for the consumer), the PRD's "a few".</summary>
    public const int DefaultCapacity = 4;

    private readonly IReadOnlyList<PageSpec> _pages;
    private readonly Func<PageSpec, RenderedPage> _render;
    private readonly Action<PageSpec, int>? _onRendered;
    private readonly int _degree;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _stop;
    private readonly CancellationToken _cancellation;
    private readonly object _gate = new();
    private readonly Dictionary<int, RenderedPage> _ready = new();
    private Task[]? _workers;
    private int _nextIndex = -1;
    private int _renderedCount;
    private Exception? _failure;
    private PageSpec? _failedPage;
    private bool _disposed;

    /// <param name="pages">Pages to render, in the order the consumer wants them.</param>
    /// <param name="render">Renders one page; called concurrently.</param>
    /// <param name="maxDegreeOfParallelism">Worker count (GenerateOrchestratorOptions.DefaultParallelism in production: the processor count, at most 4).</param>
    /// <param name="capacity">Pages held at once; at least 1.</param>
    /// <param name="cancellationToken">Stops rendering between pages.</param>
    /// <param name="onRendered">Called on a worker thread after each render with the page and the running count, before the page is handed to the consumer.</param>
    public RenderPipeline(
        IReadOnlyList<PageSpec> pages,
        Func<PageSpec, RenderedPage> render,
        int maxDegreeOfParallelism,
        int capacity,
        CancellationToken cancellationToken,
        Action<PageSpec, int>? onRendered = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _pages = pages;
        _render = render;
        _onRendered = onRendered;
        _degree = Math.Min(maxDegreeOfParallelism, Math.Max(1, pages.Count));
        _slots = new SemaphoreSlim(capacity, capacity);
        _cancellation = cancellationToken;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    /// <summary>Pages rendered so far.</summary>
    public int RenderedCount => Volatile.Read(ref _renderedCount);

    /// <summary>The rendered pages in order. Enumerate once; each page's slot is released when the next one is asked for.</summary>
    public IEnumerable<RenderedPage> Pages()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Start();
        for (int i = 0; i < _pages.Count; i++)
        {
            RenderedPage page = WaitFor(i);
            yield return page;
            _slots.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        if (_workers is not null)
        {
            try
            {
                Task.WaitAll(_workers, TimeSpan.FromSeconds(30));
            }
            catch (AggregateException)
            {
                // Worker faults are already recorded in _failure; nothing else to surface here.
            }
        }

        lock (_gate)
        {
            _ready.Clear();
        }

        _stop.Dispose();
        _slots.Dispose();
    }

    private void Start()
    {
        if (_workers is not null)
        {
            return;
        }

        _workers = Enumerable.Range(0, _degree)
            .Select(_ => Task.Factory.StartNew(WorkerLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();
    }

    private RenderedPage WaitFor(int index)
    {
        lock (_gate)
        {
            while (true)
            {
                if (_ready.Remove(index, out RenderedPage? page))
                {
                    return page;
                }

                if (_failure is not null)
                {
                    throw new RenderFailedException(_failedPage!, _failure);
                }

                _cancellation.ThrowIfCancellationRequested();
                Monitor.Wait(_gate, TimeSpan.FromMilliseconds(50));
            }
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            try
            {
                _slots.Wait(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            int index = Interlocked.Increment(ref _nextIndex);
            if (index >= _pages.Count || _stop.IsCancellationRequested)
            {
                ReleaseSlotQuietly();
                return;
            }

            PageSpec spec = _pages[index];
            try
            {
                RenderedPage page = _render(spec);

                // Count and report before the hand-off: once the consumer holds the page its callback has finished.
                int count = Interlocked.Increment(ref _renderedCount);
                _onRendered?.Invoke(spec, count);
                lock (_gate)
                {
                    _ready[index] = page;
                    Monitor.PulseAll(_gate);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lock (_gate)
                {
                    if (_failure is null)
                    {
                        _failure = ex;
                        _failedPage = spec;
                    }

                    Monitor.PulseAll(_gate);
                }

                _stop.Cancel();
                return;
            }
        }
    }

    private void ReleaseSlotQuietly()
    {
        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed while the worker was finishing; nothing to release.
        }
    }
}
