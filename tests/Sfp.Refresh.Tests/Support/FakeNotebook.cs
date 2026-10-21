using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Render;

namespace Sfp.Refresh.Tests.Support;

/// <summary>
/// A notebook "generated" into the fake OneNote the way the M3 writer does it: notebook, sections, every page created,
/// hierarchy applied, hyperlinks fetched, then each page written with the locked sheet and its link anchors (no
/// propagated slots, which Refresh creates). The sidecar is built from the IDs and the anchors read back by position.
/// </summary>
public sealed class FakeNotebook
{
    /// <summary>A real 1 x 1 PNG; the builder only base64-encodes the bytes.</summary>
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static readonly DateTimeOffset GeneratedAt = new(2026, 12, 20, 10, 0, 0, TimeSpan.Zero);

    private FakeNotebook(FakeOneNoteApp app, Manifest manifest, NotebookPlan plan, SlotCatalog catalog, PageXmlOptions options, string notebookId, string folder, Sidecar sidecar, Dictionary<PageKey, string> pageIds)
    {
        App = app;
        Manifest = manifest;
        Plan = plan;
        Catalog = catalog;
        Options = options;
        NotebookId = notebookId;
        Folder = folder;
        Sidecar = sidecar;
        PageIds = pageIds;
    }

    public FakeOneNoteApp App { get; }

    public Manifest Manifest { get; }

    public NotebookPlan Plan { get; }

    public SlotCatalog Catalog { get; }

    public PageXmlOptions Options { get; }

    public string NotebookId { get; }

    /// <summary>The notebooks folder the notebook was created in.</summary>
    public string Folder { get; }

    /// <summary>The notebook folder path (folder plus name).</summary>
    public string NotebookPath => Path.Combine(Folder, Plan.Name);

    public Sidecar Sidecar { get; }

    public IReadOnlyDictionary<PageKey, string> PageIds { get; }

    public NotebookHandle Handle => new(NotebookId, NotebookPath, Plan.Name);

    /// <summary>
    /// Generates the Q1 2027 notebook (or another quarter) into a fresh fake app. With <paramref name="window"/> the daily
    /// pages are taken to have that Time Blocks window and the sidecar records it, as a current Generate does; without it
    /// the pages have the default window and the sidecar records none, as a file written before the window was recorded.
    /// </summary>
    public static FakeNotebook Generate(QuarterSpec? start = null, string? folder = null, FakeOneNoteApp? app = null, Manifest? manifest = null, TimeBlockWindow? window = null)
    {
        Manifest m = manifest ?? RefreshManifest.Shipped;
        QuarterSpec spec = start ?? new QuarterSpec(2027, 1);
        DateEngine dates = new();
        Quarter quarter = dates.Build(spec);
        GenerateOptions generate = new() { Start = spec, TimeBlocks = window ?? TimeBlockWindow.Default };
        NotebookPlan plan = new PlanBuilder(dates).Build(m, quarter, generate);
        SlotCatalog catalog = new(m, dates, quarter, generate.TimeBlocks);
        PageXmlOptions options = PageXmlOptions.From(m, generate, m.Font.Family, TimeZoneInfo.Utc);
        FakeOneNoteApp target = app ?? new FakeOneNoteApp();
        string notebooksFolder = folder ?? Path.Combine("C:", "Notebooks");

        string notebookId = target.OpenHierarchy(Path.Combine(notebooksFolder, plan.Name), null, CreateFileType.Notebook);
        Dictionary<string, string> sectionIds = new(StringComparer.Ordinal);
        Dictionary<PageKey, string> pageIds = new();
        Dictionary<PageKey, string> sectionOf = new();
        foreach (SectionSpec section in plan.Sections)
        {
            string sectionId = target.OpenHierarchy(Path.Combine(notebooksFolder, plan.Name, section.Name + ".one"), notebookId, CreateFileType.Section);
            sectionIds[section.Name] = sectionId;
            List<(string PageId, string Name, int Level)> entries = new();
            foreach (PageSpec page in section.Pages)
            {
                string pageId = target.CreateNewPage(sectionId, NewPageStyle.BlankPageWithTitle);
                pageIds[page.Key] = pageId;
                sectionOf[page.Key] = sectionId;
                entries.Add((pageId, page.Title, page.Level));
            }

            target.UpdateHierarchy(HierarchyXmlBuilder.SectionPages(sectionId, section.Name, section.ColorHex, entries));
        }

        Dictionary<PageKey, string> hrefs = pageIds.ToDictionary(p => p.Key, p => target.GetHyperlinkToObject(p.Value, null));
        Dictionary<string, SidecarPage> records = new(StringComparer.Ordinal);
        foreach (PageSpec page in plan.Pages)
        {
            List<ResolvedAnchor> anchors = new();
            foreach (LinkSpec link in plan.LinksFrom(page.Key))
            {
                SlotGeometry? geometry = catalog.Link(page, link.AnchorId);
                if (geometry is not null && hrefs.TryGetValue(link.To, out string? href))
                {
                    AnchorPlacement placement = geometry.ToPlacement(link.AnchorId, link.Text) with { Kind = AnchorKind.Link, Opacity = 1f };
                    anchors.Add(new ResolvedAnchor(placement, href, true));
                }
            }

            RenderedPage art = new(page.Key, Png, 3398, 2353, anchors.Select(a => a.Placement).ToList());
            target.UpdatePageContent(PageXmlBuilder.Build(pageIds[page.Key], page, art, anchors, options), DateTime.MinValue);
            PageXml written = PageXmlParser.Parse(target.GetPageContent(pageIds[page.Key], PageInfo.Basic));
            records[page.Key.Value] = new SidecarPage
            {
                PageId = pageIds[page.Key],
                SectionId = sectionOf[page.Key],
                Anchors = SidecarRecovery.MatchAnchors(page, written, catalog, plan, m.OneNote),
            };
        }

        Sidecar sidecar = new()
        {
            ToolVersion = "test",
            ManifestId = m.TemplateId,
            Quarter = spec,
            NotebookName = plan.Name,
            NotebookPath = Path.Combine(notebooksFolder, plan.Name),
            NotebookId = notebookId,
            Sections = sectionIds,
            Pages = records,
            Generated = GeneratedAt,
            TimeBlocks = window,
        };
        return new FakeNotebook(target, m, plan, catalog, options, notebookId, notebooksFolder, sidecar, pageIds);
    }

