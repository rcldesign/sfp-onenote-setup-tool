using System.Runtime.Versioning;
using Serilog;

namespace Sfp.OneNote.Com;

/// <summary>
/// The production OneNote.Application wrapper: late-bound through the ProgID, every call on one STA thread, timed and
/// logged, retried once after <see cref="RetryDelay"/> when OneNote is busy, and failures mapped to
/// <see cref="OneNoteException"/> and its subtypes. Construct once per run and dispose at the end.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OneNoteApp : IOneNoteApp
{
    /// <summary>The PRD's "retried once after 2 s".</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly OneNoteSession _session;

    public OneNoteApp(ILogger log)
        : this(log, RetryDelay)
    {
    }

    public OneNoteApp(ILogger log, TimeSpan retryDelay)
    {
        _session = new OneNoteSession(new ProgIdActivator(), log, retryDelay);
    }

    public string Version => _session.Version;

    public string GetHierarchy(string? startId, HierarchyScope scope) => _session.GetHierarchy(startId, scope);

    public string OpenHierarchy(string path, string? relativeToId, CreateFileType createFileType) => _session.OpenHierarchy(path, relativeToId, createFileType);

    public string CreateNewPage(string sectionId, NewPageStyle pageStyle) => _session.CreateNewPage(sectionId, pageStyle);

    public void UpdateHierarchy(string xml) => _session.UpdateHierarchy(xml);

    public void UpdatePageContent(string xml, DateTime dateExpectedLastModified) => _session.UpdatePageContent(xml, dateExpectedLastModified);

    public string GetPageContent(string pageId, PageInfo pageInfo) => _session.GetPageContent(pageId, pageInfo);

    public string GetHyperlinkToObject(string id, string? pageObjectId) => _session.GetHyperlinkToObject(id, pageObjectId);

    public void SyncHierarchy(string id) => _session.SyncHierarchy(id);

    public void NavigateTo(string id) => _session.NavigateTo(id);

    public void DeleteHierarchy(string id, bool deletePermanently) => _session.DeleteHierarchy(id, deletePermanently);

    public void DeletePageContent(string pageId, string objectId, DateTime dateExpectedLastModified) => _session.DeletePageContent(pageId, objectId, dateExpectedLastModified);

    public void CloseNotebook(string id) => _session.CloseNotebook(id);

    public void Dispose() => _session.Dispose();
}
