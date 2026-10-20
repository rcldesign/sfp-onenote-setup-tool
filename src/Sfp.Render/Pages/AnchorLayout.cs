using Sfp.Core.Manifests;
using Sfp.Core.Plan;

namespace Sfp.Render.Pages;

/// <summary>
/// M2.9: places every link of the page that sits at a fixed manifest position (objectiveBack, prev, next,
/// prevCalendar, nextCalendar, reflectionNext, progressScorecard, calendarReview, reflectionQuarterReview,
/// quarterReviewScorecard, notesContents), erasing the printed art under an anchor whose manifest entry says erase,
/// reserves the "nextQuarter" slot on the quarterly review, and finally checks that no link of the page was left
/// without a placement. The per-page painters place the anchors whose geometry depends on the page (dates, calendar
/// cells, header cells, objective blocks, scorecard columns).
/// </summary>
internal static class AnchorLayout
{
    public const string NextQuarterAnchor = "nextQuarter";

    public static void PlaceFixedAnchors(RenderContext c)
    {
        foreach (LinkSpec link in c.Links)
        {
            if (c.HasAnchor(link.AnchorId))
            {
                continue;
            }

            FixedAnchor? anchor = FixedAnchorFor(c.Manifest.Links, link.AnchorType);
            if (anchor is null)
            {
                throw new InvalidOperationException($"Page {c.Page.Key} carries link '{link.AnchorId}' but the renderer has no geometry for anchor type '{link.AnchorType}'.");
            }

            if (anchor.Erase)
            {
                c.Erase(anchor.Rect);
            }

            c.AddLink(link.AnchorId, anchor.Rect, link.Text, anchor.Align, anchor.SizePt);
        }
    }

    /// <summary>The "Next quarter" slot: reserved empty on the quarterly review so a later Generate with --previous can fill it.</summary>
    public static void ReserveNextQuarter(RenderContext c)
    {
        if (!c.Generate.Links)
        {
            return;
        }

        FixedAnchor anchor = c.Manifest.Links.NextQuarter;
        if (anchor.Erase)
        {
            c.Erase(anchor.Rect);
        }

        c.AddLink(NextQuarterAnchor, anchor.Rect, "", anchor.Align, anchor.SizePt);
    }

    /// <summary>The manifest geometry of a fixed-position anchor type, or null for the types whose position depends on the page.</summary>
    public static FixedAnchor? FixedAnchorFor(LinksSpec links, string anchorType)
    {
        ArgumentNullException.ThrowIfNull(links);
        return anchorType switch
        {
            "objectiveBack" => links.ObjectiveBack,
            "prev" => links.Prev,
            "next" => links.Next,
            "prevCalendar" => links.PrevCalendar,
            "nextCalendar" => links.NextCalendar,
            "reflectionNext" => links.ReflectionNext,
            "progressScorecard" => links.ProgressScorecard,
            "calendarReview" => links.CalendarReview,
            "reflectionQuarterReview" => links.ReflectionQuarterReview,
            "quarterReviewScorecard" => links.QuarterReviewScorecard,
            NextQuarterAnchor => links.NextQuarter,
            "notesContents" => links.NotesContents,
            _ => null,
        };
    }
}
