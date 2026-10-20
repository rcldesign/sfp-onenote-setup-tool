using SkiaSharp;

namespace Sfp.Render.Fonts;

/// <summary>
/// Resolves families through <see cref="SKFontManager.Default"/>: DirectWrite on Windows (where Bookman Old Style ships
/// with Office), CoreText on macOS. The Linux build of SkiaSharp used here has no fontconfig, so it finds nothing and the
/// service falls back to the bundled face.
/// </summary>
public sealed class SystemFontResolver : IFontResolver
{
    public SKTypeface? Resolve(string family, FontStyleKind style)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return SKFontManager.Default.MatchFamily(family, style.ToSkia());
    }
}
