using System.Globalization;

namespace Sfp.OneNote.Xml;

/// <summary>#RRGGBB parsing and the opacity blend used for propagated text.</summary>
public static class ColorHex
{
    /// <summary>Parses "#RRGGBB" (case-insensitive) into channels.</summary>
    public static (int R, int G, int B) Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        string h = hex.Trim();
        if (h.Length != 7 || h[0] != '#')
        {
            throw new FormatException($"Expected a color as #RRGGBB, got '{hex}'.");
        }

        return (Channel(h, 1), Channel(h, 3), Channel(h, 5));
    }

    /// <summary>Formats channels as "#RRGGBB" in upper case.</summary>
    public static string Format(int r, int g, int b) =>
        string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");

    /// <summary>
    /// <paramref name="ink"/> drawn at <paramref name="opacity"/> over <paramref name="background"/>, as the fixed color
    /// OneNote should show: each channel is ink * opacity + background * (1 - opacity), rounded half away from zero.
    /// Opacity 1 returns the ink unchanged; 0 returns the background.
    /// </summary>
    public static string Blend(string ink, string background, double opacity)
    {
        double a = Math.Clamp(opacity, 0, 1);
        (int ir, int ig, int ib) = Parse(ink);
        (int br, int bg, int bb) = Parse(background);
        return Format(Mix(ir, br, a), Mix(ig, bg, a), Mix(ib, bb, a));
    }

    private static int Mix(int ink, int background, double a) =>
        (int)Math.Round((ink * a) + (background * (1 - a)), MidpointRounding.AwayFromZero);

    private static int Channel(string hex, int at) =>
        int.TryParse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)
            ? v
            : throw new FormatException($"'{hex}' is not a hex color.");
}
