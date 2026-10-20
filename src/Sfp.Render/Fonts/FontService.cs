using System.Globalization;
using Sfp.Core.Manifests;
using SkiaSharp;

namespace Sfp.Render.Fonts;

/// <summary>
/// Production <see cref="IFontService"/>. <see cref="Load(FontSpec, string, IReadOnlyList{string}?)"/> asks a resolver
/// for the manifest family; a face from another family is refused with <see cref="FontSubstitutionException"/>; no face
/// at all loads the bundled fallback files named in the manifest and records a warning that the glyphs will differ.
/// </summary>
public sealed class FontService : IFontService, IDisposable
{
    private static readonly FontStyleKind[] AllStyles = [FontStyleKind.Regular, FontStyleKind.Bold, FontStyleKind.Italic, FontStyleKind.BoldItalic];

    private readonly Dictionary<FontStyleKind, SKTypeface> faces;
    private readonly List<SKTypeface> owned;
    private readonly List<string> warnings;
    private bool disposed;

    private FontService(string requestedFamily, string familyInUse, bool isFallback, Dictionary<FontStyleKind, SKTypeface> faces, List<SKTypeface> owned, List<string> warnings)
    {
        RequestedFamily = requestedFamily;
        FamilyInUse = familyInUse;
        IsFallback = isFallback;
        this.faces = faces;
        this.owned = owned;
        this.warnings = warnings;
    }

    public string RequestedFamily { get; }

    public string FamilyInUse { get; }

    public bool IsFallback { get; }

    public IReadOnlyList<string> Warnings => warnings;

    /// <summary>
    /// Loads the manifest font. With <paramref name="systemFontDirectories"/> null the operating system's font manager is
    /// consulted; with a list, only font files in those folders count (tests use this to simulate a machine without Bookman).
    /// </summary>
    /// <param name="font">The manifest's font block.</param>
    /// <param name="templatesDir">The templates folder that the fallback file paths are relative to.</param>
    /// <param name="systemFontDirectories">Folders to scan instead of the system font manager, or null.</param>
    public static FontService Load(FontSpec font, string templatesDir, IReadOnlyList<string>? systemFontDirectories = null)
    {
        IFontResolver resolver = systemFontDirectories is null
            ? new SystemFontResolver()
            : new DirectoryFontResolver(systemFontDirectories);
        return Load(font, templatesDir, resolver);
    }

    /// <summary>
    /// Loads the manifest font through the given resolver (a stub in tests). Typefaces the resolver returns become the
    /// service's to dispose.
    /// </summary>
    public static FontService Load(FontSpec font, string templatesDir, IFontResolver resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesDir);
        string family = font.Family;
        List<string> warnings = [];
        List<SKTypeface> owned = [];

        SKTypeface? regular = resolver.Resolve(family, FontStyleKind.Regular);
        if (regular is null)
        {
            return LoadFallback(font, templatesDir, warnings);
        }

        Dictionary<FontStyleKind, SKTypeface> faces = new();
        try
        {
            owned.Add(regular);
            VerifyFamily(family, regular, FontStyleKind.Regular);
            faces[FontStyleKind.Regular] = regular;

            foreach (FontStyleKind style in AllStyles.Where(s => s != FontStyleKind.Regular))
            {
                SKTypeface? face = resolver.Resolve(family, style);
                if (face is null)
                {
                    warnings.Add(string.Format(CultureInfo.InvariantCulture, "Font '{0}' has no {1} face installed; the Regular face is used for it.", family, style));
                    faces[style] = regular;
                    continue;
                }

                owned.Add(face);
                VerifyFamily(family, face, style);
                faces[style] = face;
            }
        }
        catch
        {
            DisposeAll(owned);
            throw;
        }

        return new FontService(family, regular.FamilyName, isFallback: false, faces, owned, warnings);
    }

    public SKTypeface Typeface(FontStyleKind style = FontStyleKind.Regular)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return faces[style];
    }

    public SKFont Font(double sizePt, double scale = 1.0, FontStyleKind style = FontStyleKind.Regular)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizePt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        return new SKFont(Typeface(style), (float)(sizePt * scale))
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.None,
            LinearMetrics = true,
        };
    }

    public double DigitWidth(double sizePt, FontStyleKind style = FontStyleKind.Regular)
    {
        using SKFont font = Font(sizePt, 1.0, style);
        double widest = 0;
        for (char digit = '0'; digit <= '9'; digit++)
        {
            widest = Math.Max(widest, font.MeasureText(digit.ToString()));
        }

        return widest;
    }

    public double TextWidth(string text, double sizePt, FontStyleKind style = FontStyleKind.Regular)
    {
        using SKFont font = Font(sizePt, 1.0, style);
        return font.MeasureText(text);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeAll(owned);
        faces.Clear();
    }

    private static void DisposeAll(List<SKTypeface> typefaces)
    {
        foreach (SKTypeface face in typefaces)
        {
            face.Dispose();
        }

        typefaces.Clear();
    }

    private static FontService LoadFallback(FontSpec font, string templatesDir, List<string> warnings)
    {
        Dictionary<FontStyleKind, SKTypeface> faces = new();
        List<SKTypeface> owned = [];
        try
        {
            foreach (FontStyleKind style in AllStyles)
            {
                string key = style.ManifestKey();
                if (!font.FallbackFiles.TryGetValue(key, out string? relative))
                {
                    if (style == FontStyleKind.Regular)
                    {
                        throw new FileNotFoundException("The manifest font.fallbackFiles block has no 'regular' entry; the fallback typeface cannot be loaded.");
                    }

                    faces[style] = faces[FontStyleKind.Regular];
                    continue;
                }

                string path = Path.GetFullPath(Path.Combine(templatesDir, relative));
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Fallback font file not found: " + path, path);
                }

                SKTypeface face = SKTypeface.FromFile(path)
                    ?? throw new InvalidDataException("Fallback font file could not be read as a font: " + path);
                owned.Add(face);
                if (!string.Equals(face.FamilyName, font.Fallback, StringComparison.OrdinalIgnoreCase))
                {
                    throw new FontSubstitutionException(font.Fallback, face.FamilyName, style + " fallback file " + relative);
                }

                faces[style] = face;
            }
        }
        catch
        {
            DisposeAll(owned);
            throw;
        }

        warnings.Add(string.Format(
            CultureInfo.InvariantCulture,
            "Font '{0}' is not installed; using the bundled '{1}' instead. The date glyphs will differ slightly from the printed labels.",
            font.Family, font.Fallback));

        return new FontService(font.Family, faces[FontStyleKind.Regular].FamilyName, isFallback: true, faces, owned, warnings);
    }

    private static void VerifyFamily(string requested, SKTypeface face, FontStyleKind style)
    {
        if (!string.Equals(face.FamilyName, requested, StringComparison.OrdinalIgnoreCase))
        {
            throw new FontSubstitutionException(requested, face.FamilyName, style.ToString());
        }
    }
}
