using SkiaSharp;

namespace Sfp.Render.Fonts;

/// <summary>
/// Resolves families by scanning font files (.ttf, .otf, .ttc) in a list of directories and matching the family name each
/// file reports, case-insensitively. Used when the caller names the font directories explicitly (a test with a folder
/// that lacks Bookman, or a machine where the system font manager is not trusted). The directories are scanned once,
/// lazily, for family and style; each successful <see cref="Resolve"/> opens a fresh typeface that the caller owns.
/// </summary>
public sealed class DirectoryFontResolver : IFontResolver
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];

    private readonly IReadOnlyList<string> directories;
    private readonly bool recursive;
    private readonly Lazy<List<FontFile>> files;

    /// <param name="directories">Folders to scan; missing folders are ignored.</param>
    /// <param name="recursive">Scan subfolders as well.</param>
    public DirectoryFontResolver(IReadOnlyList<string> directories, bool recursive = false)
    {
        this.directories = directories;
        this.recursive = recursive;
        files = new Lazy<List<FontFile>>(Scan);
    }

    /// <summary>Distinct family names found in the directories, sorted.</summary>
    public IReadOnlyList<string> Families =>
        files.Value.Select(f => f.Family).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public SKTypeface? Resolve(string family, FontStyleKind style)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        List<FontFile> candidates = files.Value
            .Where(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        // Exact style first, then the closest: same boldness beats same slant, and regular is the last resort.
        FontFile chosen = candidates.FirstOrDefault(f => f.Style == style)
            ?? candidates.FirstOrDefault(f => f.Style.IsBold() == style.IsBold())
            ?? candidates.FirstOrDefault(f => f.Style == FontStyleKind.Regular)
            ?? candidates[0];
        return SKTypeface.FromFile(chosen.Path);
    }

    private List<FontFile> Scan()
    {
        List<FontFile> found = [];
        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*", option).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                using SKTypeface? face = SKTypeface.FromFile(file);
                if (face is not null)
                {
                    found.Add(new FontFile(file, face.FamilyName, FontStyleKindExtensions.Of(face)));
                }
            }
        }

        return found;
    }

    private sealed record FontFile(string Path, string Family, FontStyleKind Style);
}
