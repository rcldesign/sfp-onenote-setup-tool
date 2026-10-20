using System.Globalization;
using Sfp.Core.Plan;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// An in-memory <see cref="INotebookWriter"/>: generates IDs and onenote: hrefs, records every call in order with the
/// page key or section name it concerned, keeps what was written, and can create the notebook folder on disk so the
/// orchestrator's cancel path (delete the partial notebook) can be observed.
/// </summary>
public sealed class FakeNotebookWriter : INotebookWriter
{
    private readonly List<string> _calls = new();
    private readonly List<string> _warnings = new();
    private int _nextId;

    /// <summary>Create the notebook folder (with a small file inside) at CreateNotebook, as OneNote does.</summary>
    public bool CreateFolderOnDisk { get; set; }

    /// <summary>Given the page key, the exception WritePage should throw, or null.</summary>
    public Func<PageKey, Exception?>? WritePageFailure { get; set; }

    /// <summary>Exception CreateNotebook should throw, or null.</summary>
    public Exception? CreateNotebookFailure { get; set; }

    /// <summary>Every call in order: "CreateNotebook:name", "CreateSectionGroup:name", "CreateSection:name", "CreatePage:key", "ApplyHierarchy:section", "HyperlinkTo:key", "HyperlinkToSection:name", "WritePage:key", "ReadAnchorIds:key", "WriteContentsPage:key", "Sync", "NavigateTo:key", "CloseNotebook".</summary>
    public IReadOnlyList<string> Calls => _calls;

    public NotebookHandle? Notebook { get; private set; }

    public List<SectionHandle> Sections { get; } = new();

    public List<SectionGroupHandle> Groups { get; } = new();

    public Dictionary<PageKey, PageHandle> Pages { get; } = new();

    public Dictionary<string, IReadOnlyList<(PageHandle Handle, PageSpec Spec)>> Hierarchies { get; } = new(StringComparer.Ordinal);

    public Dictionary<PageKey, (PageSpec Spec, RenderedPage Art, IReadOnlyList<ResolvedAnchor> Anchors)> Written { get; } = new();

    public (PageHandle Page, PageSpec Spec, IReadOnlyList<ContentsSectionEntry> Sections)? Contents { get; private set; }

    public IReadOnlyList<string> Warnings => _warnings;

    public void AddWarning(string warning) => _warnings.Add(warning);

    public NotebookHandle CreateNotebook(string folder, string name)
    {
        _calls.Add("CreateNotebook:" + name);
        if (CreateNotebookFailure is not null)
        {
            throw CreateNotebookFailure;
        }

        string path = Path.Combine(folder, name);
        if (CreateFolderOnDisk)
        {
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "Open Notebook.onetoc2"), new byte[2048]);
        }

        Notebook = new NotebookHandle(NewId("nb"), path, name);
        return Notebook;
    }

    public SectionHandle CreateSection(NotebookHandle nb, SectionSpec s) => CreateSection(nb, s, null);

    public SectionHandle CreateSection(NotebookHandle nb, SectionSpec s, SectionGroupHandle? group)
    {
        _calls.Add("CreateSection:" + s.Name);
        SectionHandle handle = new(NewId("s"), s.Name, nb) { ColorHex = s.ColorHex, Group = group };
        Sections.Add(handle);
        if (CreateFolderOnDisk)
        {
            File.WriteAllBytes(Path.Combine(group?.Path ?? nb.Path, s.Name + ".one"), new byte[4096]);
        }

        return handle;
    }

    public SectionGroupHandle CreateSectionGroup(NotebookHandle nb, string name)
    {
        _calls.Add("CreateSectionGroup:" + name);
        SectionGroupHandle handle = new(NewId("sg"), name, Path.Combine(nb.Path, name));
        Groups.Add(handle);
        if (CreateFolderOnDisk)
        {
            Directory.CreateDirectory(handle.Path);
        }

        return handle;
    }

    public PageHandle CreatePage(SectionHandle s, PageSpec p)
    {
        _calls.Add("CreatePage:" + p.Key.Value);
        PageHandle handle = new(NewId("p"), p.Key, s);
        Pages[p.Key] = handle;
        return handle;
    }

    public void ApplyHierarchy(SectionHandle s, IReadOnlyList<(PageHandle Handle, PageSpec Spec)> pages)
    {
        _calls.Add("ApplyHierarchy:" + s.Name);
        Hierarchies[s.Name] = pages;
    }

    public string HyperlinkTo(PageHandle p)
    {
        _calls.Add("HyperlinkTo:" + p.Key.Value);
        return "onenote:#" + Uri.EscapeDataString(p.Key.Value) + "&section-id=" + p.Section.Id + "&page-id=" + p.Id + "&end";
    }

    public string HyperlinkTo(SectionHandle s)
    {
        _calls.Add("HyperlinkToSection:" + s.Name);
        return "onenote:" + Uri.EscapeDataString(s.Name) + ".one#section-id=" + s.Id + "&end";
    }

    public void WritePage(PageHandle p, PageSpec spec, RenderedPage art, IReadOnlyList<ResolvedAnchor> anchors)
    {
        _calls.Add("WritePage:" + p.Key.Value);
        Exception? failure = WritePageFailure?.Invoke(p.Key);
        if (failure is not null)
        {
            throw failure;
        }

        Written[p.Key] = (spec, art, anchors);
    }

    public PageXml ReadPage(PageHandle p)
    {
        _calls.Add("ReadPage:" + p.Key.Value);
        throw new NotSupportedException("The fake writer keeps no page XML.");
    }

    public IReadOnlyDictionary<string, string> ReadAnchorIds(PageHandle p, IReadOnlyList<ResolvedAnchor> anchors)
    {
        _calls.Add("ReadAnchorIds:" + p.Key.Value);
        return anchors.ToDictionary(a => a.Placement.AnchorId, a => p.Id.TrimEnd('}') + ":" + a.Placement.AnchorId + "}", StringComparer.Ordinal);
    }

    public void WriteContentsPage(PageHandle p, PageSpec spec, IReadOnlyList<ContentsSectionEntry> sections)
    {
        _calls.Add("WriteContentsPage:" + p.Key.Value);
        Contents = (p, spec, sections);
    }

    public void Sync(NotebookHandle nb) => _calls.Add("Sync");

    public void NavigateTo(PageHandle p) => _calls.Add("NavigateTo:" + p.Key.Value);

    public void CloseNotebook(NotebookHandle nb) => _calls.Add("CloseNotebook");

    private string NewId(string kind) => "{" + kind + ":" + (++_nextId).ToString("D4", CultureInfo.InvariantCulture) + "}{1}{B0}";
}
