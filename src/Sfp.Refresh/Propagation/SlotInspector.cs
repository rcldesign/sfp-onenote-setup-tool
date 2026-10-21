using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.OneNote;
using Sfp.OneNote.Xml;

namespace Sfp.Refresh.Propagation;

/// <summary>Something of the user's that sits in a slot rectangle.</summary>
/// <param name="ObjectId">Its OneNote object ID.</param>
/// <param name="Description">A short description for the blocked reason ("text 'my note'" or "ink").</param>
public sealed record Intruder(string ObjectId, string Description);

/// <summary>What a page holds at one slot rectangle.</summary>
/// <param name="Owned">The tool-owned outline recorded for the slot, when it still exists on the page.</param>
/// <param name="Moved">True when the owned outline exists but no longer overlaps the slot rectangle (the user moved it).</param>
/// <param name="Intruders">Outlines, ink drawings, and pictures, files or later-version content placed on the page, that the tool does not own and that overlap the rectangle.</param>
public sealed record SlotState(OutlineInfo? Owned, bool Moved, IReadOnlyList<Intruder> Intruders)
{
    /// <summary>The owned outline's plain text, or null when there is no owned outline.</summary>
    public string? OwnedText => Owned?.PlainText;

    public bool IsBlocked => Intruders.Count > 0;

    /// <summary>"a user object overlaps the slot: text 'my note'" for the blocked reason.</summary>
    public string IntruderSummary => string.Join("; ", Intruders.Select(i => i.Description));
}

/// <summary>
/// Reads the state of a slot on a page: the tool-owned outline by object ID, whether it was moved, and the user's
/// objects overlapping the rectangle (outlines, page-level ink, and pictures, attached files, media files or content from
/// a later OneNote version placed on the page outside any outline). Rectangles are sheet points; the sheet outline's origin and placed scale are applied
/// so a hidden-title or scaled notebook is read correctly.
/// </summary>
public static class SlotInspector
{
    /// <summary>Position tolerance for outlines whose size OneNote did not report.</summary>
    public const double PositionTolerancePt = 4.0;

    /// <summary>
    /// The only elements a tool outline may hold, from the official 2013 schema's Outline and OE content models: position,
    /// size, indents, meta, paragraphs of text runs, and list bullets or numbers. Anything else (a picture, ink, a file, a
    /// table, a tag, an Outlook task, a linked note, media, content from a later OneNote version as one:FutureObject, or an
    /// element the schema does not know) is the user's.
    /// </summary>
    private static readonly HashSet<string> ToolOutlineContent = new(StringComparer.Ordinal)
    {
        "Position", "Size", "Indents", "Indent", "Meta", "OEChildren", "OE", "T", "List", "Bullet", "Number",
    };

    /// <param name="page">The page as read.</param>
    /// <param name="sheetRect">The slot rectangle in sheet points.</param>
    /// <param name="ownedObjectId">The object ID the sidecar records for this slot, or null.</param>
    /// <param name="ownedIds">Every object ID the sidecar records for the page (other slots and link anchors never block).</param>
    /// <param name="sheetWidthPt">The manifest page width, to derive the placed scale from the sheet outline.</param>
    public static SlotState Inspect(PageXml page, RectPt sheetRect, string? ownedObjectId, IReadOnlyCollection<string> ownedIds, double sheetWidthPt)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(ownedIds);
        RectPt pageRect = ToPageRect(page, sheetRect, sheetWidthPt);
        OutlineInfo? owned = ownedObjectId is null
            ? null
            : page.Outlines.FirstOrDefault(o => !o.IsBackgroundImage && o.ObjectId.Length > 0 && string.Equals(o.ObjectId, ownedObjectId, StringComparison.Ordinal));
        bool moved = owned is not null && !Overlaps(owned.Rect, pageRect);

        List<Intruder> intruders = new();
        foreach (OutlineInfo outline in page.Outlines)
        {
            if (outline.IsBackgroundImage || ReferenceEquals(outline, owned) || ownedIds.Contains(outline.ObjectId) || !Overlaps(outline.Rect, pageRect))
            {
                continue;
            }

            intruders.Add(new Intruder(outline.ObjectId, Describe(outline)));
        }

