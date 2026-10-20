using Sfp.Core.Dates;

namespace Sfp.OneNote;

/// <summary>
/// sfp-state.json beside the notebook folder: tool version, manifest id, quarter, notebook name and a map from page key
/// to IDs and tool-owned anchor object IDs. Rebuildable from the notebook alone by reading each page's sfp.key meta.
/// </summary>
public sealed record Sidecar
{
    public const string FileName = "sfp-state.json";

    public int SchemaVersion { get; init; } = 1;

    public required string ToolVersion { get; init; }

    public required string ManifestId { get; init; }

    public required QuarterSpec Quarter { get; init; }

    public required string NotebookName { get; init; }

    /// <summary>Folder path (or URL after a move) OneNote reports for the notebook.</summary>
    public required string NotebookPath { get; init; }

    public required string NotebookId { get; init; }

    /// <summary>Section name to OneNote section ID.</summary>
    public IReadOnlyDictionary<string, string> Sections { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Page key value to page record.</summary>
    public IReadOnlyDictionary<string, SidecarPage> Pages { get; init; } = new Dictionary<string, SidecarPage>(StringComparer.Ordinal);

    public DateTimeOffset? Generated { get; init; }

    public DateTimeOffset? LastRefresh { get; init; }

    /// <summary>Calendar writes never touch days on or before this date.</summary>
    public DateOnly? FrozenThrough { get; init; }

    /// <summary>Path of the previous quarter's notebook when generated with --previous.</summary>
    public string? PreviousNotebook { get; init; }
}

/// <summary>One page's IDs and its tool-owned objects.</summary>
public sealed record SidecarPage
{
    public required string PageId { get; init; }

    public required string SectionId { get; init; }

    /// <summary>Slot or anchor id to OneNote object ID of the outline the tool wrote.</summary>
    public IReadOnlyDictionary<string, string> Anchors { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
