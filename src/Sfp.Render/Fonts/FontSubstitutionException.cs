using System.Globalization;

namespace Sfp.Render.Fonts;

/// <summary>
/// The font lookup returned a face from a different family than the one asked for. The PRD treats a silent substitution
/// (Arial standing in for Bookman Old Style) as an error, not a warning, because the baked dates would not match the page.
/// </summary>
public sealed class FontSubstitutionException : Exception
{
    public FontSubstitutionException()
    {
    }

    public FontSubstitutionException(string message)
        : base(message)
    {
    }

    public FontSubstitutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FontSubstitutionException(string requestedFamily, string actualFamily, string style)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "Font lookup for '{0}' ({1}) returned '{2}' instead. A substituted typeface is refused; install '{0}' or remove it so the bundled fallback is used.",
            requestedFamily, style, actualFamily))
    {
        RequestedFamily = requestedFamily;
        ActualFamily = actualFamily;
    }

    public string? RequestedFamily { get; }

    public string? ActualFamily { get; }
}
