using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.OneNote;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;

namespace Sfp.Refresh.Orchestration;

/// <summary>
/// The marks every outline the tool writes carries and a note the user types does not: a width the writer fixed
/// (one:Size isSetByUser) equal to the slot's width, a first one:T styled with the manifest typeface (or its bundled
/// fallback) in the tool's color for the slot's opacity, and for a link an a href to an onenote: URL. The sidecar
/// recovery adopts an outline as tool-owned only when it carries all of them, so a note the user typed at a slot's origin
/// is never taken for the tool's and overwritten. The style is looked for on the spans inside the one:T, and on the
/// one:T and its one:OE, in case OneNote moves a whole-run style there; a link whose span color OneNote dropped (the PRD
/// expects it may) still counts, a link in another color does not.
/// </summary>
public static partial class AnchorSignature
{
    /// <summary>How far (points) the outline's width may differ from the slot's placed width.</summary>
    public const double WidthTolerancePt = 4.0;

    /// <summary>True when the outline carries every mark of an outline the tool wrote for a slot with this geometry.</summary>
    /// <param name="outline">An outline as read from the page.</param>
    /// <param name="geometry">The slot or link anchor the outline would have been written for.</param>
    /// <param name="isLink">True for a link anchor (an a href to an onenote: URL is required, the color is the ink).</param>
    /// <param name="scale">The placed scale of the sheet (its outline width over the manifest page width).</param>
    /// <param name="manifest">The typeface, its fallback and the colors.</param>
    public static bool Matches(OutlineInfo outline, SlotGeometry geometry, bool isLink, double scale, Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(manifest);
        XNamespace one = outline.Element.Name.Namespace;
        XElement? size = outline.Element.Element(one + "Size");
        if (size is null || !string.Equals((string?)size.Attribute("isSetByUser"), "true", StringComparison.OrdinalIgnoreCase)
            || Math.Abs(outline.Width - (geometry.Rect.W * scale)) > WidthTolerancePt)
        {
            return false;
        }

        XElement? t = outline.Element.Descendants(one + "T").FirstOrDefault();
        if (t is null)
        {
            return false;
        }

        string html = t.Value;
        List<IReadOnlyDictionary<string, string>> styles = new();
        AddStyle(styles, (string?)t.Parent?.Attribute("style"));
        AddStyle(styles, (string?)t.Attribute("style"));
        foreach (Match match in StyleAttribute().Matches(html))
        {
            AddStyle(styles, WebUtility.HtmlDecode(match.Groups["v"].Value));
        }

        if (!styles.Any(s => s.TryGetValue("font-family", out string? family) && IsToolFamily(family, manifest.Font)))
        {
            return false;
        }

        string expected = ExpectedColor(geometry, isLink, manifest.Colors);
        List<string> colors = styles.Where(s => s.ContainsKey("color")).Select(s => NormalizeColor(s["color"])).ToList();
        bool colorMatches = colors.Any(c => string.Equals(c, expected, StringComparison.OrdinalIgnoreCase));
        if (!isLink)
        {
            return colorMatches;
        }

        return (colorMatches || colors.Count == 0) && OneNoteHref().IsMatch(html);
    }

    /// <summary>The color the tool writes a slot's text in: the ink for a link, else the ink blended toward the page background by the slot's opacity.</summary>
    public static string ExpectedColor(SlotGeometry geometry, bool isLink, ColorSpec colors)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(colors);
        return isLink ? colors.Ink : ColorHex.Blend(colors.Ink, colors.Background, geometry.Opacity);
    }

    private static bool IsToolFamily(string value, FontSpec font) =>
        value.Split(',').Select(f => f.Trim().Trim('"', '\'').Trim())
            .Any(f => string.Equals(f, font.Family, StringComparison.OrdinalIgnoreCase) || string.Equals(f, font.Fallback, StringComparison.OrdinalIgnoreCase));

    private static void AddStyle(List<IReadOnlyDictionary<string, string>> styles, string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return;
        }

        Dictionary<string, string> declarations = new(StringComparer.OrdinalIgnoreCase);
        foreach (string declaration in style.Split(';'))
        {
            int colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                declarations[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
            }
        }

        styles.Add(declarations);
    }

    /// <summary>"#rrggbb" and "rgb(r, g, b)" as "#RRGGBB"; anything else as given.</summary>
    private static string NormalizeColor(string value)
    {
        string trimmed = value.Trim().Trim('"', '\'');
        Match rgb = Rgb().Match(trimmed);
        if (rgb.Success)
        {
            return ColorHex.Format(Channel(rgb, "r"), Channel(rgb, "g"), Channel(rgb, "b"));
        }

        return trimmed.ToUpperInvariant();
    }

    private static int Channel(Match rgb, string name) =>
        Math.Clamp(int.Parse(rgb.Groups[name].Value, NumberStyles.Integer, CultureInfo.InvariantCulture), 0, 255);

    [GeneratedRegex("""\bstyle\s*=\s*(?:"(?<v>[^"]*)"|'(?<v>[^']*)')""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("""<a\b[^>]*\bhref\s*=\s*["']\s*onenote:""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OneNoteHref();

    [GeneratedRegex(@"^rgb\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Rgb();
}
