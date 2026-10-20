using SkiaSharp;

namespace Sfp.Render.Fonts;

/// <summary>
/// Finds an installed typeface by family name. The production implementation asks the operating system's font manager;
/// tests inject a directory scan or a stub. A resolver may return a face from another family (that is what a system
/// font manager does when it substitutes); <see cref="FontService"/> checks the family name and refuses substitutions.
/// </summary>
public interface IFontResolver
{
    /// <summary>The typeface for the family and style, or null when nothing is installed under that family name.</summary>
    SKTypeface? Resolve(string family, FontStyleKind style);
}
