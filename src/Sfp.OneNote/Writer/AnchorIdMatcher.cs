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
/// anchor's object ID. The 2013 schema allows one:Meta only at page level, so there is no per-outline marker: outlines
/// are matched by position (the builder's outline position, within a tolerance), with the outline text as a tie breaker
/// and as the fallback when OneNote moved the outline further than the tolerance.
/// </summary>
public static class AnchorIdMatcher
{
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
            OutlineInfo? match = ByPosition(candidates, expected, anchor.Placement.Text, tolerancePt) ?? ByText(candidates, anchor.Placement.Text);
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
