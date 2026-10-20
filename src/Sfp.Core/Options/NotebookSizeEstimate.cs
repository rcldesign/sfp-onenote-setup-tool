using System.Globalization;

namespace Sfp.Core.Options;

/// <summary>The size line under the DPI dropdown: pages times a per-page figure for the DPI, shown in MB.</summary>
public static class NotebookSizeEstimate
{
    /// <summary>The notebook size target from the non-functional requirements, in bytes.</summary>
    public const long TargetBytes = 60L * 1024 * 1024;

    /// <summary>Bytes per page at each allowed DPI (a 24-bit PNG of a sheet, base64 inside the page).</summary>
    public static IReadOnlyDictionary<int, long> BytesPerPage { get; } = new Dictionary<int, long>
    {
        [144] = 140L * 1024,
        [220] = 300L * 1024,
        [300] = 520L * 1024,
    };

    /// <summary>Estimated notebook size in bytes. Throws for a DPI outside the allowed set.</summary>
    public static long Bytes(int dpi, int pageCount)
    {
        if (!BytesPerPage.TryGetValue(dpi, out long perPage))
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be 144, 220 or 300.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        return perPage * pageCount;
    }

    /// <summary>"About 42 MB for 144 pages at 220 DPI", with a note when the estimate is above the 60 MB target.</summary>
    public static string Describe(int dpi, int pageCount)
    {
        long bytes = Bytes(dpi, pageCount);
        double megabytes = bytes / (1024.0 * 1024.0);
        string text = string.Format(
            CultureInfo.InvariantCulture,
            "About {0:0} MB for {1} pages at {2} DPI",
            megabytes,
            pageCount,
            dpi);
        return bytes > TargetBytes ? text + " (above the 60 MB target)" : text;
    }
}
