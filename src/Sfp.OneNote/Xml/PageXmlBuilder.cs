using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Sfp.Core;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render;

namespace Sfp.OneNote.Xml;

/// <summary>
/// Pure function from a page of the plan, its rendered sheet and its resolved anchors to the one:Page XML that
/// UpdatePageContent takes: page settings, the page key meta, the title, the locked background image in its own
/// outline, then one small outline per anchor. Nothing here touches OneNote.
/// </summary>
public static class PageXmlBuilder
{
    /// <summary>The whole page XML for <paramref name="page"/>, serialized.</summary>
    /// <param name="pageId">The ID CreateNewPage returned for this page.</param>
    /// <param name="page">The page spec (key, title, date).</param>
    /// <param name="art">The rendered sheet; its PNG goes in as the background image.</param>
    /// <param name="anchors">Link anchors and propagated slots, already resolved to hrefs.</param>
    /// <param name="options">Run options.</param>
    public static string Build(string pageId, PageSpec page, RenderedPage art, IReadOnlyList<ResolvedAnchor> anchors, PageXmlOptions options)
    {
        XElement root = Frame(pageId, page, options);
        root.Add(SheetOutline(art, options));
        foreach (ResolvedAnchor anchor in anchors)
        {
            root.Add(AnchorOutline(anchor, options));
        }

        return OneNoteXml.Serialize(root);
    }

    /// <summary>
    /// The page without content: one:Page with ID, lang and dateTime, the sfp.key meta, PageSettings and the Title.
    /// The Contents page builder adds its native outline to this.
    /// </summary>
    public static XElement Frame(string pageId, PageSpec page, PageXmlOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        XNamespace one = options.OneNote.SchemaNamespace;
        XElement root = new(
            one + "Page",
            new XAttribute(XNamespace.Xmlns + OneNoteXml.Prefix, one.NamespaceName),
            new XAttribute("ID", pageId),
            new XAttribute("lang", options.Lang));
        if (options.SetPageDates && page.Date is DateOnly date)
        {
            root.Add(new XAttribute("dateTime", PageDateTime(date, options)));
        }

        root.Add(new XElement(one + "Meta", new XAttribute("name", OneNoteXml.PageKeyMetaName), new XAttribute("content", page.Key.Value)));
        root.Add(PageSettings(one, options));
        if (!options.HideTitle)
        {
            root.Add(new XElement(one + "Title", new XElement(one + "OE", new XElement(one + "T", new XCData(page.Title)))));
        }

        return root;
    }

    /// <summary>The planner date at the manifest hour in the options' zone, as ISO 8601 with offset ("2027-01-04T06:00:00-05:00").</summary>
    public static string PageDateTime(DateOnly date, PageXmlOptions options)
    {
        DateTime local = date.ToDateTime(new TimeOnly(options.OneNote.PageDateTimeHour, 0), DateTimeKind.Unspecified);
        TimeSpan offset = options.TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>The outline holding the locked sheet image at (0, titleOffset), sized to the placed sheet.</summary>
    public static XElement SheetOutline(RenderedPage art, PageXmlOptions options)
    {
        XNamespace one = options.OneNote.SchemaNamespace;
        double w = options.PlacedWidthPt;
        double h = options.PlacedHeightPt;
        return new XElement(
            one + "Outline",
            OneNoteXml.Position(one, 0, options.TitleOffsetPt, 0),
            OneNoteXml.Size(one, w, h, true),
            new XElement(
                one + "OEChildren",
                new XElement(
                    one + "OE",
                    new XElement(
                        one + "Image",
                        new XAttribute("format", "png"),
                        new XAttribute("backgroundImage", "true"),
                        OneNoteXml.Size(one, w, h, true),
                        new XElement(one + "Data", Convert.ToBase64String(art.Png))))));
    }

    /// <summary>
    /// One anchor as its own outline above the sheet: position offset by the title and the manifest padding, explicit
    /// size so the text never wraps, paragraph alignment, and the styled link (or plain styled text for a propagated slot).
    /// </summary>
    public static XElement AnchorOutline(ResolvedAnchor anchor, PageXmlOptions options)
    {
        XNamespace one = options.OneNote.SchemaNamespace;
        AnchorPlacement p = anchor.Placement;
        double s = options.PageScale;
        double x = (p.X * s) + options.OneNote.AnchorPaddingPt.X;
        double y = (p.Y * s) + options.TitleOffsetPt + options.OneNote.AnchorPaddingPt.Y;
        return new XElement(
            one + "Outline",
            OneNoteXml.Position(one, x, y, 1),
            OneNoteXml.Size(one, p.W * s, p.H * s, true),
            new XElement(
                one + "OEChildren",
                new XElement(
                    one + "OE",
                    new XAttribute("alignment", Alignment(p.Align)),
                    new XElement(one + "T", new XCData(AnchorHtml(anchor, options))))));
    }

    /// <summary>
    /// The HTML inside the anchor's one:T: a span styled with the font family, size and color, wrapped in an a href for a
    /// link. Propagated text gets the ink blended toward the background by its opacity. Everything is HTML-escaped.
    /// </summary>
    public static string AnchorHtml(ResolvedAnchor anchor, PageXmlOptions options)
    {
        AnchorPlacement p = anchor.Placement;
        bool isLink = p.Kind == AnchorKind.Link && !string.IsNullOrEmpty(anchor.Href);
        string color = isLink ? options.InkHex : ColorHex.Blend(options.InkHex, options.BackgroundHex, p.Opacity);
        string style = string.Concat(
            "font-family:", WebUtility.HtmlEncode(options.FontFamily),
            ";font-size:", OneNoteXml.FontSize(p.SizePt * options.PageScale),
            ";color:", color,
            isLink && !options.OneNote.AnchorUnderline ? ";text-decoration:none" : "");
        string span = string.Concat("<span style='", style, "'>", WebUtility.HtmlEncode(p.Text), "</span>");
        return isLink ? string.Concat("<a href=\"", WebUtility.HtmlEncode(anchor.Href), "\">", span, "</a>") : span;
    }

    /// <summary>"left", "center" or "right" as the one:OE alignment attribute wants it.</summary>
    public static string Alignment(TextAlign align) => align switch
    {
        TextAlign.Center => "center",
        TextAlign.Right => "right",
        _ => "left",
    };

    private static XElement PageSettings(XNamespace one, PageXmlOptions options)
    {
        XElement size = new(one + "PageSize");
        if (options.PageSize == PageSizeMode.Custom)
        {
            size.Add(new XElement(
                one + "Dimensions",
                new XElement(one + "Width", new XAttribute("value", OneNoteXml.Pt(options.SheetWidthPt))),
                new XElement(one + "Height", new XAttribute("value", OneNoteXml.Pt(options.SheetHeightPt)))));
        }
        else
        {
            size.Add(new XElement(one + "Automatic"));
        }

        return new XElement(
            one + "PageSettings",
            new XAttribute("RTL", "false"),
            new XAttribute("color", options.OneNote.PageColor),
            size,
            new XElement(one + "RuleLines", new XAttribute("visible", "false")));
    }
}
