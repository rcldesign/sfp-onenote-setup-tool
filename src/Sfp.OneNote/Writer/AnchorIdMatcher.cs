using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Writer;

/// <summary>The outline object IDs found for a page's anchors, and the anchors that could not be matched.</summary>
/// <param name="ObjectIds">AnchorId to the OneNote objectID of its outline.</param>
/// <param name="Unmatched">AnchorIds with no outline at the expected position or with the expected text.</param>
public sealed record AnchorMatchResult(IReadOnlyDictionary<string, string> ObjectIds, IReadOnlyList<string> Unmatched);

/// <summary>
/// Joins the anchors a page was written with to the outlines OneNote reports back, so the sidecar can record each
/// anchor's object ID. An outline carrying the anchor's sfp.anchor meta (the builder writes one on every anchor outline;
/// the 2013 schema allows Meta on page objects) is that anchor's, wherever it sits. Outlines without the marker (a
/// notebook generated before it existed) are matched by position (the builder's outline position, within a tolerance),
/// with the outline text as a tie breaker and as the fallback when OneNote moved the outline further than the tolerance.
/// </summary>
public static class AnchorIdMatcher
{
    /// <summary>The anchor ID an outline's sfp.anchor meta names, or null when it carries none.</summary>
    public static string? MarkedAnchorId(OutlineInfo outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        XNamespace one = outline.Element.Name.Namespace;
        return outline.Element.Elements(one + "Meta")
            .Where(m => string.Equals((string?)m.Attribute("name"), OneNoteXml.AnchorMetaName, StringComparison.Ordinal))
            .Select(m => (string?)m.Attribute("content"))
            .FirstOrDefault(content => !string.IsNullOrEmpty(content));
    }

    /// <summary>How far (in points, each axis) an outline may sit from where it was written and still count as the same object.</summary>
    public const double DefaultTolerancePt = 3.0;

    public static AnchorMatchResult Match(PageXml page, IReadOnlyList<ResolvedAnchor> anchors, PageXmlOptions options, double tolerancePt = DefaultTolerancePt)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(options);
        XNamespace one = options.OneNote.SchemaNamespace;

        List<OutlineInfo> candidates = page.Outlines.Where(o => !o.IsBackgroundImage && o.ObjectId.Length > 0).ToList();
        Dictionary<string, string> ids = new(StringComparer.Ordinal);
        List<string> unmatched = new();
        foreach (ResolvedAnchor anchor in anchors)
        {
            RectPt expected = PageXmlParser.Bounds(PageXmlBuilder.AnchorOutline(anchor, options), one)
                ?? throw new InvalidOperationException("The anchor outline builder produced no position.");
            OutlineInfo? match = ByMarker(candidates, anchor.Placement.AnchorId, expected);
            if (match is null)
            {
                // The fallback for outlines without the marker; an outline another anchor's marker claims is never taken.
                List<OutlineInfo> unmarked = candidates.Where(o => MarkedAnchorId(o) is null).ToList();
                match = ByPosition(unmarked, expected, anchor.Placement.Text, tolerancePt) ?? ByText(unmarked, anchor.Placement.Text);
            }
            if (match is null)
            {
                unmatched.Add(anchor.Placement.AnchorId);
                continue;
            }

            candidates.Remove(match);
            ids[anchor.Placement.AnchorId] = match.ObjectId;
        }

        return new AnchorMatchResult(ids, unmatched);
    }

    /// <summary>The outline carrying this anchor's marker; of several (a copied outline keeps the marker), the nearest to where the anchor was written.</summary>
    private static OutlineInfo? ByMarker(List<OutlineInfo> candidates, string anchorId, RectPt expected) =>
        candidates
            .Where(o => string.Equals(MarkedAnchorId(o), anchorId, StringComparison.Ordinal))
            .OrderBy(o => Math.Max(Math.Abs(o.X - expected.X), Math.Abs(o.Y - expected.Y)))
            .FirstOrDefault();

    private static OutlineInfo? ByPosition(List<OutlineInfo> candidates, RectPt expected, string text, double tolerancePt)
    {
        OutlineInfo? best = null;
        double bestDistance = double.MaxValue;
        bool bestTextMatches = false;
        foreach (OutlineInfo outline in candidates)
        {
            double distance = Math.Max(Math.Abs(outline.X - expected.X), Math.Abs(outline.Y - expected.Y));
            if (distance > tolerancePt)
            {
                continue;
            }

            bool textMatches = TextMatches(outline, text);
            if (best is null || (textMatches && !bestTextMatches) || (textMatches == bestTextMatches && distance < bestDistance))
            {
                best = outline;
                bestDistance = distance;
                bestTextMatches = textMatches;
            }
        }

        return best;
    }

    private static OutlineInfo? ByText(List<OutlineInfo> candidates, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        List<OutlineInfo> same = candidates.Where(o => TextMatches(o, text)).ToList();
        return same.Count == 1 ? same[0] : null;
    }

    private static bool TextMatches(OutlineInfo outline, string text) =>
        string.Equals(outline.PlainText, text.Trim(), StringComparison.Ordinal);
}
