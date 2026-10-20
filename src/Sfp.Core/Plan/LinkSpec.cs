namespace Sfp.Core.Plan;

/// <summary>One link anchor: a small native text object on page <paramref name="From"/> that opens page <paramref name="To"/>.</summary>
/// <param name="From">Page carrying the anchor.</param>
/// <param name="AnchorId">
/// Names the anchor slot, "{anchorType}" or "{anchorType}:{instance}", where anchorType is a key of the manifest links block
/// (annualObjective, objectiveBack, calendarDay, calendarWeek, dailyDate, overviewDayHeader, prev, next, reflectionNext,
/// progressScorecard, scorecardWeek, calendarReview, reflectionQuarterReview, quarterReviewScorecard, nextQuarter, notesContents)
/// and instance disambiguates repeated anchors on one page, for example "calendarDay:2027-01-04", "overviewDayHeader:1", "annualObjective:3".
/// The renderer emits an <c>AnchorPlacement</c> with the same AnchorId so the writer can join the two.
/// </param>
/// <param name="To">Target page.</param>
/// <param name="Text">Visible link text, for example "4", "Week 02", "01 / 04 / 27", "Tuesday", "Objective 3".</param>
public sealed record LinkSpec(PageKey From, string AnchorId, PageKey To, string Text)
{
    /// <summary>The anchor type part of <see cref="AnchorId"/> (before the first colon).</summary>
    public string AnchorType
    {
        get
        {
            int i = AnchorId.IndexOf(':', StringComparison.Ordinal);
            return i < 0 ? AnchorId : AnchorId[..i];
        }
    }

    /// <summary>The instance part of <see cref="AnchorId"/> (after the first colon), or null.</summary>
    public string? AnchorInstance
    {
        get
        {
            int i = AnchorId.IndexOf(':', StringComparison.Ordinal);
            return i < 0 ? null : AnchorId[(i + 1)..];
        }
    }
}
