using System.Text.Json;
using Sfp.Core;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;

namespace Sfp.Render;

/// <summary>One anchor placement as pages.json lists it.</summary>
public sealed record RenderIndexAnchor(string AnchorId, AnchorKind Kind, float X, float Y, float W, float H, string Text, TextAlign Align, float SizePt, float Opacity)
{
    public static RenderIndexAnchor From(AnchorPlacement p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new RenderIndexAnchor(p.AnchorId, p.Kind, p.X, p.Y, p.W, p.H, p.Text, p.Align, p.SizePt, p.Opacity);
    }

    public AnchorPlacement ToPlacement() => new(AnchorId, X, Y, W, H, Text, Align, SizePt) { Kind = Kind, Opacity = Opacity };
}

/// <summary>One page as pages.json lists it: identity, where its art came from, the PNG file (null for a page without an image) and its anchors.</summary>
public sealed record RenderIndexPage(
    string Key,
    string Title,
    string Section,
    int Level,
    int? PdfPage,
    string? Png,
    int Width,
    int Height,
    IReadOnlyList<RenderIndexAnchor> Anchors)
{
    public static RenderIndexPage From(PageSpec page, string section, RenderedPage art, string? pngFile)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(art);
        return new RenderIndexPage(page.Key.Value, page.Title, section, page.Level, page.PdfPage, pngFile, art.WidthPx, art.HeightPx, art.Anchors.Select(RenderIndexAnchor.From).ToList());
    }
}

/// <summary>
/// The pages.json index that render-only writes beside the PNGs: the notebook name and quarter, the DPI, and one
/// entry per page in notebook order. Serialized with the tool's shared JSON conventions (camelCase keys, enums as
/// camelCase strings, indented).
/// </summary>
public sealed record RenderIndex(string Name, string Quarter, int Dpi, IReadOnlyList<RenderIndexPage> Pages)
{
    public string Serialize() => JsonSerializer.Serialize(this, ManifestLoader.JsonOptions);

    public static RenderIndex Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<RenderIndex>(json, ManifestLoader.JsonOptions)
            ?? throw new InvalidDataException("pages.json is empty.");
    }
}
