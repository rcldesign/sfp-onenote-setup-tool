using Sfp.Core;
using Sfp.Core.Options;
using Sfp.Core.Plan;

namespace Sfp.Render;

/// <summary>What a placed anchor is for. Link anchors are written at Generate; propagated slots are filled by Refresh.</summary>
public enum AnchorKind
{
    /// <summary>A hyperlink anchor matching a <see cref="LinkSpec"/> by AnchorId.</summary>
    Link,

    /// <summary>A tool-owned text slot that Refresh fills (legend, standing goal, metric name, habit legend, calendar event line).</summary>
    Propagated,
}

/// <summary>
/// Where a native OneNote object goes on a page, in PDF points relative to the sheet image's top-left corner.
/// The writer adds the title offset and the manifest's anchor padding when it builds outline positions.
/// </summary>
/// <param name="AnchorId">Matches <see cref="LinkSpec.AnchorId"/> for link anchors; for propagated slots it is the slot id
/// ("objectiveLegend", "standingGoal:1", "metricName:2", "habitLegend", "allDayBand", "timeBlockEvent:9", "overviewDay:1", "calendarEvents:2027-01-04").</param>
/// <param name="X">Left edge in points.</param>
/// <param name="Y">Top edge in points.</param>
/// <param name="W">Width in points (set explicitly so text never wraps).</param>
/// <param name="H">Height in points.</param>
/// <param name="Text">Initial text; empty for a propagated slot that has nothing yet.</param>
/// <param name="Align">Paragraph alignment inside the outline.</param>
/// <param name="SizePt">Font size in points.</param>
public sealed record AnchorPlacement(string AnchorId, float X, float Y, float W, float H, string Text, TextAlign Align, float SizePt)
{
    public AnchorKind Kind { get; init; } = AnchorKind.Link;

    /// <summary>Ink opacity for propagated text (1.0 for names and legends, 0.6 for pre-fills, 0.7 for events).</summary>
    public float Opacity { get; init; } = 1.0f;
}

/// <summary>One rendered sheet plus the native objects that go on top of it.</summary>
/// <param name="Key">Page key.</param>
/// <param name="Png">24-bit RGB PNG bytes; null-free, opaque.</param>
/// <param name="WidthPx">Pixel width (3398 at 220 DPI).</param>
/// <param name="HeightPx">Pixel height (2353 at 220 DPI).</param>
/// <param name="Anchors">Link and propagated slot placements; nothing is drawn for them in the bitmap.</param>
public sealed record RenderedPage(PageKey Key, byte[] Png, int WidthPx, int HeightPx, IReadOnlyList<AnchorPlacement> Anchors);

/// <summary>Inputs the renderer needs beyond the page and plan.</summary>
/// <param name="PdfPath">The SFP PDF to rasterize.</param>
/// <param name="Dpi">Render DPI (144, 220 or 300).</param>
/// <param name="Generate">The generate options (date format, weeks-remaining mode, links on or off, time-block window, and so on).</param>
public sealed record RenderOptions(string PdfPath, int Dpi, GenerateOptions Generate)
{
    /// <summary>Points to pixels factor.</summary>
    public float Scale => Dpi / 72f;
}

/// <summary>Renders one page of the plan to a PNG with its overlays, and reports the anchor placements left blank for native objects.</summary>
public interface IPageRenderer
{
    RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o);
}
