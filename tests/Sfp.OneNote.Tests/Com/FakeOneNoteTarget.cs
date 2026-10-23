using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Sfp.OneNote.Com;

namespace Sfp.OneNote.Tests.Com;

/// <summary>
/// Stands in for the late-bound OneNote.Application object. The session calls it through dynamic exactly as it calls
/// COM, so the method shapes here mirror the IApplication interface (enums as int, out parameters for results).
/// It records the thread every call ran on and throws queued COMExceptions to exercise the retry and mapping rules.
/// Public because the C# runtime binder needs to see it from the Sfp.OneNote assembly.
/// </summary>
public sealed class FakeOneNoteTarget : IDisposable
{
    private readonly ConcurrentQueue<Exception> _pendingFailures = new();
    private readonly List<(string Call, int ThreadId)> _calls = new();
    private readonly object _gate = new();

    public string HierarchyXml { get; set; } = "<one:Notebooks xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\"><one:Notebook name=\"SFP Q1 2027\" ID=\"{nb}\"/></one:Notebooks>";

    public string PageXml { get; set; } = "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" ID=\"{p}\"/>";

    public string? LastUpdatedPageXml { get; private set; }

    public string? LastHierarchyXml { get; private set; }

    public DateTime? LastExpectedLastModified { get; private set; }

    public int? DisposedOnThread { get; private set; }

    public IReadOnlyList<(string Call, int ThreadId)> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public int CallCount(string call) => Calls.Count(c => c.Call == call);

    /// <summary>The next call (whichever it is) throws a COMException with this HRESULT; queue several for a sequence.</summary>
    public void FailNext(params int[] hresults)
    {
        foreach (int hr in hresults)
        {
            _pendingFailures.Enqueue(new COMException("fake OneNote failure", hr));
        }
    }

    /// <summary>
    /// The next call (whichever it is) throws <paramref name="failure"/> as it is, for the exceptions the COM binder raises
    /// itself instead of a COMException (MissingMemberException, for one).
    /// </summary>
    public void FailNextWith(Exception failure) => _pendingFailures.Enqueue(failure);

    public void GetHierarchy(string startId, int scope, out string xml, int schema)
    {
        Enter("GetHierarchy");
        xml = HierarchyXml;
    }

    public void OpenHierarchy(string path, string relativeToId, out string id, int createFileType)
    {
        Enter("OpenHierarchy");
        id = "{id:" + path + "}";
    }

    public void CreateNewPage(string sectionId, out string pageId, int pageStyle)
    {
        Enter("CreateNewPage");
        pageId = "{page:" + sectionId + ":" + pageStyle + "}";
    }

    public void UpdateHierarchy(string xml, int schema)
    {
        Enter("UpdateHierarchy");
        LastHierarchyXml = xml;
    }

    public void UpdatePageContent(string xml, DateTime dateExpectedLastModified, int schema, bool force)
    {
        Enter("UpdatePageContent");
        LastUpdatedPageXml = xml;
        LastExpectedLastModified = dateExpectedLastModified;
    }

    public void GetPageContent(string pageId, out string xml, int pageInfo, int schema)
    {
        Enter("GetPageContent");
        xml = PageXml;
    }

    public void GetHyperlinkToObject(string id, string pageObjectId, out string link)
    {
        Enter("GetHyperlinkToObject");
        link = "onenote:#Page&section-id={s}&page-id=" + id + "&end";
    }

    public void SyncHierarchy(string id) => Enter("SyncHierarchy");

    public void NavigateTo(string id, string objectId, bool newWindow) => Enter("NavigateTo");

    public void DeleteHierarchy(string id, DateTime dateExpectedLastModified, bool deletePermanently) => Enter("DeleteHierarchy");

    public void CloseNotebook(string id, bool force) => Enter("CloseNotebook");

    public void Dispose() => DisposedOnThread = Environment.CurrentManagedThreadId;

    private void Enter(string call)
    {
        lock (_gate)
        {
            _calls.Add((call, Environment.CurrentManagedThreadId));
        }

        if (_pendingFailures.TryDequeue(out Exception? failure))
        {
            throw failure;
        }
    }
}

/// <summary>
/// Stands in for a OneNote.Application object without the automation members, as a registration that is not OneNote
/// desktop's, or a damaged install, gives: it has none of the IApplication methods, so every late-bound call finds no
/// such member and the runtime binder throws RuntimeBinderException, as it does for a COM object whose IDispatch does
/// not know the name. Public, like <see cref="FakeOneNoteTarget"/>, so the binder reports this type's member missing.
/// </summary>
public sealed class NoAutomationTarget
{
}

/// <summary>Hands a target (the fake, or another stand-in) to the session the way the ProgID activator hands OneNote, and records where it happened.</summary>
internal sealed class FakeActivator : IOneNoteActivator
{
    public FakeActivator(object target)
    {
        Target = target;
    }

    public object Target { get; }

    public int? ActivatedOnThread { get; private set; }

    public int ActivationCount { get; private set; }

    /// <summary>When set, Activate throws this instead of returning the fake (simulates a missing OneNote).</summary>
    public Exception? Failure { get; set; }

    public ActivatedApp Activate()
    {
        ActivatedOnThread = Environment.CurrentManagedThreadId;
        ActivationCount++;
        if (Failure is not null)
        {
            throw Failure;
        }

        return new ActivatedApp(Target, "16.0.fake");
    }
}
