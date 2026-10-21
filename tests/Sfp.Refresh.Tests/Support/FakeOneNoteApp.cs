using System.Globalization;
using System.Xml.Linq;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;

namespace Sfp.Refresh.Tests.Support;

/// <summary>
/// An in-memory OneNote for the Refresh tests: notebooks, sections and pages with the hierarchy XML GetHierarchy
/// returns, page XML stored as written with stable objectIDs stamped on every outline, paragraph, image and ink element
/// the way OneNote does, image bytes kept apart so a piBasic read hands back a CallbackID and a write carrying a
/// CallbackID keeps the bytes. UpdatePageContent merges by objectID the way OneNote does: an object the XML carries with
/// a known objectID replaces the stored one, an object without one is added, and an object the XML leaves out is kept
/// (only DeletePageContent removes one). Every write stamps the page's lastModifiedTime (one second later each time), a
/// read hands it back on one:Page, and a write passing a dateExpectedLastModified other than DateTime.MinValue that does
/// not match it fails with hrLastModifiedDateDidNotMatch, as OneNote does. Notebooks can be closed and reopened by path,
/// and every ID can be renumbered to simulate a move to OneDrive. Every call is recorded. Meant to be shared with the
/// OneNote tests when the integrator merges.
/// </summary>
public sealed class FakeOneNoteApp : IOneNoteApp
{
    /// <summary>Top-level page children that are content objects, merged by objectID; every other child (Meta, PageSettings, Title ...) is replaced by name.</summary>
    private static readonly HashSet<string> ContentObjects = new(StringComparer.Ordinal) { "Outline", "Image", "InkDrawing", "InsertedFile", "MediaFile" };

    private readonly Dictionary<string, Notebook> notebooks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Section> sections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Page> pages = new(StringComparer.Ordinal);
    private readonly List<(string Call, string Detail)> calls = new();
    private int nextId;
    private DateTimeOffset clock = new(2026, 12, 20, 10, 0, 0, TimeSpan.Zero);
    private bool inBeforeWrite;

    public string Version => "16.0.fake";

    /// <summary>Every call in order with a short detail (an ID or a path).</summary>
    public IReadOnlyList<(string Call, string Detail)> Calls => calls;

    /// <summary>The XML of the last UpdatePageContent call, or null.</summary>
    public string? LastUpdateXml { get; private set; }

    /// <summary>Decides whether an UpdatePageContent call fails: given the page ID, the exception to throw or null.</summary>
    public Func<string, Exception?>? UpdatePageContentFailure { get; set; }

    /// <summary>
    /// Runs at the start of every UpdatePageContent and DeletePageContent, given the page ID, before the last-modified
    /// check: a test makes the user edit the page there, between Refresh's read and its write. Writes made inside it do
    /// not run it again.
    /// </summary>
    public Action<string>? BeforeWrite { get; set; }

    /// <summary>Decides whether a GetPageContent call fails: given the page ID, the exception to throw or null.</summary>
    public Func<string, Exception?>? GetPageContentFailure { get; set; }

    /// <summary>Decides whether a GetHierarchy call fails: given the start ID and scope, the exception to throw or null.</summary>
    public Func<string?, HierarchyScope, Exception?>? GetHierarchyFailure { get; set; }

    /// <summary>The zone OneNote reads a dateExpectedLastModified in (a COM DATE has none); UTC unless a test sets another.</summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>Thrown by every call when set (simulates OneNote not installed or not reachable).</summary>
    public Exception? Unavailable { get; set; }

    /// <summary>The lastModifiedTime of a page, or null when it was never written.</summary>
    public DateTimeOffset? LastModified(string pageId) => pages[pageId].Modified;

    public bool Disposed { get; private set; }

    public int CallCount(string call) => calls.Count(c => c.Call == call);

    /// <summary>How many times a page was written.</summary>
    public int UpdateCount(string pageId) => calls.Count(c => c.Call == "UpdatePageContent" && c.Detail == pageId);

    /// <summary>The background image bytes stored for a page (base64), or null.</summary>
    public string? ImageData(string pageId) => pages[pageId].ImageData;

