using Sfp.OneNote;

namespace Sfp.Refresh.Tests.Support;

/// <summary>
/// The real sidecar store, with failures injected the way a file held by a sync client or a virus scanner fails: the
/// next saves that match a target (<see cref="Journal"/>, <see cref="Final"/> or <see cref="Any"/>) throw an
/// <see cref="IOException"/>; every other save goes through.
/// </summary>
public sealed class FlakySidecarStore : ISidecarStore
{
    /// <summary>The message of every injected failure (a Windows sharing violation).</summary>
    public const string Message = "The process cannot access the file because it is being used by another process.";

    private readonly ISidecarStore inner;
    private Func<Sidecar, bool> target = Any;
    private int failures;

    public FlakySidecarStore(ISidecarStore inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

    /// <summary>Every call to <see cref="Save"/>, failed or not.</summary>
    public int Saves { get; private set; }

    /// <summary>The calls to <see cref="Save"/> that failed.</summary>
    public int Failed { get; private set; }

    /// <summary>Any save.</summary>
    public static bool Any(Sidecar sidecar) => true;

    /// <summary>The journal save a run makes before its first page write: some page holds journaled texts.</summary>
    public static bool Journal(Sidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        return sidecar.Pages.Values.Any(p => p.PendingTexts is { Count: > 0 });
    }

    /// <summary>A save in which no page holds journaled texts: in a run that journaled its writes, its final save once every write was recorded.</summary>
    public static bool Final(Sidecar sidecar) => !Journal(sidecar);

    /// <summary>Makes the next <paramref name="times"/> saves that match <paramref name="when"/> fail.</summary>
    public void FailNext(int times, Func<Sidecar, bool> when)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(times);
        ArgumentNullException.ThrowIfNull(when);
        failures = times;
        target = when;
    }

    public string PathFor(string notebookFolder) => inner.PathFor(notebookFolder);

    public Sidecar? Load(string path) => inner.Load(path);

    public void Save(string path, Sidecar sidecar)
    {
        Saves++;
        if (failures > 0 && target(sidecar))
        {
            failures--;
            Failed++;
            throw new IOException(Message);
        }

        inner.Save(path, sidecar);
    }
}
