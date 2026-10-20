using System.Xml.Linq;
using Sfp.Core.Manifests;

namespace Sfp.OneNote.Xml;

/// <summary>A one:InkDrawing: free strokes, page-level or inside an outline, with recognition when OneNote offers it.</summary>
/// <param name="ObjectId">Object ID.</param>
/// <param name="RecognizedText">The recognizedText attribute, or empty when absent.</param>
/// <param name="Bounds">Its own Position and Size in page points, else the enclosing outline's rectangle, else null.</param>
/// <param name="OutlineObjectId">Object ID of the enclosing outline, or null for a page-level drawing.</param>
public sealed record InkDrawingInfo(string ObjectId, string RecognizedText, RectPt? Bounds, string? OutlineObjectId);

/// <summary>
/// Ink drawings read on demand from the document kept in <see cref="PageXml"/>: pencil strokes on an iPad arrive as
/// one:InkDrawing elements (usually page-level), unlike recognized handwriting which arrives as one:InkWord inside an outline.
/// </summary>
public static class PageXmlInk
{
    /// <summary>Every one:InkDrawing on the page, in document order.</summary>
    public static IReadOnlyList<InkDrawingInfo> InkDrawings(this PageXml page)
    {
        XElement? root = page.Document.Root;
        if (root is null)
        {
            return Array.Empty<InkDrawingInfo>();
        }

        XNamespace one = root.Name.Namespace;
        return root.Descendants(one + "InkDrawing").Select(d =>
        {
            XElement? outline = d.Ancestors(one + "Outline").FirstOrDefault();
            RectPt? bounds = PageXmlParser.Bounds(d, one) ?? (outline is null ? null : PageXmlParser.Bounds(outline, one));
            return new InkDrawingInfo(
                (string?)d.Attribute("objectID") ?? "",
                (string?)d.Attribute("recognizedText") ?? "",
                bounds,
                outline is null ? null : (string?)outline.Attribute("objectID") ?? "");
        }).ToList();
    }

    /// <summary>Ink drawings whose bounds overlap the given rectangle (in sheet coordinates, like <see cref="PageXml.OutlinesIn"/>).</summary>
    public static IEnumerable<InkDrawingInfo> InkDrawingsIn(this PageXml page, RectPt sheetRect)
    {
        OutlineInfo? sheet = page.SheetOutline;
        RectPt pageRect = new(sheetRect.X + (sheet?.X ?? 0), sheetRect.Y + (sheet?.Y ?? 0), sheetRect.W, sheetRect.H);
        return page.InkDrawings().Where(d => d.Bounds is RectPt b && b.Overlaps(pageRect));
    }
}
