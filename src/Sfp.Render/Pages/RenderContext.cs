using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render.Fonts;
using Sfp.Render.Overlay;
using SkiaSharp;

namespace Sfp.Render.Pages;

/// <summary>
/// Everything one page render shares: the manifest, the fonts (cached per size for the page's scale), the plan, the
/// page, the options, the sheet being drawn on and the anchor placements collected so far. The painters in this
/// namespace draw through it so every coordinate is a manifest value in points and every placement is built the same way.
/// </summary>
internal sealed class RenderContext : IDisposable
{
    private readonly Dictionary<(double SizePt, FontStyleKind Style), SKFont> fontCache = new();
    private readonly List<AnchorPlacement> anchors = new();
    private readonly IReadOnlyList<LinkSpec> links;

    public RenderContext(Manifest manifest, IFontService fonts, IDateEngine dates, PageSpec page, NotebookPlan plan, RenderOptions options, SheetCanvas sheet, SKColor background, SKColor ink)
    {
        Manifest = manifest;
        Fonts = fonts;
        Dates = dates;
        Page = page;
        Plan = plan;
        Options = options;
        Sheet = sheet;
        Background = background;
        Ink = ink;
        links = plan.LinksFrom(page.Key).ToList();
    }

    public Manifest Manifest { get; }

    public IFontService Fonts { get; }

    public IDateEngine Dates { get; }

    public PageSpec Page { get; }

    public NotebookPlan Plan { get; }

    public RenderOptions Options { get; }

    public SheetCanvas Sheet { get; }

    public SKColor Background { get; }

    public SKColor Ink { get; }

    public GenerateOptions Generate => Options.Generate;

    /// <summary>The manifest page name of this page's art (mon, overview, calendar, scorecard ...), what appliesTo lists name.</summary>
    public string PdfPageName => Page.Token(Tokens.Extra.PdfPageName);

    /// <summary>False only for a day page outside the quarter (GenerateOptions.IncludeOutOfQuarterDays).</summary>
    public bool InQuarter => !Page.Tokens.TryGetValue(Tokens.Extra.InQuarter, out string? v) || v != "false";

    /// <summary>The links whose anchor sits on this page.</summary>
    public IReadOnlyList<LinkSpec> Links => links;

    /// <summary>The placements collected so far, in emission order.</summary>
    public IReadOnlyList<AnchorPlacement> Anchors => anchors;

    /// <summary>True when the slot's appliesTo list names this page's art.</summary>
    public bool Applies(IReadOnlyList<string> appliesTo) => appliesTo.Contains(PdfPageName, StringComparer.Ordinal);

    /// <summary>The link on this page with the given anchor id, or null.</summary>
    public LinkSpec? Link(string anchorId) => links.FirstOrDefault(l => string.Equals(l.AnchorId, anchorId, StringComparison.Ordinal));

    /// <summary>The links on this page of one anchor type.</summary>
    public IEnumerable<LinkSpec> LinksOfType(string anchorType) => links.Where(l => string.Equals(l.AnchorType, anchorType, StringComparison.Ordinal));

    /// <summary>A font at a point size for this sheet's scale, owned by the context.</summary>
    public SKFont Font(double sizePt, FontStyleKind style = FontStyleKind.Regular)
    {
        if (!fontCache.TryGetValue((sizePt, style), out SKFont? font))
        {
            font = Fonts.Font(sizePt, Sheet.Scale, style);
            fontCache[(sizePt, style)] = font;
        }

        return font;
    }

    /// <summary>Draws text in ink (optionally lightened) with its baseline at (x, baselineY) in points.</summary>
    public void Text(string text, double x, double baselineY, TextAlign align, double sizePt, double opacity = 1.0) =>
        Sheet.Text(text, x, baselineY, align, Font(sizePt), Ink, opacity);

    /// <summary>Draws text in an explicit color (the white digit inside the weeks-remaining circle).</summary>
    public void Text(string text, double x, double baselineY, TextAlign align, double sizePt, SKColor color) =>
        Sheet.Text(text, x, baselineY, align, Font(sizePt), color);

    /// <summary>
    /// Draws text in ink inside a rectangle the way a link anchor's text sits in its outline: anchored on x by the
    /// alignment (the left edge, the center or the right edge) and, unless a baseline is given, with its capitals
    /// centered vertically. Bakes an anchor's text into the sheet when the page has no link for it (links off).
    /// </summary>
    public void TextIn(RectPt rect, string text, TextAlign align, double sizePt, double? baselineY = null)
    {
        double x = align switch
        {
            TextAlign.Center => rect.CenterX,
            TextAlign.Right => rect.Right,
            _ => rect.X,
        };
        Text(text, x, baselineY ?? rect.CenterY + (CapHeight(sizePt) / 2), align, sizePt);
    }

    /// <summary>Advance width of the text in points at a point size.</summary>
    public double TextWidth(string text, double sizePt) => Sheet.TextWidth(text, Font(sizePt));

    /// <summary>Cap height in points at a point size.</summary>
    public double CapHeight(double sizePt) => Sheet.Measure(Font(sizePt)).CapHeight;

    /// <summary>Paints the background over a rectangle, removing printed art.</summary>
    public void Erase(RectPt rect) => Sheet.Erase(rect, Background);

    /// <summary>A rule in ink.</summary>
    public void Rule(double x1, double y1, double x2, double y2, double widthPt) => Sheet.Rule(x1, y1, x2, y2, widthPt, Ink);

    /// <summary>A rectangle outline in ink.</summary>
    public void RectOutline(RectPt rect, double widthPt) => Sheet.RectOutline(rect, widthPt, Ink);

    /// <summary>A filled ink circle.</summary>
    public void Circle(double cx, double cy, double diameterPt) => Sheet.Circle(cx, cy, diameterPt, Ink, filled: true);

    /// <summary>Records a link anchor placement; nothing is drawn for it.</summary>
    public void AddLink(string anchorId, RectPt rect, string text, TextAlign align, double sizePt) =>
        Add(new AnchorPlacement(anchorId, (float)rect.X, (float)rect.Y, (float)rect.W, (float)rect.H, text, align, (float)sizePt) { Kind = AnchorKind.Link });

    /// <summary>Records a propagated slot placement (empty text; Refresh fills it); nothing is drawn for it.</summary>
    public void AddPropagated(string slotId, RectPt rect, double sizePt, double opacity = 1.0, TextAlign align = TextAlign.Left) =>
        Add(new AnchorPlacement(slotId, (float)rect.X, (float)rect.Y, (float)rect.W, (float)rect.H, "", align, (float)sizePt) { Kind = AnchorKind.Propagated, Opacity = (float)opacity });

    /// <summary>Records a placement built by a painter (a widened link rectangle, for example).</summary>
    public void Add(AnchorPlacement placement)
    {
        if (anchors.Any(a => string.Equals(a.AnchorId, placement.AnchorId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Page {Page.Key} would carry anchor '{placement.AnchorId}' twice.");
        }

        anchors.Add(placement);
    }

    /// <summary>True when a placement with this anchor id was recorded.</summary>
    public bool HasAnchor(string anchorId) => anchors.Any(a => string.Equals(a.AnchorId, anchorId, StringComparison.Ordinal));

    public void Dispose()
    {
        foreach (SKFont font in fontCache.Values)
        {
            font.Dispose();
        }

        fontCache.Clear();
    }
}