    /// <summary>The current XML of a page by key.</summary>
    public PageXml Read(PageKey key) => PageXmlParser.Parse(App.GetPageContent(PageIds[key], PageInfo.Basic));

    /// <summary>Adds a user outline (typed text) at a sheet rectangle of a page, the way a note typed in OneNote arrives.</summary>
    public void TypeUserText(PageKey key, RectPt sheetRect, string text) =>
        AddUserOutline(key, sheetRect, one => new System.Xml.Linq.XElement(one + "T", new System.Xml.Linq.XCData(text)));

    /// <summary>Adds a user outline holding recognized ink words at a sheet rectangle of a page.</summary>
    public void InkUserText(PageKey key, RectPt sheetRect, params string[] words) =>
        AddUserOutline(key, sheetRect, one => words.Select(w => new System.Xml.Linq.XElement(one + "InkWord", new System.Xml.Linq.XAttribute("recognizedText", w))).ToArray());

    /// <summary>
    /// Adds an outline exactly at a sheet rectangle (its origin and width, a fixed width when <paramref name="fixedWidth"/>)
    /// holding one one:T with the given HTML, and returns its object ID: a note typed at a slot's very origin, or an
    /// outline written the way the tool writes one.
    /// </summary>
    public string AddOutlineAt(PageKey key, RectPt sheetRect, string html, bool fixedWidth)
    {
        PageXml page = Read(key);
        System.Xml.Linq.XElement root = page.Document.Root!;
        System.Xml.Linq.XNamespace one = root.Name.Namespace;
        HashSet<string> before = page.Outlines.Select(o => o.ObjectId).ToHashSet(StringComparer.Ordinal);
        root.Add(new System.Xml.Linq.XElement(
            one + "Outline",
            OneNoteXml.Position(one, sheetRect.X + (page.SheetOutline?.X ?? 0), sheetRect.Y + (page.SheetOutline?.Y ?? 0), 2),
            OneNoteXml.Size(one, sheetRect.W, sheetRect.H, fixedWidth),
            new System.Xml.Linq.XElement(one + "OEChildren", new System.Xml.Linq.XElement(one + "OE", new System.Xml.Linq.XElement(one + "T", new System.Xml.Linq.XCData(html))))));
        App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
        return Read(key).Outlines.Single(o => !before.Contains(o.ObjectId)).ObjectId;
    }

