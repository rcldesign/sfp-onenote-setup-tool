namespace Sfp.OneNote.Com;

/// <summary>
/// The OneNote.Application calls the writer and Refresh need, with the out parameters turned into return values.
/// Every call runs on the wrapper's single COM thread, is timed and logged, is retried once when OneNote reports
/// that it is busy, and fails with a typed <see cref="OneNoteException"/> carrying the HRESULT.
/// Object IDs are the strings OneNote returns ("{guid}{1}{B0}"); XML is the 2013 schema.
/// </summary>
public interface IOneNoteApp : IDisposable
{
    /// <summary>The installed OneNote version ("16.0.17928.20156"), or "unknown" when it cannot be read.</summary>
    string Version { get; }

    /// <summary>Hierarchy XML below <paramref name="startId"/> (null or empty for all notebooks) to the given depth.</summary>
    string GetHierarchy(string? startId, HierarchyScope scope);

    /// <summary>Opens or creates a notebook, folder or section at <paramref name="path"/> and returns its ID.</summary>
    string OpenHierarchy(string path, string? relativeToId, CreateFileType createFileType);

    /// <summary>Creates a page in a section and returns its ID.</summary>
    string CreateNewPage(string sectionId, NewPageStyle pageStyle);

    /// <summary>Applies hierarchy changes (names, order, page levels, section color).</summary>
    void UpdateHierarchy(string xml);

    /// <summary>Writes page content. Pass <see cref="DateTime.MinValue"/> to skip the last-modified check.</summary>
    void UpdatePageContent(string xml, DateTime dateExpectedLastModified);

    /// <summary>Reads a page as XML.</summary>
    string GetPageContent(string pageId, PageInfo pageInfo);

    /// <summary>An onenote: URL to a hierarchy object, or to an object on a page when <paramref name="pageObjectId"/> is given.</summary>
    string GetHyperlinkToObject(string id, string? pageObjectId);

    void SyncHierarchy(string id);

    void NavigateTo(string id);

    void DeleteHierarchy(string id, bool deletePermanently);

    void CloseNotebook(string id);
}
