using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;

namespace Sfp.Refresh.Setup;

/// <summary>
/// Production <see cref="IAnnualFallback"/>: reads the "annualRow:{n}" rectangle (manifest slots.annualTitle) on the
/// annual page that holds block n (page 1 for blocks 1 to 4, page 2 for 5 to 8) and returns the typed text of the
/// outlines there that the tool does not own. Tool-owned outlines are excluded so a title the tool propagated earlier
/// is never read back as if the user had typed it; ink is ignored because the PRD wording is "holds typed text".
/// </summary>
public sealed class AnnualPageFallback : IAnnualFallback
{
    private readonly Manifest manifest;
    private readonly Func<PageKey, PageXml?> reader;
    private readonly Func<PageKey, IReadOnlyCollection<string>> ownedObjectIds;

    /// <param name="manifest">Slot geometry (slots.annualTitle).</param>
    /// <param name="reader">Reads a page by key, null when it cannot be read.</param>
    /// <param name="ownedObjectIds">The tool-owned outline object IDs of a page (from the sidecar).</param>
    public AnnualPageFallback(Manifest manifest, Func<PageKey, PageXml?> reader, Func<PageKey, IReadOnlyCollection<string>> ownedObjectIds)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(ownedObjectIds);
        this.manifest = manifest;
        this.reader = reader;
        this.ownedObjectIds = ownedObjectIds;
    }

    /// <summary>A fallback over a sidecar's anchor map.</summary>
    public static AnnualPageFallback FromSidecar(Manifest manifest, Func<PageKey, PageXml?> reader, Sidecar? sidecar) =>
        new(manifest, reader, key => OwnedIds(sidecar, key));

    /// <summary>The object IDs the sidecar records for a page's anchors (empty when the page or sidecar is unknown).</summary>
    public static IReadOnlyCollection<string> OwnedIds(Sidecar? sidecar, PageKey key)
    {
        if (sidecar is null || !sidecar.Pages.TryGetValue(key.Value, out SidecarPage? page))
        {
            return Array.Empty<string>();
        }

        return page.Anchors.Values.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string? ObjectiveTitle(int n)
    {
        AnnualTitleSlot slot = manifest.Slots.AnnualTitle;
        int perPage = Math.Max(1, slot.BlockTops.Count);
        if (n < 1)
        {
            return null;
        }

        PageKey key = PageKey.Annual(((n - 1) / perPage) + 1);
        PageXml? page = reader(key);
        if (page is null)
        {
            return null;
        }

        IReadOnlyCollection<string> owned = ownedObjectIds(key);
        RectPt rect = slot.Rect((n - 1) % perPage);
        IEnumerable<string> typed = page.OutlinesIn(rect)
            .Where(o => !owned.Contains(o.ObjectId))
            .Select(o => o.PlainText);
        return SetupReader.Normalize(string.Join(" ", typed));
    }
}
