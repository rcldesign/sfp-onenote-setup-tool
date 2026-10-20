using SkiaSharp;

namespace Sfp.Render.Fonts;

/// <summary>The four faces of a family the renderer can ask for.</summary>
public enum FontStyleKind
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
}

public static class FontStyleKindExtensions
{
    /// <summary>The Skia style used to look the face up.</summary>
    public static SKFontStyle ToSkia(this FontStyleKind style) => style switch
    {
        FontStyleKind.Bold => SKFontStyle.Bold,
        FontStyleKind.Italic => SKFontStyle.Italic,
        FontStyleKind.BoldItalic => SKFontStyle.BoldItalic,
        _ => SKFontStyle.Normal,
    };

    /// <summary>The manifest's fallbackFiles key for the style (regular, bold, italic, boldItalic).</summary>
    public static string ManifestKey(this FontStyleKind style) => style switch
    {
        FontStyleKind.Bold => "bold",
        FontStyleKind.Italic => "italic",
        FontStyleKind.BoldItalic => "boldItalic",
        _ => "regular",
    };

    public static bool IsBold(this FontStyleKind style) => style is FontStyleKind.Bold or FontStyleKind.BoldItalic;

    public static bool IsItalic(this FontStyleKind style) => style is FontStyleKind.Italic or FontStyleKind.BoldItalic;

    /// <summary>The style a loaded typeface reports, from its weight and slant.</summary>
    public static FontStyleKind Of(SKTypeface typeface)
    {
        bool bold = typeface.FontStyle.Weight >= (int)SKFontStyleWeight.SemiBold;
        bool italic = typeface.FontStyle.Slant != SKFontStyleSlant.Upright;
        return (bold, italic) switch
        {
            (true, true) => FontStyleKind.BoldItalic,
            (true, false) => FontStyleKind.Bold,
            (false, true) => FontStyleKind.Italic,
            _ => FontStyleKind.Regular,
        };
    }
}