    /// <summary>The stored XML of a page (objectIDs stamped, image as CallbackID), or null when never written.</summary>
    public string? StoredXml(string pageId) => pages[pageId].Xml;

    /// <summary>Notebook ID by name, or null.</summary>
    public string? NotebookId(string name) => notebooks.Values.FirstOrDefault(n => n.Name == name)?.Id;

    /// <summary>Marks a notebook closed: GetHierarchy no longer lists it until OpenHierarchy on its path.</summary>
    public void Close(string notebookId) => notebooks[notebookId].Open = false;

    /// <summary>
    /// Deletes a page the way OneNote does: it moves to the notebook's recycle bin, which GetHierarchy lists as the
    /// OneNote_RecycleBin section group (isRecycleBin) holding a Deleted Pages section (isInRecycleBin). The fake lists
    /// the bin ahead of the notebook's sections, so a reader that relied on document order would meet it first.
    /// </summary>
    public void MoveToRecycleBin(string pageId)
    {
        Page page = pages[pageId];
        Section section = sections[page.SectionId];
        section.PageIds.Remove(pageId);
        notebooks[section.NotebookId].RecycleBin.Add(pageId);
    }

    /// <summary>Puts a copy of a page (same content and sfp.key, new page ID) in the notebook's recycle bin, as deleting a duplicated page leaves one; returns its ID.</summary>
    public string CopyToRecycleBin(string pageId)
    {
        Page source = pages[pageId];
        Page copy = new(NewId("p"), source.SectionId) { Name = source.Name, Level = source.Level, ImageData = source.ImageData, Modified = source.Modified };
        copy.Xml = source.Xml?.Replace(source.Prefix, copy.Prefix, StringComparison.Ordinal).Replace("ID=\"" + pageId + "\"", "ID=\"" + copy.Id + "\"", StringComparison.Ordinal);
        pages[copy.Id] = copy;
        notebooks[sections[source.SectionId].NotebookId].RecycleBin.Add(copy.Id);
        return copy.Id;
    }

    /// <summary>
    /// Gives the notebook, its sections, its pages and every objectID inside them new IDs, the way IDs change after a
    /// notebook moves to OneDrive. Returns the notebook's new ID.
    /// </summary>
    public string RenumberIds(string notebookId)
    {
        Notebook nb = notebooks[notebookId];
        notebooks.Remove(notebookId);
        nb.Id = NewId("nb");
        notebooks[nb.Id] = nb;
        for (int i = 0; i < nb.SectionIds.Count; i++)
        {
            Section section = sections[nb.SectionIds[i]];
            sections.Remove(section.Id);
            section.Id = NewId("s");
            sections[section.Id] = section;
            nb.SectionIds[i] = section.Id;
            for (int p = 0; p < section.PageIds.Count; p++)
            {
                Page page = pages[section.PageIds[p]];
                pages.Remove(page.Id);
                string oldPrefix = page.Prefix;
                page.Id = NewId("p");
                page.SectionId = section.Id;
                if (page.Xml is not null)
                {
                    page.Xml = page.Xml.Replace(oldPrefix, page.Prefix, StringComparison.Ordinal).Replace("ID=\"" + section.PageIds[p] + "\"", "ID=\"" + page.Id + "\"", StringComparison.Ordinal);
                }

                pages[page.Id] = page;
                section.PageIds[p] = page.Id;
            }
        }

        return nb.Id;
    }