    /// <summary>Replaces the one:T content of an outline already on a page, the way typing into it does.</summary>
    public void EditOutlineText(PageKey key, string objectId, string html)
    {
        PageXml page = Read(key);
        System.Xml.Linq.XElement root = page.Document.Root!;
        System.Xml.Linq.XNamespace one = root.Name.Namespace;
        System.Xml.Linq.XElement outline = root.Elements(one + "Outline").Single(o => (string?)o.Attribute("objectID") == objectId);
        outline.Descendants(one + "T").First().ReplaceNodes(new System.Xml.Linq.XCData(html));
        App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
    }

    /// <summary>Moves an outline already on a page to a sheet position, the way dragging it does.</summary>
    public void MoveOutline(PageKey key, string objectId, double sheetX, double sheetY)
    {
        PageXml page = Read(key);
        System.Xml.Linq.XElement root = page.Document.Root!;
        System.Xml.Linq.XNamespace one = root.Name.Namespace;
        System.Xml.Linq.XElement outline = root.Elements(one + "Outline").Single(o => (string?)o.Attribute("objectID") == objectId);
        outline.Element(one + "Position")!.ReplaceWith(OneNoteXml.Position(one, sheetX + (page.SheetOutline?.X ?? 0), sheetY + (page.SheetOutline?.Y ?? 0), 2));
        App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
    }

    /// <summary>
    /// Places a picture on a page outside any outline at a sheet rectangle (a page-level one:Image, as pasting a photo onto
    /// the page in OneNote or on the iPad stores it) and returns its object ID.
    /// </summary>
    public string AddPagePicture(PageKey key, RectPt sheetRect)
    {
        PageXml page = Read(key);
        System.Xml.Linq.XElement root = page.Document.Root!;
        System.Xml.Linq.XNamespace one = root.Name.Namespace;
        HashSet<string> before = page.PageObjects.Select(o => o.ObjectId).ToHashSet(StringComparer.Ordinal);
        root.Add(new System.Xml.Linq.XElement(
            one + "Image",
            new System.Xml.Linq.XAttribute("format", "png"),
            OneNoteXml.Position(one, sheetRect.X + (page.SheetOutline?.X ?? 0), sheetRect.Y + (page.SheetOutline?.Y ?? 0), 3),
            OneNoteXml.Size(one, sheetRect.W, sheetRect.H, true),
            new System.Xml.Linq.XElement(one + "Data", Convert.ToBase64String(Png))));
        App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
        return Read(key).PageObjects.Single(o => !before.Contains(o.ObjectId)).ObjectId;
    }

    /// <summary>
    /// Adds a paragraph (one:OE) holding <paramref name="content"/> to an outline already on a page, the way clicking at
    /// the end of a line and pasting a picture, writing with the pen or attaching a file puts it inside that outline.
    /// </summary>
    public void AddInsideOutline(PageKey key, string objectId, Func<System.Xml.Linq.XNamespace, object> content)
    {
        PageXml page = Read(key);
        System.Xml.Linq.XElement root = page.Document.Root!;
        System.Xml.Linq.XNamespace one = root.Name.Namespace;
        System.Xml.Linq.XElement outline = root.Elements(one + "Outline").Single(o => (string?)o.Attribute("objectID") == objectId);
        outline.Element(one + "OEChildren")!.Add(new System.Xml.Linq.XElement(one + "OE", content(one)));
        App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
    }

    private void AddUserOutline(PageKey key, RectPt sheetRect, Func<System.Xml.Linq.XNamespace, object> paragraphContent)
    {
        PageXml page = Read(key);
        System.Xml.Linq.XElement root = page.Document.Root!;
        System.Xml.Linq.XNamespace one = root.Name.Namespace;
        double ox = page.SheetOutline?.X ?? 0;
        double oy = page.SheetOutline?.Y ?? 0;
        root.Add(new System.Xml.Linq.XElement(
            one + "Outline",
            OneNoteXml.Position(one, sheetRect.X + ox + 2, sheetRect.Y + oy + 2, 2),
            OneNoteXml.Size(one, sheetRect.W - 4, 13, false),
            new System.Xml.Linq.XElement(one + "OEChildren", new System.Xml.Linq.XElement(one + "OE", paragraphContent(one)))));
        App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
    }
}
