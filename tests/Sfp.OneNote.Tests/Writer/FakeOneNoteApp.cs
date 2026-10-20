using System.Globalization;
using System.Xml.Linq;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// An in-memory OneNote: notebooks, sections and pages created by the same calls the COM wrapper exposes, hierarchy
/// updates applied, page XML stored and handed back with objectIDs the way GetPageContent reports them, and onenote:
/// hyperlinks in the real shape. Every call is recorded so tests can assert the PRD call sequence without COM.
/// </summary>
public sealed class FakeOneNoteApp : IOneNoteApp
{
    private readonly Dictionary<string, Notebook> _notebooks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Section> _sections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Page> _pages = new(StringComparer.Ordinal);
    private readonly List<(string Call, string Detail)> _calls = new();
    private readonly object _gate = new();
    private int _nextId;

    /// <summary>Notebooks listed by GetHierarchy(null, Notebooks) before any is created; empty means "not signed in".</summary>
    public List<string> PreexistingNotebooks { get; } = new() { "Personal" };

    /// <summary>Create the notebook folder on disk at OpenHierarchy, as OneNote does.</summary>
    public bool CreateFoldersOnDisk { get; set; }

    /// <summary>Decides whether an UpdatePageContent call fails: given the XML, return the exception to throw or null.</summary>
    public Func<string, Exception?>? UpdatePageContentFailure { get; set; }

    /// <summary>Decides whether a GetHierarchy call fails.</summary>
    public Func<Exception?>? GetHierarchyFailure { get; set; }

    public bool Disposed { get; private set; }

    public string Version => "16.0.fake";

    public IReadOnlyList<(string Call, string Detail)> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    /// <summary>The calls of one kind, in order, with their detail.</summary>
    public IReadOnlyList<string> CallsOf(string call) => Calls.Where(c => c.Call == call).Select(c => c.Detail).ToList();

    /// <summary>Section names of a notebook in creation order.</summary>
    public IReadOnlyList<string> SectionNames(string notebookId) => _notebooks[notebookId].SectionIds.Select(id => _sections[id].Name).ToList();

    /// <summary>Section color as last applied, or null.</summary>
    public string? SectionColor(string sectionId) => _sections[sectionId].Color;

    /// <summary>Pages of a section in display order: (id, name, level).</summary>
    public IReadOnlyList<(string Id, string Name, int Level)> PagesOf(string sectionId) =>
        _sections[sectionId].PageIds.Select(id => (id, _pages[id].Name, _pages[id].Level)).ToList();

    /// <summary>The XML last written for a page, or null.</summary>
    public string? PageContent(string pageId) => _pages.TryGetValue(pageId, out Page? p) ? p.Xml : null;

    /// <summary>Every page that has content, keyed by page id.</summary>
    public IReadOnlyDictionary<string, string> WrittenPages => _pages.Where(p => p.Value.Xml is not null).ToDictionary(p => p.Key, p => p.Value.Xml!, StringComparer.Ordinal);

    public string GetHierarchy(string? startId, HierarchyScope scope)
    {
        Record("GetHierarchy", (startId ?? "") + ":" + scope);
        Exception? failure = GetHierarchyFailure?.Invoke();
        if (failure is not null)
        {
            throw failure;
        }

        XNamespace one = OneNoteXml.Namespace2013;
        if (string.IsNullOrEmpty(startId))
        {
            XElement notebooks = new(one + "Notebooks", new XAttribute(XNamespace.Xmlns + "one", one.NamespaceName));
            foreach (string name in PreexistingNotebooks)
            {
                notebooks.Add(new XElement(one + "Notebook", new XAttribute("name", name), new XAttribute("ID", "{pre:" + name + "}"), new XAttribute("path", "C:\\Pre\\" + name)));
            }

            foreach (Notebook nb in _notebooks.Values)
            {
                notebooks.Add(NotebookElement(one, nb, scope));
            }

            return OneNoteXml.Serialize(notebooks);
        }

        if (_notebooks.TryGetValue(startId, out Notebook? notebook))
        {
            XElement element = NotebookElement(one, notebook, scope);
            element.Add(new XAttribute(XNamespace.Xmlns + "one", one.NamespaceName));
            return OneNoteXml.Serialize(element);
        }

        if (_sections.TryGetValue(startId, out Section? section))
        {
            XElement element = SectionElement(one, section, scope);
            element.Add(new XAttribute(XNamespace.Xmlns + "one", one.NamespaceName));
            return OneNoteXml.Serialize(element);
        }

        throw new OneNoteException("GetHierarchy", OneNoteHResults.ObjectDoesNotExist, "No object " + startId, null);
    }

