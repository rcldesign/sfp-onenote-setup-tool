using System.Xml.Linq;
using Sfp.Core.Manifests;

namespace Sfp.OneNote;

/// <summary>
/// A parsed one:Page document as returned by GetPageContent: outlines with positions, text runs, ink words with
/// recognized text, images and page-level meta. Positions are OneNote page points; subtract the sheet's outline origin
/// to compare with manifest rectangles. The raw <see cref="Document"/> is kept so an update can resend only the changed outline.
/// </summary>
public sealed record PageXml(
    string PageId,
    string? Title,
    IReadOnlyList<OutlineInfo> Outlines,
    IReadOnlyList<ImageInfo> Images,
    IReadOnlyDictionary<string, string> Meta,
    XDocument Document)
{
    /// <summary>The sfp.key meta value, or null when the page was not written by the tool.</summary>
    public string? SfpKey => Meta.TryGetValue("sfp.key", out string? v) ? v : null;

    /// <summary>The outline holding the locked sheet image, or null.</summary>
    public OutlineInfo? SheetOutline => Outlines.FirstOrDefault(o => o.IsBackgroundImage);

    /// <summary>Outlines whose rectangle overlaps the given rectangle (in sheet coordinates).</summary>
    public IEnumerable<OutlineInfo> OutlinesIn(RectPt sheetRect)
    {
        OutlineInfo? sheet = SheetOutline;
        double ox = sheet?.X ?? 0;
        double oy = sheet?.Y ?? 0;
        RectPt pageRect = new(sheetRect.X + ox, sheetRect.Y + oy, sheetRect.W, sheetRect.H);
        return Outlines.Where(o => !o.IsBackgroundImage && o.Rect.Overlaps(pageRect));
    }
}

/// <summary>One one:Outline.</summary>
/// <param name="ObjectId">OneNote object ID of the outline.</param>
/// <param name="X">Position x in page points.</param>
/// <param name="Y">Position y in page points.</param>
/// <param name="Z">Z order.</param>
/// <param name="Width">Size width, or 0 if unset.</param>
/// <param name="Height">Size height, or 0 if unset.</param>
/// <param name="TextRuns">All one:T runs inside, in document order.</param>
/// <param name="InkWords">All one:InkWord elements inside, in document order.</param>
/// <param name="IsBackgroundImage">True when the outline holds a one:Image with backgroundImage="true".</param>
/// <param name="Element">The raw element.</param>
public sealed record OutlineInfo(
    string ObjectId,
    double X,
    double Y,
    int Z,
    double Width,
    double Height,
    IReadOnlyList<TextRun> TextRuns,
    IReadOnlyList<InkWord> InkWords,
    bool IsBackgroundImage,
    XElement Element)
{
    public RectPt Rect => new(X, Y, Width, Height);

    /// <summary>All typed text in the outline, runs joined by a space, HTML stripped.</summary>
    public string PlainText => string.Join(" ", TextRuns.Select(r => r.PlainText).Where(t => t.Length > 0));

    /// <summary>All recognized ink text in the outline, words joined by a space.</summary>
    public string InkText => string.Join(" ", InkWords.Select(w => w.RecognizedText).Where(t => !string.IsNullOrWhiteSpace(t)));
}

/// <summary>A one:T run.</summary>
/// <param name="ObjectId">Object ID of the enclosing one:OE.</param>
/// <param name="Html">The CDATA content as returned (may hold span and a tags).</param>
/// <param name="PlainText">The content with tags removed and entities decoded.</param>
public sealed record TextRun(string ObjectId, string Html, string PlainText);

/// <summary>A one:InkWord with its recognition.</summary>
/// <param name="ObjectId">Object ID.</param>
/// <param name="RecognizedText">The recognizedText attribute, or empty when absent.</param>
/// <param name="Bounds">Bounding rectangle in page points when reported, else null.</param>
public sealed record InkWord(string ObjectId, string RecognizedText, RectPt? Bounds);

/// <summary>A one:Image.</summary>
/// <param name="ObjectId">Object ID.</param>
/// <param name="IsBackground">backgroundImage="true".</param>
/// <param name="Width">Size width.</param>
/// <param name="Height">Size height.</param>
/// <param name="Hyperlink">The hyperlink attribute, or null.</param>
public sealed record ImageInfo(string ObjectId, bool IsBackground, double Width, double Height, string? Hyperlink);
