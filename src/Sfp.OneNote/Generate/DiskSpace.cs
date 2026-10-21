using System.Globalization;
using Sfp.OneNote.Com;

namespace Sfp.OneNote.Generate;

/// <summary>
/// The PRD's "Disk full" row (a message with the free space needed; exit 4 while rendering, 5 while writing): tells an
/// out-of-space failure from any other and estimates the space a run needs. Windows reports a full disk as an
/// IOException whose HResult is ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL as an HRESULT (0x80070070, 0x80070027);
/// Linux and macOS report ENOSPC, whose errno (28) .NET puts in IOException.HResult. A OneNote call that fails because
/// its disk is full carries the same Win32 HRESULTs.
/// </summary>
public static class DiskSpace
{
    /// <summary>ERROR_DISK_FULL (112) as an HRESULT.</summary>
    public const int ErrorDiskFull = unchecked((int)0x80070070);

    /// <summary>ERROR_HANDLE_DISK_FULL (39) as an HRESULT.</summary>
    public const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    /// <summary>ENOSPC ("No space left on device"), the errno .NET puts in IOException.HResult on Linux and macOS.</summary>
    public const int Enospc = 28;

    /// <summary>The DPI the per-page estimate is for.</summary>
    public const int ReferenceDpi = 220;

    /// <summary>Estimated bytes of one page image at <see cref="ReferenceDpi"/> (the PRD's 144 pages near 45 MB).</summary>
    public const long BytesPerPageAtReferenceDpi = 300L * 1024;

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

    /// <summary>The bytes a run of <paramref name="pages"/> pages needs: 300 KB per page at 220 DPI, scaled by (dpi / 220) squared.</summary>
    public static long EstimatedBytes(int pages, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pages);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);
        double scale = (double)dpi / ReferenceDpi;
        return (long)Math.Ceiling(pages * BytesPerPageAtReferenceDpi * scale * scale);
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
