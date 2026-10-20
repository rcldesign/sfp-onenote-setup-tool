using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;

namespace Sfp.Refresh.Tests.Support;

/// <summary>
/// A notebook state after planned changes were "written" without the anchor writer (so the propagator tests do not
/// depend on M4.5): each unblocked Create adds a tool-owned outline at the slot rectangle holding the text and records
/// its object ID in the sidecar; Update replaces the text; Remove deletes the outline and the record.
/// </summary>
public sealed class WrittenNotebook
{
    private readonly FakeNotebook notebook;
    private readonly Dictionary<PageKey, XDocument> documents = new();
    private readonly Dictionary<string, Dictionary<string, string>> anchors = new(StringComparer.Ordinal);

    private WrittenNotebook(FakeNotebook notebook)
    {
        this.notebook = notebook;
    }

    public Sidecar Sidecar => notebook.Sidecar with
    {
        Pages = notebook.Sidecar.Pages.ToDictionary(
            p => p.Key,
            p => anchors.TryGetValue(p.Key, out Dictionary<string, string>? a) ? p.Value with { Anchors = a } : p.Value,
            StringComparer.Ordinal),
    };

    public static WrittenNotebook Apply(FakeNotebook notebook, IEnumerable<PlannedChange> changes)
    {
        WrittenNotebook written = new(notebook);
        foreach (PlannedChange change in changes.Where(c => !c.Blocked))
        {
            written.Apply(change);
        }

        return written;
    }

    public PageXml? Read(PageKey key) => documents.TryGetValue(key, out XDocument? doc)
        ? PageXmlParser.Parse(doc.ToString())
        : notebook.Read(key);

    /// <summary>Moves the tool-owned outline of a slot to another sheet rectangle, as a user dragging it would.</summary>
    public void Move(PageKey key, string slotId, RectPt sheetRect)
    {
        XDocument doc = Document(key);
        XNamespace one = doc.Root!.Name.Namespace;
        string id = anchors[key.Value][slotId];
        XElement outline = doc.Root.Elements(one + "Outline").Single(o => (string?)o.Attribute("objectID") == id);
        PageXml page = PageXmlParser.Parse(doc.ToString());
        outline.Element(one + "Position")!.ReplaceWith(OneNoteXml.Position(one, sheetRect.X + (page.SheetOutline?.X ?? 0), sheetRect.Y + (page.SheetOutline?.Y ?? 0), 1));
    }

    private void Apply(PlannedChange change)
    {
        XDocument doc = Document(change.Page);
        XNamespace one = doc.Root!.Name.Namespace;
        Dictionary<string, string> pageAnchors = Anchors(change.Page);
        switch (change.Kind)
        {
            case ChangeKind.Create:
            {
                PageSpec spec = notebook.Plan.Page(change.Page);
                SlotGeometry geometry = notebook.Catalog.Slot(spec, change.SlotId) ?? throw new InvalidOperationException("No geometry for " + change.SlotId);
                PageXml page = PageXmlParser.Parse(doc.ToString());
                string id = "{w-" + change.Page.Value + "}{" + change.SlotId + "}{B0}";
                doc.Root.Add(new XElement(
                    one + "Outline",
                    new XAttribute("objectID", id),
                    OneNoteXml.Position(one, geometry.Rect.X + (page.SheetOutline?.X ?? 0), geometry.Rect.Y + (page.SheetOutline?.Y ?? 0), 1),
                    OneNoteXml.Size(one, geometry.Rect.W, geometry.Rect.H, true),
                    new XElement(one + "OEChildren", new XElement(one + "OE", new XElement(one + "T", new XCData(change.NewText.Replace("\n", "<br>", StringComparison.Ordinal)))))));
                pageAnchors[change.SlotId] = id;
                break;
            }

            case ChangeKind.Update:
            {
                XElement outline = Outline(doc, one, pageAnchors[change.SlotId]);
                outline.Descendants(one + "T").First().ReplaceNodes(new XCData(change.NewText.Replace("\n", "<br>", StringComparison.Ordinal)));
                break;
            }

            case ChangeKind.Remove:
            {
                Outline(doc, one, pageAnchors[change.SlotId]).Remove();
                pageAnchors.Remove(change.SlotId);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private static XElement Outline(XDocument doc, XNamespace one, string id) =>
        doc.Root!.Elements(one + "Outline").Single(o => (string?)o.Attribute("objectID") == id);

    private XDocument Document(PageKey key)
    {
        if (!documents.TryGetValue(key, out XDocument? doc))
        {
            doc = XDocument.Parse(notebook.Read(key).Document.ToString());
            documents[key] = doc;
        }

        return doc;
    }

    private Dictionary<string, string> Anchors(PageKey key)
    {
        if (!anchors.TryGetValue(key.Value, out Dictionary<string, string>? a))
        {
            a = new Dictionary<string, string>(notebook.Sidecar.Pages[key.Value].Anchors, StringComparer.Ordinal);
            anchors[key.Value] = a;
        }

        return a;
    }
}