    public string GetHierarchy(string? startId, HierarchyScope scope)
    {
        Record("GetHierarchy", (startId ?? "") + ":" + scope);
        Exception? failure = GetHierarchyFailure?.Invoke(startId, scope);
        if (failure is not null)
        {
            throw failure;
        }

        XNamespace one = OneNoteXml.Namespace2013;
        if (string.IsNullOrEmpty(startId))
        {
            XElement root = new(one + "Notebooks", new XAttribute(XNamespace.Xmlns + "one", one.NamespaceName));
            foreach (Notebook nb in notebooks.Values.Where(n => n.Open))
            {
                root.Add(NotebookElement(one, nb, scope));
            }

            return OneNoteXml.Serialize(root);
        }

        if (notebooks.TryGetValue(startId, out Notebook? notebook) && notebook.Open)
        {
            return Serialize(one, NotebookElement(one, notebook, scope));
        }

        if (sections.TryGetValue(startId, out Section? section))
        {
            return Serialize(one, SectionElement(one, section, scope));
        }

        if (pages.TryGetValue(startId, out Page? page))
        {
            return Serialize(one, PageElement(one, page));
        }

        throw new OneNoteException("GetHierarchy", OneNoteHResults.PageObjectDoesNotExist, "No object " + startId, null);
    }

    public string OpenHierarchy(string path, string? relativeToId, CreateFileType createFileType)
    {
        Record("OpenHierarchy", path + ":" + createFileType);
        switch (createFileType)
        {
            case CreateFileType.Notebook:
            {
                Notebook existing = notebooks.Values.FirstOrDefault(n => n.Path == path) ?? Create(path);
                existing.Open = true;
                return existing.Id;
            }

            case CreateFileType.None:
            {
                Notebook? existing = notebooks.Values.FirstOrDefault(n => string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    throw new OneNoteException("OpenHierarchy", OneNoteHResults.FileDoesNotExist, "No notebook at " + path, null);
                }

                existing.Open = true;
                return existing.Id;
            }

            case CreateFileType.Section:
            {
                if (relativeToId is null || !notebooks.TryGetValue(relativeToId, out Notebook? nb))
                {
                    throw new OneNoteException("OpenHierarchy", OneNoteHResults.NotebookDoesNotExist, "No notebook " + relativeToId, null);
                }

                Section section = new(NewId("s"), Path.GetFileNameWithoutExtension(path), path, nb.Id);
                sections[section.Id] = section;
                nb.SectionIds.Add(section.Id);
                return section.Id;
            }

            default:
                throw new OneNoteException("OpenHierarchy", OneNoteHResults.FileDoesNotExist, "Unsupported create type " + createFileType, null);
        }
    }

    public string CreateNewPage(string sectionId, NewPageStyle pageStyle)
    {
        Record("CreateNewPage", sectionId);
        if (!sections.TryGetValue(sectionId, out Section? section))
        {
            throw new OneNoteException("CreateNewPage", OneNoteHResults.SectionDoesNotExist, "No section " + sectionId, null);
        }

        Page page = new(NewId("p"), section.Id);
        pages[page.Id] = page;
        section.PageIds.Add(page.Id);
        return page.Id;
    }

    public void UpdateHierarchy(string xml)
    {
        XElement root = XElement.Parse(xml);
        XNamespace one = root.Name.Namespace;
        string id = (string?)root.Attribute("ID") ?? "";
        Record("UpdateHierarchy", id);
        if (!sections.TryGetValue(id, out Section? section))
        {
            throw new OneNoteException("UpdateHierarchy", OneNoteHResults.SectionDoesNotExist, "No section " + id, null);
        }

        section.Name = (string?)root.Attribute("name") ?? section.Name;
        foreach (XElement pageElement in root.Elements(one + "Page"))
        {
            string pageId = (string?)pageElement.Attribute("ID") ?? "";
            if (pages.TryGetValue(pageId, out Page? page))
            {
                page.Name = (string?)pageElement.Attribute("name") ?? page.Name;
                page.Level = int.Parse((string?)pageElement.Attribute("pageLevel") ?? "1", CultureInfo.InvariantCulture);
            }
        }
    }

