using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace Sfp.OneNote.Com;

/// <summary>
/// The platform-neutral heart of the COM wrapper: owns the single worker thread, activates the target lazily on it,
/// makes every call through dynamic, times and logs each one, retries once when OneNote is busy and maps COM failures
/// to typed exceptions. <see cref="OneNoteApp"/> is the Windows production shell around it; tests drive it with a fake target.
/// </summary>
internal sealed class OneNoteSession : IOneNoteApp
{
    private const int Schema = (int)XmlSchemaVersion.Schema2013;

    private readonly IOneNoteActivator _activator;
    private readonly ILogger _log;
    private readonly TimeSpan _retryDelay;
    private readonly SingleThreadScheduler _scheduler;
    private ActivatedApp? _app;
    private bool _disposed;

    public OneNoteSession(IOneNoteActivator activator, ILogger log, TimeSpan retryDelay)
    {
        _activator = activator;
        _log = log;
        _retryDelay = retryDelay;
        _scheduler = new SingleThreadScheduler("sfp2onenote COM");
    }

    /// <summary>Managed id of the thread every call runs on.</summary>
    public int WorkerThreadId => _scheduler.ThreadId;

    public string Version => Call("Version", null, _ => EnsureApp().Version);

    public string GetHierarchy(string? startId, HierarchyScope scope) =>
        Call("GetHierarchy", scope.ToString(), app =>
        {
            string xml = "";
            app.GetHierarchy(startId ?? "", (int)scope, out xml, Schema);
            return xml;
        });

    public string OpenHierarchy(string path, string? relativeToId, CreateFileType createFileType) =>
        Call("OpenHierarchy", path, app =>
        {
            string id = "";
            app.OpenHierarchy(path, relativeToId ?? "", out id, (int)createFileType);
            return id;
        });

    public string CreateNewPage(string sectionId, NewPageStyle pageStyle) =>
        Call("CreateNewPage", sectionId, app =>
        {
            string id = "";
            app.CreateNewPage(sectionId, out id, (int)pageStyle);
            return id;
        });

    public void UpdateHierarchy(string xml) =>
        Call("UpdateHierarchy", LogSafe.Xml(xml), app =>
        {
            app.UpdateHierarchy(xml, Schema);
            return true;
        }, xml);

    public void UpdatePageContent(string xml, DateTime dateExpectedLastModified) =>
        Call("UpdatePageContent", LogSafe.Xml(xml), app =>
        {
            app.UpdatePageContent(xml, dateExpectedLastModified, Schema, false);
            return true;
        }, xml);

    public string GetPageContent(string pageId, PageInfo pageInfo) =>
        Call("GetPageContent", pageId, app =>
        {
            string xml = "";
            app.GetPageContent(pageId, out xml, (int)pageInfo, Schema);
            return xml;
        });

    public string GetHyperlinkToObject(string id, string? pageObjectId) =>
        Call("GetHyperlinkToObject", id, app =>
        {
            string link = "";
            app.GetHyperlinkToObject(id, pageObjectId ?? "", out link);
            return link;
        });

    public void SyncHierarchy(string id) =>
        Call("SyncHierarchy", id, app =>
        {
            app.SyncHierarchy(id);
            return true;
        });

    public void NavigateTo(string id) =>
        Call("NavigateTo", id, app =>
        {
            app.NavigateTo(id, "", false);
            return true;
        });

    public void DeleteHierarchy(string id, bool deletePermanently) =>
        Call("DeleteHierarchy", id, app =>
        {
            app.DeleteHierarchy(id, DateTime.MinValue, deletePermanently);
            return true;
        });

    public void DeletePageContent(string pageId, string objectId, DateTime dateExpectedLastModified) =>
        Call("DeletePageContent", pageId + " " + objectId, app =>
        {
            app.DeletePageContent(pageId, objectId, dateExpectedLastModified, false);
            return true;
        });

    public void CloseNotebook(string id) =>
        Call("CloseNotebook", id, app =>
        {
            app.CloseNotebook(id, false);
            return true;
        });

    /// <summary>Releases the COM object on the worker thread and stops the thread.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _scheduler.Run(ReleaseTarget);
        }
        catch (ObjectDisposedException)
        {
            // The scheduler was already gone; nothing to release.
        }

        _scheduler.Dispose();
    }

    /// <summary>
    /// Runs one COM call on the worker thread with the timing, logging, retry and mapping rules. <paramref name="detail"/>
    /// is a short, already-safe description for the log (an id, a path, or elided XML); <paramref name="xml"/> is the
    /// full body carried into a rejection so the orchestrator can save it.
    /// </summary>
    private T Call<T>(string name, string? detail, Func<dynamic, T> call, string? xml = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _scheduler.Run(() =>
        {
            // Typed as object here so the delegate invocation itself is static; only the calls inside the lambda are dynamic.
            object target = EnsureApp().Target;
            Stopwatch clock = Stopwatch.StartNew();
            _log.Debug("OneNote {Call} starting {Detail}", name, detail ?? "");
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    T result = call(target);
                    _log.Information("OneNote {Call} completed in {ElapsedMs} ms", name, clock.ElapsedMilliseconds);
                    return result;
                }
                catch (COMException ex) when (OneNoteHResults.IsBusy(ex.HResult))
                {
                    if (attempt >= 2)
                    {
                        _log.Error("OneNote {Call} rejected twice as busy after {ElapsedMs} ms: {HResult}", name, clock.ElapsedMilliseconds, OneNoteHResults.Describe(ex.HResult));
                        throw new OneNoteBusyException(name, ex.HResult, ex);
                    }

                    _log.Warning("OneNote {Call} was rejected as busy ({HResult}); retrying once after {DelayMs} ms", name, OneNoteHResults.Describe(ex.HResult), _retryDelay.TotalMilliseconds);
                    Thread.Sleep(_retryDelay);
                }
                catch (COMException ex)
                {
                    OneNoteException mapped = OneNoteHResults.Map(name, ex, xml);
                    _log.Error("OneNote {Call} failed after {ElapsedMs} ms: {HResult} {ExceptionType}", name, clock.ElapsedMilliseconds, OneNoteHResults.Describe(ex.HResult), mapped.GetType().Name);
                    throw mapped;
                }
            }
        });
    }

    private ActivatedApp EnsureApp()
    {
        if (_app is not null)
        {
            return _app;
        }

        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            _app = _activator.Activate();
        }
        catch (COMException ex)
        {
            _log.Error("OneNote activation failed: {HResult}", OneNoteHResults.Describe(ex.HResult));
            throw OneNoteHResults.Map(OneNoteHResults.ActivateCall, ex, null);
        }

        _log.Information("OneNote {Call} completed in {ElapsedMs} ms (version {Version})", OneNoteHResults.ActivateCall, clock.ElapsedMilliseconds, _app.Version);
        return _app;
    }

    private void ReleaseTarget()
    {
        object? target = _app?.Target;
        _app = null;
        if (target is null)
        {
            return;
        }

        if (OperatingSystem.IsWindows() && Marshal.IsComObject(target))
        {
            Marshal.FinalReleaseComObject(target);
        }
        else if (target is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _log.Debug("OneNote target released");
    }
}
