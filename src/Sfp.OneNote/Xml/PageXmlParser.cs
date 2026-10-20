using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sfp.Core.Manifests;

namespace Sfp.OneNote.Xml;

/// <summary>
/// Parses the XML GetPageContent returns into the <see cref="PageXml"/> model: page ID and title, page-level meta,
/// top-level outlines with position, size, z, text runs and ink words, and every image. The namespace is taken from
/// the document so 2010 and 2013 pages both parse. Malformed XML raises <see cref="System.Xml.XmlException"/>.
/// </summary>
public static partial class PageXmlParser
{
    public static PageXml Parse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        XDocument doc = XDocument.Parse(xml, LoadOptions.None);
        XElement root = doc.Root ?? throw new FormatException("The page XML has no root element.");
        if (root.Name.LocalName != "Page")
        {
            throw new FormatException($"Expected a one:Page root element, got '{root.Name.LocalName}'.");
        }

        XNamespace one = root.Name.Namespace;
        string pageId = (string?)root.Attribute("ID") ?? "";
        string? title = ParseTitle(root, one);
        Dictionary<string, string> meta = new(StringComparer.Ordinal);
        foreach (XElement m in root.Elements(one + "Meta"))
        {
            string? name = (string?)m.Attribute("name");
            if (!string.IsNullOrEmpty(name))
            {
                meta[name] = (string?)m.Attribute("content") ?? "";
            }
        }

        List<OutlineInfo> outlines = root.Elements(one + "Outline").Select(o => ParseOutline(o, one)).ToList();
        List<ImageInfo> images = root.Descendants(one + "Image").Select(i => ParseImage(i, one)).ToList();
        return new PageXml(pageId, title, outlines, images, meta, doc);
    }

    /// <summary>The CDATA of a one:T as plain text: line breaks kept, tags removed, entities decoded, ends trimmed.</summary>
    public static string PlainText(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        string text = LineBreak().Replace(html, "\n");
        text = Tag().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        return text.Trim();
    }

    /// <summary>Reads a one:Position plus one:Size pair under <paramref name="element"/> as a rectangle, or null when either is missing.</summary>
    public static RectPt? Bounds(XElement element, XNamespace one)
    {
        XElement? position = element.Element(one + "Position");
        XElement? size = element.Element(one + "Size");
        if (position is null || size is null)
        {
            return null;
        }

        return new RectPt(Number(position, "x"), Number(position, "y"), Number(size, "width"), Number(size, "height"));
    }

    private static string? ParseTitle(XElement root, XNamespace one)
    {
        XElement? titleElement = root.Element(one + "Title");
        if (titleElement is null)
        {
            return null;
        }

        return string.Join(" ", titleElement.Descendants(one + "T").Select(t => PlainText(t.Value)).Where(t => t.Length > 0));
    }

    private static OutlineInfo ParseOutline(XElement outline, XNamespace one)
    {
        XElement? position = outline.Element(one + "Position");
        XElement? size = outline.Element(one + "Size");
        List<TextRun> runs = outline.Descendants(one + "T")
            .Select(t => new TextRun(EnclosingOeId(t, one), t.Value, PlainText(t.Value)))
            .ToList();
        List<InkWord> ink = outline.Descendants(one + "InkWord")
            .Select(w => new InkWord(ObjectId(w), (string?)w.Attribute("recognizedText") ?? "", Bounds(w, one)))
            .ToList();
        bool isBackground = outline.Descendants(one + "Image").Any(i => Flag(i, "backgroundImage"));
        return new OutlineInfo(
            ObjectId(outline),
            position is null ? 0 : Number(position, "x"),
            position is null ? 0 : Number(position, "y"),
            position is null ? 0 : Integer(position, "z"),
            size is null ? 0 : Number(size, "width"),
            size is null ? 0 : Number(size, "height"),
            runs,
            ink,
            isBackground,
            outline);
    }

    private static ImageInfo ParseImage(XElement image, XNamespace one)
    {
        XElement? size = image.Element(one + "Size");
        return new ImageInfo(
            ObjectId(image),
            Flag(image, "backgroundImage"),
            size is null ? 0 : Number(size, "width"),
            size is null ? 0 : Number(size, "height"),
            (string?)image.Attribute("hyperlink"));
    }

    private static string EnclosingOeId(XElement element, XNamespace one) =>
        element.Ancestors(one + "OE").Select(ObjectId).FirstOrDefault() ?? "";

    private static string ObjectId(XElement element) => (string?)element.Attribute("objectID") ?? "";

    private static bool Flag(XElement element, string attribute) =>
        string.Equals((string?)element.Attribute(attribute), "true", StringComparison.OrdinalIgnoreCase);

    private static double Number(XElement element, string attribute) =>
        double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static int Integer(XElement element, string attribute) =>
        int.TryParse((string?)element.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();
}