    public void UpdatePageContent(string xml, DateTime dateExpectedLastModified)
    {
        Guard();
        XElement root = XElement.Parse(xml);
        string id = (string?)root.Attribute("ID") ?? "";
        Record("UpdatePageContent", id);
        LastUpdateXml = xml;
        RunBeforeWrite(id);
        Exception? failure = UpdatePageContentFailure?.Invoke(id);
        if (failure is not null)
        {
            throw failure;
        }

        if (!pages.TryGetValue(id, out Page? page))
        {
            throw new OneNoteException("UpdatePageContent", OneNoteHResults.PageDoesNotExist, "No page " + id, null);
        }

        CheckLastModified(page, dateExpectedLastModified, "UpdatePageContent");
        XNamespace one = root.Name.Namespace;
        foreach (XElement element in root.Descendants().Where(e => e.Name.Namespace == one && e.Name.LocalName is "Outline" or "OE" or "Image" or "InkWord" or "InkDrawing"))
        {
            if (string.IsNullOrEmpty((string?)element.Attribute("objectID")))
            {
                element.SetAttributeValue("objectID", page.NextObjectId());
            }
        }

        foreach (XElement image in root.Descendants(one + "Image").ToList())
        {
            XElement? data = image.Element(one + "Data");
            if (data is not null)
            {
                page.ImageData = data.Value;
                data.ReplaceWith(new XElement(one + "CallbackID", new XAttribute("callbackID", page.Prefix + "{cb}{B0}")));
            }
        }

        string? title = root.Element(one + "Title")?.Descendants(one + "T").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(title))
        {
            page.Name = PageXmlParser.PlainText(title);
        }

