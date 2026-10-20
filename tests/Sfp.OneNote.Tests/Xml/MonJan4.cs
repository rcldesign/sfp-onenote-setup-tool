using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Tests.Xml;

/// <summary>
/// The reference page of the XML tests: Monday January 4, 2027 (week 2 of Q1 2027, PDF page 26) with its link anchors
/// (date, previous, next) and two propagated slots (objective legend, first standing goal). Every rectangle comes from
/// the manifest; the hrefs are the onenote: URL shape GetHyperlinkToObject returns.
/// </summary>
internal static class MonJan4
{
    public const string PageId = "{5D9E1B2C-3A4F-4C6D-8E7F-0A1B2C3D4E5F}{1}{E19512345678901234567890123456789012345678}";
    public const string OverviewHref = "onenote:#Weekly%20Overview&section-id={7A0B1C2D-3E4F-4A5B-8C6D-9E0F1A2B3C4D}{1}{B0}&page-id={8B1C2D3E-4F5A-4B6C-9D7E-0F1A2B3C4D5E}{1}{B0}&end";
    public const string PrevHref = "onenote:#Sun%20Jan%203&section-id={6A9B0C1D-2E3F-4A4B-8C5D-9E0F1A2B3C4C}{1}{B0}&page-id={9C2D3E4F-5A6B-4C7D-8E8F-1A2B3C4D5E6F}{1}{B0}&end";
    public const string NextHref = "onenote:#Tue%20Jan%205&section-id={7A0B1C2D-3E4F-4A5B-8C6D-9E0F1A2B3C4D}{1}{B0}&page-id={0D3E4F5A-6B7C-4D8E-9F9A-2B3C4D5E6F7A}{1}{B0}&end";

    public static readonly DateOnly Date = new(2027, 1, 4);

    /// <summary>A fixed UTC-5 zone so the dateTime offset in snapshots never depends on the machine.</summary>
    public static readonly TimeZoneInfo FixedZone = TimeZoneInfo.CreateCustomTimeZone("Test UTC-5", TimeSpan.FromHours(-5), "Test UTC-5", "Test UTC-5");

    /// <summary>A real 1 x 1 PNG; the builder only base64-encodes the bytes.</summary>
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static PageSpec Page(string title = "Mon Jan 4") => new(
        PageKey.Day(Date),
        PageType.Day,
        title,
        2,
        26,
        Date,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ddd"] = "Mon",
            ["Mon"] = "Jan",
            ["D"] = "4",
            ["NN"] = "02",
            ["YYYY"] = "2027",
            ["Qn"] = "Q1",
            ["week"] = "2",
            ["weekCount"] = "14",
            ["dayOfQuarter"] = "4",
            ["quarterLength"] = "90",
        });

    public static GenerateOptions Generate() => new() { Start = new QuarterSpec(2027, 1) };

    public static PageXmlOptions Options(Manifest m, GenerateOptions? generate = null) =>
        PageXmlOptions.From(m, generate ?? Generate(), m.Font.Family, FixedZone);

    public static AnchorPlacement Placement(string anchorId, RectPt rect, string text, TextAlign align, double sizePt, AnchorKind kind = AnchorKind.Link, double opacity = 1.0) =>
        new(anchorId, (float)rect.X, (float)rect.Y, (float)rect.W, (float)rect.H, text, align, (float)sizePt) { Kind = kind, Opacity = (float)opacity };

    public static ResolvedAnchor DateAnchor(Manifest m) =>
        new(Placement("dailyDate", m.Links.DailyDate.Rect, "01 / 04 / 27", m.Links.DailyDate.Align, m.Links.DailyDate.SizePt), OverviewHref, true);

    public static ResolvedAnchor PrevAnchor(Manifest m) =>
        new(Placement("prev", m.Links.Prev.Rect, "‹ Sun Jan 3", m.Links.Prev.Align, m.Links.Prev.SizePt), PrevHref, true);

    public static ResolvedAnchor NextAnchor(Manifest m) =>
        new(Placement("next", m.Links.Next.Rect, "Tue Jan 5 ›", m.Links.Next.Align, m.Links.Next.SizePt), NextHref, true);

    public static ResolvedAnchor LegendAnchor(Manifest m) =>
        new(Placement("objectiveLegend", m.Slots.ObjectiveLegend.Rect, "O1 Ship the connector  O2 Hire two PMs", TextAlign.Left, m.Slots.ObjectiveLegend.SizePt, AnchorKind.Propagated), "", true);

    public static ResolvedAnchor StandingGoalAnchor(Manifest m) =>
        new(Placement("standingGoal:1", m.Slots.StandingGoals.Rows[0], "Walk 30 min", TextAlign.Left, m.Slots.StandingGoals.SizePt, AnchorKind.Propagated, m.Colors.PrefillInkOpacity), "", true);

    public static IReadOnlyList<ResolvedAnchor> Anchors(Manifest m) =>
        new[] { DateAnchor(m), PrevAnchor(m), NextAnchor(m), LegendAnchor(m), StandingGoalAnchor(m) };

    public static RenderedPage Art(IReadOnlyList<ResolvedAnchor> anchors) =>
        new(PageKey.Day(Date), Png, 3398, 2353, anchors.Select(a => a.Placement).ToList());

    /// <summary>The whole Mon Jan 4 page XML with the shipped manifest and default options.</summary>
    public static string Build(Manifest m, PageXmlOptions? options = null, string title = "Mon Jan 4")
    {
        IReadOnlyList<ResolvedAnchor> anchors = Anchors(m);
        return PageXmlBuilder.Build(PageId, Page(title), Art(anchors), anchors, options ?? Options(m));
    }
}
