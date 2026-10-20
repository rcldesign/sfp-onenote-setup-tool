using System.Xml.Linq;
using Serilog;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Writer;

/// <summary>
/// The production <see cref="INotebookWriter"/> over the OneNote COM wrapper, following the PRD call sequence:
/// OpenHierarchy for the notebook and each section (in plan order, so the tabs read left to right), CreateNewPage for
/// every page, UpdateHierarchy per section for names, order, levels and color, GetHyperlinkToObject for links, then
/// UpdatePageContent per page. Every COM call goes through <see cref="IOneNoteApp"/>, which owns the thread and the retry rule.
/// </summary>
public sealed class ComNotebookWriter : INotebookWriter
{
    private const string AutomaticColor = "automatic";

    private readonly IOneNoteApp _app;
    private readonly ILogger _log;
    private readonly List<string> _warnings = new();
    private PageXmlOptions _xml;
    private bool _fallbackTried;

    /// <param name="app">The run's COM session.</param>
    /// <param name="xmlOptions">Page XML settings for the run (title, scale, page dates, font family).</param>
    /// <param name="log">Run log.</param>
    public ComNotebookWriter(IOneNoteApp app, PageXmlOptions xmlOptions, ILogger log)
    {
        _app = app;
        _xml = xmlOptions;
        _log = log;
    }

    /// <summary>The XML options in use; they change once if OneNote refuses the page color or the page date.</summary>
    public PageXmlOptions XmlOptions => _xml;

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Position tolerance for <see cref="ReadAnchorIds"/>.</summary>
    public double AnchorMatchTolerancePt { get; init; } = AnchorIdMatcher.DefaultTolerancePt;

    public NotebookHandle CreateNotebook(string folder, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = Path.Combine(folder, name);
        string id = _app.OpenHierarchy(path, null, CreateFileType.Notebook);
        (string? reportedPath, string? reportedName) = ReadHierarchyNode(id);
        NotebookHandle handle = new(id, string.IsNullOrWhiteSpace(reportedPath) ? path : reportedPath, string.IsNullOrWhiteSpace(reportedName) ? name : reportedName);
        _log.Information("Notebook {Name} created at {Path} ({Id})", handle.Name, handle.Path, handle.Id);
        return handle;
    }

    public SectionHandle CreateSection(NotebookHandle nb, SectionSpec s)
    {
        ArgumentNullException.ThrowIfNull(nb);
        ArgumentNullException.ThrowIfNull(s);
        string name = NotebookNames.SanitizeSectionName(s.Name);
        if (!string.Equals(name, s.Name, StringComparison.Ordinal))
        {
            _log.Warning("Section name {Original} holds characters OneNote refuses; using {Sanitized}", s.Name, name);
        }

        string path = Path.Combine(nb.Path, name + ".one");
        string id = _app.OpenHierarchy(path, nb.Id, CreateFileType.Section);
        SectionHandle handle = new(id, name, nb) { ColorHex = s.ColorHex };
        if (!string.IsNullOrWhiteSpace(s.ColorHex))
        {
            // The section is empty at this point, so a one:Section element without pages drops nothing.
            _app.UpdateHierarchy(HierarchyXmlBuilder.SectionPages(id, name, s.ColorHex, Array.Empty<(string, string, int)>(), _xml.OneNote.SchemaNamespace));
        }

        return handle;
    }

