using System.Globalization;
using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.OneNote;
using Sfp.OneNote.Xml;

namespace Sfp.Refresh.Tests.Support;

/// <summary>
/// Builds page XML in the GetPageContent(piBasic) shape: the sfp.key meta, a title, the sheet outline with its
/// background image as a CallbackID, then one outline per call placed at a sheet rectangle (the sheet origin is added,
/// so manifest rectangles can be passed straight in). Typed outlines hold a one:T; inked ones hold one:InkWord elements.
/// </summary>
public sealed class PageXmlFixture
{
    private readonly XNamespace one = OneNoteXml.Namespace2013;
    private readonly XElement root;
    private readonly double sheetX;
    private readonly double sheetY;
    private int nextObject = 20;

    public PageXmlFixture(string pageId, string? sfpKey, string? title = "Setup", double sheetX = 0, double sheetY = 36, double sheetWidth = 1112, double sheetHeight = 770)
    {
        PageId = pageId;
        this.sheetX = sheetX;
        this.sheetY = sheetY;
        root = new XElement(one + "Page", new XAttribute(XNamespace.Xmlns + "one", one.NamespaceName), new XAttribute("ID", pageId), new XAttribute("lang", "en-US"));
        if (sfpKey is not null)
        {
            root.Add(new XElement(one + "Meta", new XAttribute("name", OneNoteXml.PageKeyMetaName), new XAttribute("content", sfpKey)));
        }

        root.Add(new XElement(
            one + "PageSettings",
            new XAttribute("RTL", "false"),
            new XAttribute("color", "#DED4C8"),
            new XElement(one + "PageSize", new XElement(one + "Automatic")),
            new XElement(one + "RuleLines", new XAttribute("visible", "false"))));
        if (title is not null)
        {
            root.Add(new XElement(one + "Title", new XElement(one + "OE", new XAttribute("objectID", ObjectId(10)), new XElement(one + "T", new XCData(title)))));
        }

        root.Add(new XElement(
            one + "Outline",
            new XAttribute("objectID", ObjectId(15)),
            OneNoteXml.Position(one, sheetX, sheetY, 0),
            OneNoteXml.Size(one, sheetWidth, sheetHeight, true),
            new XElement(
                one + "OEChildren",
                new XElement(
                    one + "OE",
                    new XAttribute("objectID", ObjectId(16)),
                    new XElement(
                        one + "Image",
                        new XAttribute("format", "png"),
                        new XAttribute("backgroundImage", "true"),
                        new XAttribute("objectID", ObjectId(17)),
                        OneNoteXml.Size(one, sheetWidth, sheetHeight, true),
                        new XElement(one + "CallbackID", new XAttribute("callbackID", ObjectId(18))))))));
    }

    public string PageId { get; }

    /// <summary>The object ID of the outline added last.</summary>
    public string LastObjectId { get; private set; } = "";

    /// <summary>A typed outline inside a sheet rectangle (inset by 2 pt so it sits inside the slot like typed text does).</summary>
    public PageXmlFixture Typed(RectPt sheetRect, string html, string? objectId = null, int z = 1) =>
        TypedAt(Inset(sheetRect), html, objectId, z);

    /// <summary>A typed outline at an exact sheet rectangle.</summary>
    public PageXmlFixture TypedAt(RectPt sheetRect, string html, string? objectId = null, int z = 1)
    {
        string id = objectId ?? ObjectId(nextObject += 10);
        root.Add(Outline(id, sheetRect, z, new XElement(one + "OE", new XAttribute("objectID", ObjectId(nextObject + 1)), new XElement(one + "T", new XCData(html)))));
        LastObjectId = id;
        return this;
    }

    /// <summary>An inked outline inside a sheet rectangle holding one one:InkWord per recognized word.</summary>
    public PageXmlFixture Inked(RectPt sheetRect, params string[] words)
    {
        string id = ObjectId(nextObject += 10);
        XElement oe = new(one + "OE", new XAttribute("objectID", ObjectId(nextObject + 1)));
        int w = 0;
        foreach (string word in words)
        {
            oe.Add(new XElement(one + "InkWord", new XAttribute("objectID", ObjectId(nextObject + 2 + w++)), new XAttribute("recognizedText", word)));
        }

        root.Add(Outline(id, Inset(sheetRect), 2, oe));
        LastObjectId = id;
        return this;
    }

    /// <summary>A page-level ink drawing (no recognition) at a sheet rectangle.</summary>
    public PageXmlFixture Drawing(RectPt sheetRect)
    {
        string id = ObjectId(nextObject += 10);
        root.Add(new XElement(
            one + "InkDrawing",
            new XAttribute("objectID", id),
            OneNoteXml.Position(one, sheetRect.X + sheetX, sheetRect.Y + sheetY, 3),
            OneNoteXml.Size(one, sheetRect.W, sheetRect.H, false)));
        LastObjectId = id;
        return this;
    }

    public string Xml() => OneNoteXml.Serialize(root);

    public PageXml Build() => PageXmlParser.Parse(Xml());

    private static RectPt Inset(RectPt r) => new(r.X + 2, r.Y + 2, Math.Max(1, r.W - 4), Math.Max(1, Math.Min(r.H - 4, 13)));

    private XElement Outline(string id, RectPt sheetRect, int z, XElement oe) => new(
        one + "Outline",
        new XAttribute("objectID", id),
        OneNoteXml.Position(one, sheetRect.X + sheetX, sheetRect.Y + sheetY, z),
        OneNoteXml.Size(one, sheetRect.W, sheetRect.H, false),
        new XElement(one + "OEChildren", oe));

    private string ObjectId(int n) => "{" + PageId.Trim('{', '}').Split('}')[0] + "}{" + n.ToString(CultureInfo.InvariantCulture) + "}{B0}";
}