        foreach (InkDrawingInfo drawing in page.InkDrawings())
        {
            if (drawing.Bounds is RectPt bounds && drawing.OutlineObjectId is null && Overlaps(bounds, pageRect))
            {
                intruders.Add(new Intruder(drawing.ObjectId, "ink"));
            }
        }

        foreach (PageObjectInfo item in page.PageObjects)
        {
            if (!ownedIds.Contains(item.ObjectId) && Overlaps(item.Rect, pageRect))
            {
                intruders.Add(new Intruder(item.ObjectId, Describe(item)));
            }
        }

        return new SlotState(owned, moved, intruders);
    }

    /// <summary>A sheet rectangle in page points: the sheet outline's origin added and its placed scale applied (1 when there is no sheet).</summary>
    public static RectPt ToPageRect(PageXml page, RectPt sheetRect, double sheetWidthPt)
    {
        ArgumentNullException.ThrowIfNull(page);
        OutlineInfo? sheet = page.SheetOutline;
        double scale = SheetScale(page, sheetWidthPt);
        double ox = sheet?.X ?? 0;
        double oy = sheet?.Y ?? 0;
        return new RectPt(ox + (sheetRect.X * scale), oy + (sheetRect.Y * scale), sheetRect.W * scale, sheetRect.H * scale);
    }

    /// <summary>The placed scale of the sheet (its outline width over the manifest page width), or 1 when unknown.</summary>
    public static double SheetScale(PageXml page, double sheetWidthPt)
    {
        ArgumentNullException.ThrowIfNull(page);
        OutlineInfo? sheet = page.SheetOutline;
        return sheet is not null && sheet.Width > 0 && sheetWidthPt > 0 ? sheet.Width / sheetWidthPt : 1.0;
    }

    /// <summary>True when the object rectangle overlaps the slot; an object without a reported size counts when its position is inside the slot (within a small tolerance).</summary>
    public static bool Overlaps(RectPt objectRect, RectPt slotPageRect)
    {
        if (objectRect.W > 0 && objectRect.H > 0)
        {
            return objectRect.Overlaps(slotPageRect);
        }

        RectPt tolerant = new(
            slotPageRect.X - PositionTolerancePt,
            slotPageRect.Y - PositionTolerancePt,
            slotPageRect.W + (2 * PositionTolerancePt),
            slotPageRect.H + (2 * PositionTolerancePt));
        return tolerant.Contains(objectRect.X, objectRect.Y);
    }

    /// <summary>
    /// True when the outline still holds text only, as the tool writes it: every element inside it is one the tool's text
    /// outlines are made of (<see cref="ToolOutlineContent"/>, in the OneNote namespace), so nothing the user put inside it
    /// (clicking at the end of a tool line puts the cursor inside its outline) is there to lose. Extra lines of typed text
    /// do not change the shape; the text comparison in Propagator.IsEdited catches them.
    /// </summary>
    public static bool HasToolShape(OutlineInfo outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        XNamespace one = outline.Element.Name.Namespace;
        return outline.Element.Descendants().All(e => e.Name.Namespace == one && ToolOutlineContent.Contains(e.Name.LocalName));
    }

    private static string Describe(OutlineInfo outline)
    {
        string text = outline.PlainText;
        if (text.Length == 0)
        {
            text = outline.InkText;
        }

        if (text.Length == 0)
        {
            return outline.InkWords.Count > 0 ? "ink" : "an empty text box";
        }

        const int max = 40;
        string shown = text.Length > max ? text[..max].TrimEnd() + LegendText.Ellipsis : text;
        return "text '" + shown.Replace('\n', ' ') + "'";
    }

    private static string Describe(PageObjectInfo item) => item.Kind switch
    {
        "Image" => "a picture",
        "InsertedFile" => "an attached file",
        "MediaFile" => "a media file",
        _ => "an object",
    };
}
