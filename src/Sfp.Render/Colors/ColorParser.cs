using System.Globalization;
using SkiaSharp;

namespace Sfp.Render.Colors;

/// <summary>
/// Turns the manifest's "#RRGGBB" strings into <see cref="SKColor"/> values and back. The manifest is the only place a
/// color is written down, so this is the one path from text to paint.
/// </summary>
public static class ColorParser
{
    /// <summary>Parses "#RRGGBB" (hex digits in either case). Throws <see cref="FormatException"/> for anything else.</summary>
    public static SKColor Parse(string hex)
    {
        if (!TryParse(hex, out SKColor color))
        {
            throw new FormatException($"Color '{hex}' is not in the form #RRGGBB.");
        }

        return color;
    }

    /// <summary>Parses "#RRGGBB"; false for null, a missing '#', a wrong length or a non-hex digit.</summary>
    public static bool TryParse(string? hex, out SKColor color)
    {
        color = SKColors.Empty;
        if (hex is null || hex.Length != 7 || hex[0] != '#')
        {
            return false;
        }

        // AllowHexSpecifier alone: HexNumber would also accept leading or trailing whitespace.
        const NumberStyles hexDigitsOnly = NumberStyles.AllowHexSpecifier;
        if (!byte.TryParse(hex.AsSpan(1, 2), hexDigitsOnly, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(hex.AsSpan(3, 2), hexDigitsOnly, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(hex.AsSpan(5, 2), hexDigitsOnly, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        color = new SKColor(r, g, b);
        return true;
    }

    /// <summary>Formats a color as "#RRGGBB" in upper case, ignoring alpha.</summary>
    public static string ToHex(SKColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}");

    /// <summary>The same color with its alpha set from an opacity in 0.0 to 1.0 (clamped).</summary>
    public static SKColor WithOpacity(SKColor color, double opacity)
    {
        double clamped = Math.Clamp(opacity, 0.0, 1.0);
        return color.WithAlpha((byte)Math.Round(clamped * 255.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Largest channel difference between two colors (alpha ignored), for "within N of" checks.</summary>
    public static int MaxChannelDistance(SKColor a, SKColor b) =>
        Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
}