    public PageHandle CreatePage(SectionHandle s, PageSpec p)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(p);
        NewPageStyle style = _xml.HideTitle ? NewPageStyle.BlankPageNoTitle : NewPageStyle.BlankPageWithTitle;
        string id = _app.CreateNewPage(s.Id, style);
        return new PageHandle(id, p.Key, s);
    }

    public void ApplyHierarchy(SectionHandle s, IReadOnlyList<(PageHandle Handle, PageSpec Spec)> pages)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(pages);
        List<(string PageId, string Name, int Level)> entries = pages.Select(p => (p.Handle.Id, p.Spec.Title, p.Spec.Level)).ToList();
        _app.UpdateHierarchy(HierarchyXmlBuilder.SectionPages(s.Id, s.Name, s.ColorHex, entries, _xml.OneNote.SchemaNamespace));
    }

    public string HyperlinkTo(PageHandle p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return _app.GetHyperlinkToObject(p.Id, null);
    }

    public string HyperlinkTo(SectionHandle s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return _app.GetHyperlinkToObject(s.Id, null);
    }

    public void WritePage(PageHandle p, PageSpec spec, RenderedPage art, IReadOnlyList<ResolvedAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(anchors);
        Update(options => PageXmlBuilder.Build(p.Id, spec, art, anchors, options), spec);
    }

    public void WriteContentsPage(PageHandle p, PageSpec spec, IReadOnlyList<ContentsSectionEntry> sections)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(sections);
        Update(options => ContentsXmlBuilder.Page(p.Id, spec, sections, options), spec);
    }

    public PageXml ReadPage(PageHandle p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return PageXmlParser.Parse(_app.GetPageContent(p.Id, PageInfo.Basic));
    }

    public IReadOnlyDictionary<string, string> ReadAnchorIds(PageHandle p, IReadOnlyList<ResolvedAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(anchors);
        if (anchors.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        AnchorMatchResult result = AnchorIdMatcher.Match(ReadPage(p), anchors, _xml, AnchorMatchTolerancePt);
        if (result.Unmatched.Count > 0)
        {
            _log.Warning("Page {Key}: no outline found for anchors {Anchors}; the sidecar will not record them", p.Key.Value, string.Join(", ", result.Unmatched));
        }

        return result.ObjectIds;
    }

    public void Sync(NotebookHandle nb)
    {
        ArgumentNullException.ThrowIfNull(nb);
        _app.SyncHierarchy(nb.Id);
    }

    public void NavigateTo(PageHandle p)
    {
        ArgumentNullException.ThrowIfNull(p);
        _app.NavigateTo(p.Id);
    }

    public void CloseNotebook(NotebookHandle nb)
    {
        ArgumentNullException.ThrowIfNull(nb);
        _app.CloseNotebook(nb.Id);
    }

    /// <summary>
    /// Sends a page. When OneNote reports hrInvalidXML on the first page written with a hex page color or a page date,
    /// the page is resent once with color "automatic" and no dateTime (the fallbacks the PRD names); if that is accepted,
    /// every later page is written the same way and a warning is recorded. Any other failure propagates.
    /// </summary>
    private void Update(Func<PageXmlOptions, string> build, PageSpec spec)
    {
        string xml = build(_xml);
        try
        {
            _app.UpdatePageContent(xml, DateTime.MinValue);
        }
        catch (OneNoteXmlRejectedException ex) when (!_fallbackTried && ex.HResult == OneNoteHResults.InvalidXml && CanFallBack(_xml))
        {
            _fallbackTried = true;
            PageXmlOptions fallback = _xml with { OneNote = _xml.OneNote with { PageColor = AutomaticColor }, SetPageDates = false };
            _log.Warning("OneNote rejected page {Key} ({HResult}); resending with page color automatic and no page date", spec.Key.Value, ex.HResultHex);
            _app.UpdatePageContent(build(fallback), DateTime.MinValue);
            _xml = fallback;
            _warnings.Add("OneNote refused the page color or the page date on the first page; pages were written with the automatic color and without planner dates.");
        }
    }

    private static bool CanFallBack(PageXmlOptions options) =>
        !string.Equals(options.OneNote.PageColor, AutomaticColor, StringComparison.OrdinalIgnoreCase) || options.SetPageDates;

    /// <summary>The path and name attributes of the hierarchy node OneNote reports for an ID (GetHierarchy hsSelf).</summary>
    private (string? Path, string? Name) ReadHierarchyNode(string id)
    {
        string xml;
        try
        {
            xml = _app.GetHierarchy(id, HierarchyScope.Self);
        }
        catch (OneNoteException ex)
        {
            _log.Warning("GetHierarchy for {Id} failed ({HResult}); using the requested path and name", id, ex.HResultHex);
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(xml))
        {
            return (null, null);
        }

        try
        {
            XElement? root = XDocument.Parse(xml).Root;
            return ((string?)root?.Attribute("path"), (string?)root?.Attribute("name"));
        }
        catch (System.Xml.XmlException ex)
        {
            _log.Warning("GetHierarchy for {Id} returned unreadable XML: {Message}", id, ex.Message);
            return (null, null);
        }
    }
}