        XElement stored = page.Xml is null ? root : Merge(XElement.Parse(page.Xml), root, one);
        Touch(page, stored);
        page.Xml = OneNoteXml.Serialize(stored);
    }

    public string GetPageContent(string pageId, PageInfo pageInfo)
    {
        Guard();
        Record("GetPageContent", pageId);
        Exception? failure = GetPageContentFailure?.Invoke(pageId);
        if (failure is not null)
        {
            throw failure;
        }

        if (!pages.TryGetValue(pageId, out Page? page))
        {
            throw new OneNoteException("GetPageContent", OneNoteHResults.PageDoesNotExist, "No page " + pageId, null);
        }

        XNamespace one = OneNoteXml.Namespace2013;
        if (page.Xml is null)
        {
            return Serialize(one, new XElement(one + "Page", new XAttribute("ID", pageId), new XAttribute("name", page.Name)));
        }

        if (pageInfo is PageInfo.BinaryData or PageInfo.BinaryDataSelection or PageInfo.All && page.ImageData is not null)
        {
            XElement root = XElement.Parse(page.Xml);
            foreach (XElement callback in root.Descendants(one + "Image").Select(i => i.Element(one + "CallbackID")).OfType<XElement>().ToList())
            {
                callback.ReplaceWith(new XElement(one + "Data", page.ImageData));
            }

            return OneNoteXml.Serialize(root);
        }

        return page.Xml;
    }

    public string GetHyperlinkToObject(string id, string? pageObjectId)
    {
        Record("GetHyperlinkToObject", id);
        if (pages.TryGetValue(id, out Page? page))
        {
            return "onenote:#" + Uri.EscapeDataString(page.Name) + "&section-id=" + page.SectionId + "&page-id=" + id + "&end";
        }

        if (sections.ContainsKey(id))
        {
            return "onenote:#section-id=" + id + "&end";
        }

        throw new OneNoteException("GetHyperlinkToObject", OneNoteHResults.NoHyperlinkToObject, "No object " + id, null);
    }

    public void SyncHierarchy(string id)
    {
        Guard();
        Record("SyncHierarchy", id);
    }

    public void NavigateTo(string id) => Record("NavigateTo", id);

    /// <summary>Removes the element with the given objectID from the stored page XML, as OneNote does.</summary>
    public void DeletePageContent(string pageId, string objectId, DateTime dateExpectedLastModified)
    {
        Guard();
        Record("DeletePageContent", pageId + " " + objectId);
        RunBeforeWrite(pageId);
        if (!pages.TryGetValue(pageId, out Page? page) || page.Xml is null)
        {
            throw new OneNoteException("DeletePageContent", OneNoteHResults.PageDoesNotExist, "No page " + pageId, null);
        }

        CheckLastModified(page, dateExpectedLastModified, "DeletePageContent");
        XElement root = XElement.Parse(page.Xml);
        XElement? target = root.Descendants().FirstOrDefault(e => (string?)e.Attribute("objectID") == objectId);
        if (target is null)
        {
            throw new OneNoteException("DeletePageContent", OneNoteHResults.PageObjectDoesNotExist, "No object " + objectId + " on page " + pageId, null);
        }

        target.Remove();
        Touch(page, root);
        page.Xml = OneNoteXml.Serialize(root);
    }

    public void DeleteHierarchy(string id, bool deletePermanently)
    {
        Record("DeleteHierarchy", id);
        notebooks.Remove(id);
    }

    public void CloseNotebook(string id)
    {
        Record("CloseNotebook", id);
        Close(id);
    }

    public void Dispose() => Disposed = true;

    private static string Serialize(XNamespace one, XElement element)
    {
        element.Add(new XAttribute(XNamespace.Xmlns + "one", one.NamespaceName));
        return OneNoteXml.Serialize(element);
    }

    /// <summary>
    /// The stored page with an UpdatePageContent applied as OneNote applies it: page attributes are taken from the XML,
    /// a content object (outline, image, ink) with an objectID already on the page replaces it in place, any other content
    /// object is added at the end, the other children (Meta by name, PageSettings, Title ...) replace the stored element of
    /// the same name, and everything the XML leaves out stays on the page.
    /// </summary>
    private static XElement Merge(XElement stored, XElement incoming, XNamespace one)
    {
        foreach (XAttribute attribute in incoming.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            stored.SetAttributeValue(attribute.Name, attribute.Value);
        }

        foreach (XElement child in incoming.Elements().ToList())
        {
            XElement copy = new(child);
            if (child.Name.Namespace == one && ContentObjects.Contains(child.Name.LocalName))
            {
                string id = (string?)child.Attribute("objectID") ?? "";
                XElement? existing = stored.Elements().FirstOrDefault(e => (string?)e.Attribute("objectID") == id);
                if (existing is not null)
                {
                    existing.ReplaceWith(copy);
                }
                else
                {
                    stored.Add(copy);
                }

                continue;
            }

            XElement? same = child.Name == one + "Meta"
                ? stored.Elements(one + "Meta").FirstOrDefault(m => (string?)m.Attribute("name") == (string?)child.Attribute("name"))
                : stored.Element(child.Name);
            if (same is not null)
            {
                same.ReplaceWith(copy);
            }
            else if (stored.Elements().FirstOrDefault(e => e.Name.Namespace == one && ContentObjects.Contains(e.Name.LocalName)) is XElement firstContent)
            {
                firstContent.AddBeforeSelf(copy);
            }
            else
            {
                stored.Add(copy);
            }
        }

        return stored;
    }

    private void Guard()
    {
        if (Unavailable is not null)
        {
            throw Unavailable;
        }
    }

    private void RunBeforeWrite(string pageId)
    {
        if (BeforeWrite is null || inBeforeWrite)
        {
            return;
        }

        inBeforeWrite = true;
        try
        {
            BeforeWrite(pageId);
        }
        finally
        {
            inBeforeWrite = false;
        }
    }

    /// <summary>Fails the write when the caller expected another last-modified time than the page has (DateTime.MinValue skips the check).</summary>
    private void CheckLastModified(Page page, DateTime expected, string call)
    {
        if (expected == DateTime.MinValue)
        {
            return;
        }

        DateTime expectedUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(expected, DateTimeKind.Unspecified), TimeZone);
        if (page.Modified is not DateTimeOffset modified || modified.UtcDateTime != expectedUtc)
        {
            throw new OneNoteException(
                call,
                OneNoteHResults.LastModifiedDateDidNotMatch,
                string.Create(CultureInfo.InvariantCulture, $"Page {page.Id} was last modified at {page.Modified:O}, not {expectedUtc:O}."),
                null);
        }
    }

    /// <summary>Advances the page's last-modified time and stamps it on the stored root, as every OneNote write does.</summary>
    private void Touch(Page page, XElement root)
    {
        clock = clock.AddSeconds(1);
        page.Modified = clock;
        root.SetAttributeValue("lastModifiedTime", clock.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }

    private Notebook Create(string path)
    {
        Notebook nb = new(NewId("nb"), Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), path);
        notebooks[nb.Id] = nb;
        return nb;
    }

    private XElement NotebookElement(XNamespace one, Notebook nb, HierarchyScope scope)
    {
        XElement element = new(one + "Notebook", new XAttribute("name", nb.Name), new XAttribute("ID", nb.Id), new XAttribute("path", nb.Path));
        if (scope is HierarchyScope.Sections or HierarchyScope.Pages)
        {
            if (nb.RecycleBin.Count > 0)
            {
                XElement deleted = new(
                    one + "Section",
                    new XAttribute("name", "Deleted Pages"),
                    new XAttribute("ID", nb.Id + "{deleted}"),
                    new XAttribute("isInRecycleBin", "true"),
                    new XAttribute("isDeletedPages", "true"));
                if (scope == HierarchyScope.Pages)
                {
                    deleted.Add(nb.RecycleBin.Select(id => PageElement(one, pages[id])));
                }

                element.Add(new XElement(
                    one + "SectionGroup",
                    new XAttribute("name", "OneNote_RecycleBin"),
                    new XAttribute("ID", nb.Id + "{bin}"),
                    new XAttribute("isRecycleBin", "true"),
                    deleted));
            }

            foreach (string sectionId in nb.SectionIds)
            {
                element.Add(SectionElement(one, sections[sectionId], scope));
            }
        }

        return element;
    }

    private XElement SectionElement(XNamespace one, Section section, HierarchyScope scope)
    {
        XElement element = new(one + "Section", new XAttribute("name", section.Name), new XAttribute("ID", section.Id), new XAttribute("path", section.Path));
        if (scope == HierarchyScope.Pages)
        {
            foreach (string pageId in section.PageIds)
            {
                element.Add(PageElement(one, pages[pageId]));
            }
        }

        return element;
    }

    private static XElement PageElement(XNamespace one, Page page) =>
        new(one + "Page", new XAttribute("ID", page.Id), new XAttribute("name", page.Name), new XAttribute("pageLevel", page.Level.ToString(CultureInfo.InvariantCulture)));

    private string NewId(string kind)
    {
        int n = ++nextId;
        return "{" + kind + "-" + n.ToString("D4", CultureInfo.InvariantCulture) + "}{1}{B0}";
    }

    private void Record(string call, string detail)
    {
        Guard();
        calls.Add((call, detail));
    }

    private sealed class Notebook
    {
        public Notebook(string id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path;
        }

        public string Id { get; set; }

        public string Name { get; }

        public string Path { get; }

        public bool Open { get; set; } = true;

        public List<string> SectionIds { get; } = new();

        /// <summary>IDs of the deleted pages in the notebook's recycle bin.</summary>
        public List<string> RecycleBin { get; } = new();
    }

    private sealed class Section
    {
        public Section(string id, string name, string path, string notebookId)
        {
            Id = id;
            Name = name;
            Path = path;
            NotebookId = notebookId;
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string Path { get; }

        public string NotebookId { get; }

        public List<string> PageIds { get; } = new();
    }

    private sealed class Page
    {
        private int nextObject;

        public Page(string id, string sectionId)
        {
            Id = id;
            SectionId = sectionId;
        }

        public string Id { get; set; }

        public string SectionId { get; set; }

        public string Name { get; set; } = "";

        public int Level { get; set; } = 1;

        public string? Xml { get; set; }

        public string? ImageData { get; set; }

        /// <summary>When the page was last written, or null.</summary>
        public DateTimeOffset? Modified { get; set; }

        /// <summary>The "{p-0001}" part of the page ID that every objectID on the page starts with.</summary>
        public string Prefix => Id[..Id.IndexOf('}', StringComparison.Ordinal)] + "}";

        public string NextObjectId() => Prefix + "{" + (++nextObject).ToString(CultureInfo.InvariantCulture) + "}{B0}";
    }
}
