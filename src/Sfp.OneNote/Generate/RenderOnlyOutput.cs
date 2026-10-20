using System.Text.Json;
using System.Text.Json.Serialization;
using Sfp.Core.Plan;
using Sfp.Render;

namespace Sfp.OneNote.Generate;

/// <summary>One line of pages.json: a rendered page and where its PNG went.</summary>
public sealed record PageIndexEntry(
    string Key,
    string File,
    string Title,
    string Section,
    int Level,
    int? PdfPage,
    int Width,
    int Height,
    IReadOnlyList<AnchorPlacement> Anchors);

/// <summary>The pages.json document written by render-only runs.</summary>
public sealed record PageIndex(string Notebook, string Quarter, int Dpi, int Count, IReadOnlyList<PageIndexEntry> Pages);

/// <summary>
/// Render-only output: one {key}.png per page (the key's colons become hyphens so the name is a file name) and a
/// pages.json index with key, title, section, level, PDF page, pixel size and anchor placements. This is how overlay
/// positions are tuned and how the golden suite runs on a machine without Office.
/// </summary>
public static class RenderOnlyOutput
{
    public const string IndexFileName = "pages.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>"day:2027-01-04" gives "day-2027-01-04.png".</summary>
    public static string FileName(PageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Value.Replace(':', '-') + ".png";
    }

    /// <summary>Writes the PNG and returns the index entry for it.</summary>
    public static PageIndexEntry WritePage(string dir, PageSpec spec, string section, RenderedPage art)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(art);
        string file = FileName(art.Key);
        File.WriteAllBytes(Path.Combine(dir, file), art.Png);
        return new PageIndexEntry(art.Key.Value, file, spec.Title, section, spec.Level, spec.PdfPage, art.WidthPx, art.HeightPx, art.Anchors);
    }

    /// <summary>Writes pages.json and returns its path.</summary>
    public static string WriteIndex(string dir, NotebookPlan plan, int dpi, IReadOnlyList<PageIndexEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(entries);
        PageIndex index = new(plan.Name, plan.Quarter.Spec.ToString(), dpi, entries.Count, entries);
        string path = Path.Combine(dir, IndexFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(index, JsonOptions));
        return path;
    }

    /// <summary>Reads a pages.json back (for tests and tooling).</summary>
    public static PageIndex ReadIndex(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return JsonSerializer.Deserialize<PageIndex>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("pages.json is empty: " + path);
    }
}
