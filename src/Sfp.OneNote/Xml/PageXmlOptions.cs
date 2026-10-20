using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.OneNote.Xml;

/// <summary>
/// Everything the page XML builders need beyond the page itself: the manifest's OneNote block, the generate switches
/// that change the XML (title, page size, scale, page dates), the font family actually in use and the two colors.
/// Build one per run with <see cref="From"/>. Pure data; the time zone is passed in so no builder reads the system.
/// </summary>
public sealed record PageXmlOptions
{
    public required OneNoteSpec OneNote { get; init; }

    /// <summary>Unscaled sheet width in points (the manifest page width, 1112).</summary>
    public required double SheetWidthPt { get; init; }

    /// <summary>Unscaled sheet height in points (the manifest page height, 770).</summary>
    public required double SheetHeightPt { get; init; }

    /// <summary>The typeface used for anchors and propagated text: Bookman Old Style, or the fallback when it is missing.</summary>
    public required string FontFamily { get; init; }

    /// <summary>Ink color as #RRGGBB.</summary>
    public required string InkHex { get; init; }

    /// <summary>Page background color as #RRGGBB (used for the opacity blend of propagated text).</summary>
    public required string BackgroundHex { get; init; }

    /// <summary>Create pages without a title area and place the sheet at y 0.</summary>
    public bool HideTitle { get; init; }

    public PageSizeMode PageSize { get; init; } = PageSizeMode.Automatic;

    /// <summary>Placed size factor for the sheet; anchors scale with it.</summary>
    public double PageScale { get; init; } = 1.0;

    /// <summary>Emit one:Page dateTime for pages that carry a planner date.</summary>
    public bool SetPageDates { get; init; } = true;

    /// <summary>The zone whose offset goes into dateTime. The CLI passes the local zone; tests pass a fixed one.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>one:Page lang attribute.</summary>
    public string Lang { get; init; } = "en-US";

    /// <summary>The y at which the sheet outline sits: the manifest title offset, or 0 when the title is hidden.</summary>
    public double TitleOffsetPt => HideTitle ? 0 : OneNote.TitleOffsetPt;

    /// <summary>Sheet width as placed, after scaling.</summary>
    public double PlacedWidthPt => SheetWidthPt * PageScale;

    /// <summary>Sheet height as placed, after scaling.</summary>
    public double PlacedHeightPt => SheetHeightPt * PageScale;

    /// <summary>Builds the options for a run from the manifest and the generate options.</summary>
    public static PageXmlOptions From(Manifest manifest, GenerateOptions options, string fontFamily, TimeZoneInfo timeZone) => new()
    {
        OneNote = manifest.OneNote,
        SheetWidthPt = manifest.PageWidthPt,
        SheetHeightPt = manifest.PageHeightPt,
        FontFamily = fontFamily,
        InkHex = manifest.Colors.Ink,
        BackgroundHex = manifest.Colors.Background,
        HideTitle = options.HideTitle || manifest.OneNote.HideTitle,
        PageSize = options.PageSize,
        PageScale = options.PageScale,
        SetPageDates = options.SetPageDates,
        TimeZone = timeZone,
    };
}
