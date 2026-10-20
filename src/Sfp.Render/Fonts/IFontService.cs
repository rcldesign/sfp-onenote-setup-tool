using SkiaSharp;

namespace Sfp.Render.Fonts;

/// <summary>
/// The template typeface for overlay text. Loaded once per run from the manifest's font block: the named family when it
/// is installed, otherwise the bundled fallback with a warning. Fonts are created at a pixel size from a point size and
/// the render scale, with fixed rasterization settings so output is deterministic across runs.
/// </summary>
public interface IFontService
{
    /// <summary>The family the manifest asked for.</summary>
    string RequestedFamily { get; }

    /// <summary>The family actually loaded: the requested one, or the fallback.</summary>
    string FamilyInUse { get; }

    /// <summary>True when the bundled fallback is in use.</summary>
    bool IsFallback { get; }

    /// <summary>User-facing notes recorded while loading (the fallback note, missing styles).</summary>
    IReadOnlyList<string> Warnings { get; }

    /// <summary>The loaded typeface for a style. Owned by the service; do not dispose.</summary>
    SKTypeface Typeface(FontStyleKind style = FontStyleKind.Regular);

    /// <summary>
    /// A new font at <paramref name="sizePt"/> points scaled by <paramref name="scale"/> (dpi / 72) into pixels, with
    /// antialiased edges, subpixel positioning and no hinting. The caller owns and disposes it.
    /// </summary>
    SKFont Font(double sizePt, double scale = 1.0, FontStyleKind style = FontStyleKind.Regular);

    /// <summary>Advance width in points of the widest digit at a point size, for laying out numbers.</summary>
    double DigitWidth(double sizePt, FontStyleKind style = FontStyleKind.Regular);

    /// <summary>Advance width in points of a string at a point size.</summary>
    double TextWidth(string text, double sizePt, FontStyleKind style = FontStyleKind.Regular);
}
