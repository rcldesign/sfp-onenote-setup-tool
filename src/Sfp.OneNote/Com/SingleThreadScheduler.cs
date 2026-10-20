using System.Collections.Concurrent;

namespace Sfp.OneNote.Com;

/// <summary>
/// Runs every unit of work on one dedicated thread, in order, and hands the result or the exception back to the
/// caller. On Windows the thread is an STA so the OneNote COM object is created and used on one apartment; elsewhere
/// it is a plain thread with the same single-threaded guarantee, which is what the Linux tests exercise.
/// </summary>
internal sealed class SingleThreadScheduler : IDisposable
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(10);

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private volatile bool _disposed;

    public SingleThreadScheduler(string name)
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = name,
        };
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>Managed id of the worker thread; tests use it to prove every call landed there.</summary>
    public int ThreadId => _thread.ManagedThreadId;

    /// <summary>True when called from the worker thread itself.</summary>
    public bool IsOnWorkerThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>Runs <paramref name="work"/> on the worker thread and returns its result, rethrowing its exception unchanged.</summary>
    public T Run<T>(Func<T> work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOnWorkerThread)
        {
            return work();
        }

        TaskCompletionSource<T> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });

        // GetResult (not .Result) rethrows the original exception rather than an AggregateException.
        return done.Task.GetAwaiter().GetResult();
    }

    public void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    /// <summary>Stops accepting work, waits for the queue to drain (up to ten seconds) and lets the thread end.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        if (!IsOnWorkerThread)
        {
            _thread.Join(JoinTimeout);
        }

        _queue.Dispose();
    }

    private void Loop()
    {
        foreach (Action work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }
}
