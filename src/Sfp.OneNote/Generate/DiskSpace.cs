using System.Globalization;
using Sfp.Core.Options;
using Sfp.OneNote.Com;

namespace Sfp.OneNote.Generate;

/// <summary>
/// The PRD's "Disk full" row (a message with the free space needed; exit 4 while rendering, 5 while writing): tells an
/// out-of-space failure from any other and estimates the space a run needs. Windows reports a full disk as an
/// IOException whose HResult is ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL as an HRESULT (0x80070070, 0x80070027);
/// Linux and macOS report ENOSPC, whose errno (28) .NET puts in IOException.HResult. A OneNote call that fails because
/// its disk is full carries the same Win32 HRESULTs. Only the per-page figure is shared with the app's size line
/// (<see cref="NotebookSizeEstimate.BytesPerPage"/>): the size line counts GenerateFormDefaults.PageCount for the
/// quarter, while the message counts the plan's pages (out-of-quarter days and extra Notes pages included), so the two
/// totals can differ.
/// </summary>
public static class DiskSpace
{
    /// <summary>ERROR_DISK_FULL (112) as an HRESULT.</summary>
    public const int ErrorDiskFull = unchecked((int)0x80070070);

    /// <summary>ERROR_HANDLE_DISK_FULL (39) as an HRESULT.</summary>
    public const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    /// <summary>ENOSPC ("No space left on device"), the errno .NET puts in IOException.HResult on Linux and macOS.</summary>
    public const int Enospc = 28;

    /// <summary>True when <paramref name="error"/>, or an exception it wraps, says the disk is full.</summary>
    public static bool IsDiskFull(Exception? error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull or Enospc:
                case OneNoteException oneNote when oneNote.HResult is ErrorDiskFull or ErrorHandleDiskFull:
                    return true;
                case AggregateException aggregate when aggregate.InnerExceptions.Any(IsDiskFull):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Estimated bytes of one page at <paramref name="dpi"/>: the figure of <see cref="NotebookSizeEstimate.BytesPerPage"/>
    /// for that DPI. The CLI and the app accept only the DPIs of that table, but <see cref="GenerateOptions.Dpi"/> does not
    /// enforce them and a caller of the orchestrator can pass any DPI (the end-to-end tests render at 72). Such a DPI falls
    /// back to the nearest table entry (the lower one on a tie) scaled by the square of the DPI ratio, since a page image's
    /// pixel count grows with the DPI squared.
    /// </summary>
    public static long BytesPerPage(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);
        IReadOnlyDictionary<int, long> table = NotebookSizeEstimate.BytesPerPage;
        if (table.TryGetValue(dpi, out long perPage))
        {
            return perPage;
        }

        int nearest = table.Keys.OrderBy(d => Math.Abs(d - dpi)).ThenBy(d => d).First();
        double scale = (double)dpi / nearest;
        return (long)Math.Ceiling(table[nearest] * scale * scale);
    }

    /// <summary>The bytes a run of <paramref name="pages"/> pages needs: <paramref name="pages"/> times <see cref="BytesPerPage"/>.</summary>
    public static long EstimatedBytes(int pages, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pages);
        return pages * BytesPerPage(dpi);
    }

    /// <summary>The estimate in whole megabytes (1,048,576 bytes), rounded, at least 1.</summary>
    public static long EstimatedMegabytes(int pages, int dpi) =>
        Math.Max(1, (long)Math.Round(EstimatedBytes(pages, dpi) / (1024.0 * 1024.0), MidpointRounding.AwayFromZero));

    /// <summary>"Not enough disk space in D:\Notebooks: about 42 MB is needed for 144 pages at 220 DPI."</summary>
    public static string Message(string folder, int pages, int dpi) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Not enough disk space in {0}: about {1} MB is needed for {2} pages at {3} DPI.",
            folder,
            EstimatedMegabytes(pages, dpi),
            pages,
            dpi);
}