    public string OpenHierarchy(string path, string? relativeToId, CreateFileType createFileType)
    {
        Record("OpenHierarchy", path + ":" + createFileType);
        switch (createFileType)
        {
            case CreateFileType.Notebook:
            {
                string id = NewId("nb");
                _notebooks[id] = new Notebook(id, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), path);
                if (CreateFoldersOnDisk)
                {
                    Directory.CreateDirectory(path);
                }

                return id;
            }

            case CreateFileType.Section:
            {
                if (relativeToId is null || !_notebooks.TryGetValue(relativeToId, out Notebook? nb))
                {
                    throw new OneNoteException("OpenHierarchy", OneNoteHResults.NotebookDoesNotExist, "No notebook " + relativeToId, null);
                }

                string id = NewId("s");
                string name = Path.GetFileNameWithoutExtension(path);
                _sections[id] = new Section(id, name, path, nb.Id);
                nb.SectionIds.Add(id);
                if (CreateFoldersOnDisk)
                {
                    File.WriteAllBytes(path, new byte[1024]);
                }

                return id;
            }

            default:
                throw new OneNoteException("OpenHierarchy", OneNoteHResults.FileDoesNotExist, "Unsupported create type " + createFileType, null);
        }
    }

    public string CreateNewPage(string sectionId, NewPageStyle pageStyle)
    {
        Record("CreateNewPage", sectionId + ":" + pageStyle);
        if (!_sections.TryGetValue(sectionId, out Section? section))
        {
            throw new OneNoteException("CreateNewPage", OneNoteHResults.SectionDoesNotExist, "No section " + sectionId, null);
        }

        string id = NewId("p");
        _pages[id] = new Page(id, section.Id) { Name = pageStyle == NewPageStyle.BlankPageNoTitle ? "Untitled page" : "" , Style = pageStyle };
        section.PageIds.Add(id);
        return id;
    }

    public void UpdateHierarchy(string xml)
    {
        Record("UpdateHierarchy", LogSafeDetail(xml));
        XElement root = XElement.Parse(xml);
        XNamespace one = root.Name.Namespace;
        if (root.Name.LocalName != "Section")
        {
            throw new OneNoteXmlRejectedException("UpdateHierarchy", OneNoteHResults.InvalidXml, "Only one:Section is supported by the fake", xml, null);
        }

        string id = (string?)root.Attribute("ID") ?? "";
        if (!_sections.TryGetValue(id, out Section? section))
        {
            throw new OneNoteException("UpdateHierarchy", OneNoteHResults.SectionDoesNotExist, "No section " + id, null);
        }

        section.Name = (string?)root.Attribute("name") ?? section.Name;
        section.Color = (string?)root.Attribute("color") ?? section.Color;
        List<string> order = new();
        foreach (XElement pageElement in root.Elements(one + "Page"))
        {
            string pageId = (string?)pageElement.Attribute("ID") ?? "";
            if (!_pages.TryGetValue(pageId, out Page? page) || page.SectionId != section.Id)
            {
                throw new OneNoteException("UpdateHierarchy", OneNoteHResults.PageDoesNotExist, "No page " + pageId + " in section " + id, null);
            }

            page.Name = (string?)pageElement.Attribute("name") ?? page.Name;
            page.Level = int.Parse((string?)pageElement.Attribute("pageLevel") ?? "1", CultureInfo.InvariantCulture);
            order.Add(pageId);
        }

        if (order.Count > 0)
        {
            List<string> rest = section.PageIds.Where(p => !order.Contains(p)).ToList();
            section.PageIds.Clear();
            section.PageIds.AddRange(order);
            section.PageIds.AddRange(rest);
        }
    }

    public void UpdatePageContent(string xml, DateTime dateExpectedLastModified)
    {
        Record("UpdatePageContent", LogSafeDetail(xml));
        LastExpectedLastModified = dateExpectedLastModified;
        Exception? failure = UpdatePageContentFailure?.Invoke(xml);
        if (failure is not null)
        {
            throw failure;
        }

        XElement root = XElement.Parse(xml);
        string id = (string?)root.Attribute("ID") ?? "";
        if (!_pages.TryGetValue(id, out Page? page))
        {
            throw new OneNoteException("UpdatePageContent", OneNoteHResults.PageDoesNotExist, "No page " + id, null);
        }

        page.Xml = xml;
        XNamespace one = root.Name.Namespace;
        string? title = root.Element(one + "Title")?.Descendants(one + "T").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(title))
        {
            page.Name = PageXmlParser.PlainText(title);
        }
    }

    public DateTime? LastExpectedLastModified { get; private set; }

    public string GetPageContent(string pageId, PageInfo pageInfo)
    {
        Record("GetPageContent", pageId + ":" + pageInfo);
        if (!_pages.TryGetValue(pageId, out Page? page))
        {
            throw new OneNoteException("GetPageContent", OneNoteHResults.PageDoesNotExist, "No page " + pageId, null);
        }

        XNamespace one = OneNoteXml.Namespace2013;
        if (page.Xml is null)
        {
            XElement empty = new(one + "Page", new XAttribute(XNamespace.Xmlns + "one", one.NamespaceName), new XAttribute("ID", pageId), new XAttribute("name", page.Name));
            return OneNoteXml.Serialize(empty);
        }

        // OneNote assigns an objectID to every outline, paragraph and image; binary data becomes a CallbackID unless asked for.
        XElement root = XElement.Parse(page.Xml);
        int n = 0;
        foreach (XElement element in root.Descendants().Where(e => e.Name.LocalName is "Outline" or "OE" or "Image").ToList())
        {
            element.SetAttributeValue("objectID", pageId.TrimEnd('}') + ":o" + (++n).ToString(CultureInfo.InvariantCulture) + "}");
            if (element.Name.LocalName == "Image" && pageInfo is PageInfo.Basic or PageInfo.Selection or PageInfo.FileType)
            {
                XElement? data = element.Element(element.Name.Namespace + "Data");
                data?.ReplaceWith(new XElement(element.Name.Namespace + "CallbackID", new XAttribute("callbackID", "{cb" + n.ToString(CultureInfo.InvariantCulture) + "}")));
            }
        }

        root.SetAttributeValue("name", page.Name);
        return OneNoteXml.Serialize(root);
    }

    public string GetHyperlinkToObject(string id, string? pageObjectId)
    {
        Record("GetHyperlinkToObject", id);
        if (_pages.TryGetValue(id, out Page? page))
        {
            return "onenote:#" + Uri.EscapeDataString(page.Name) + "&section-id=" + page.SectionId + "&page-id=" + id + "&end";
        }

        if (_sections.TryGetValue(id, out Section? section))
        {
            return "onenote:" + Uri.EscapeDataString(Path.GetFileName(section.Path)) + "#section-id=" + id + "&end";
        }

        if (_notebooks.ContainsKey(id))
        {
            return "onenote:#notebook-id=" + id + "&end";
        }

        throw new OneNoteException("GetHyperlinkToObject", OneNoteHResults.NoHyperlinkToObject, "No object " + id, null);
    }

    public void SyncHierarchy(string id) => Record("SyncHierarchy", id);

    public void NavigateTo(string id) => Record("NavigateTo", id);

    public void DeleteHierarchy(string id, bool deletePermanently)
    {
        Record("DeleteHierarchy", id + ":" + deletePermanently);
        _notebooks.Remove(id);
    }

    public void CloseNotebook(string id)
    {
        Record("CloseNotebook", id);
        _notebooks.Remove(id);
    }

    public void Dispose() => Disposed = true;

    private static string LogSafeDetail(string xml)
    {
        XElement root = XElement.Parse(xml);
        return root.Name.LocalName + ":" + ((string?)root.Attribute("ID") ?? "");
    }

    private XElement NotebookElement(XNamespace one, Notebook nb, HierarchyScope scope)
    {
        XElement element = new(one + "Notebook", new XAttribute("name", nb.Name), new XAttribute("ID", nb.Id), new XAttribute("path", nb.Path));
        if (scope is HierarchyScope.Sections or HierarchyScope.Pages)
        {
            foreach (string sectionId in nb.SectionIds)
            {
                element.Add(SectionElement(one, _sections[sectionId], scope));
            }
        }

        return element;
    }

    private XElement SectionElement(XNamespace one, Section section, HierarchyScope scope)
    {
        XElement element = new(one + "Section", new XAttribute("name", section.Name), new XAttribute("ID", section.Id), new XAttribute("path", section.Path));
        if (section.Color is not null)
        {
            element.Add(new XAttribute("color", section.Color));
        }

        if (scope == HierarchyScope.Pages)
        {
            foreach (string pageId in section.PageIds)
            {
                Page page = _pages[pageId];
                element.Add(new XElement(one + "Page", new XAttribute("ID", page.Id), new XAttribute("name", page.Name), new XAttribute("pageLevel", page.Level.ToString(CultureInfo.InvariantCulture))));
            }
        }

        return element;
    }

    private string NewId(string kind)
    {
        int n = Interlocked.Increment(ref _nextId);
        return "{" + kind + ":" + n.ToString("D4", CultureInfo.InvariantCulture) + "}{1}{B0}";
    }

    private void Record(string call, string detail)
    {
        lock (_gate)
        {
            _calls.Add((call, detail));
        }
    }

    private sealed class Notebook
    {
        public Notebook(string id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path;
        }

        public string Id { get; }

        public string Name { get; }

        public string Path { get; }

        public List<string> SectionIds { get; } = new();
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

        public string Id { get; }

        public string Name { get; set; }

        public string Path { get; }

        public string NotebookId { get; }

        public string? Color { get; set; }

        public List<string> PageIds { get; } = new();
    }

    private sealed class Page
    {
        public Page(string id, string sectionId)
        {
            Id = id;
            SectionId = sectionId;
        }

        public string Id { get; }

        public string SectionId { get; }

        public string Name { get; set; } = "";

        public int Level { get; set; } = 1;

        public NewPageStyle Style { get; set; }

        public string? Xml { get; set; }
    }
}
