using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Sfp.OneNote.Com;

/// <summary>
/// The elevation of this tool and of the running OneNote processes, as <see cref="ProcessElevation.Read"/> found them.
/// <see cref="ProcessElevation.Problem"/> reports a mismatch when <see cref="OneNotePids"/> is not empty. Two states are
/// equal when every value is equal, the process IDs compared in order.
/// </summary>
/// <param name="ToolElevated">
/// True when this process's own token is elevated (TokenElevation): under UAC, only a process started as administrator.
/// Null when its token cannot be read.
/// </param>
/// <param name="OneNoteElevated">
/// The elevation the processes in <see cref="OneNotePids"/> share, the opposite of the tool's, when there are any.
/// Otherwise the elevation every readable OneNote process shares, which is the tool's own when that is known. Null when
/// no OneNote process could be read, and when the readable ones differ among themselves while the tool's elevation is
/// unknown.
/// </param>
/// <param name="OneNotePids">
/// The IDs of the OneNote processes whose token could be read and whose elevation differs from the tool's, in ascending
/// order. Empty when there are none, and when the tool's elevation is unknown.
/// </param>
/// <param name="OneNoteUnreadableCount">How many OneNote processes were found whose token could not be read; they are left out of the comparison.</param>
public sealed record ElevationState(bool? ToolElevated, bool? OneNoteElevated, IReadOnlyList<int> OneNotePids, int OneNoteUnreadableCount)
{
    /// <summary>Nothing known: neither side was read.</summary>
    public static ElevationState Unknown { get; } = new(null, null, Array.Empty<int>(), 0);

    /// <summary>True when every value is equal, the process IDs compared in order.</summary>
    public bool Equals(ElevationState? other) =>
        other is not null
        && ToolElevated == other.ToolElevated
        && OneNoteElevated == other.OneNoteElevated
        && OneNoteUnreadableCount == other.OneNoteUnreadableCount
        && OneNotePids.SequenceEqual(other.OneNotePids);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(ToolElevated);
        hash.Add(OneNoteElevated);
        hash.Add(OneNoteUnreadableCount);
        foreach (int pid in OneNotePids)
        {
            hash.Add(pid);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// The elevation check of the Generate preflight, read without starting or stopping anything. OneNote refuses automation
/// across an elevation boundary: a client running as administrator does not reach the OneNote the user runs normally,
/// COM starts a second OneNote at the client's elevation, and that one answers E_FAIL (and the reverse). The preflight
/// reads both sides (<see cref="Read"/>) before it opens OneNote and stops when a OneNote process runs at the other
/// elevation (<see cref="Problem"/>).
/// </summary>
public static class ProcessElevation
{
    /// <summary>OneNote desktop's process name: ONENOTE.EXE without its extension.</summary>
    private const string OneNoteProcessName = "ONENOTE";

    /// <summary>
    /// The problem to report when the tool's elevation is known and <see cref="ElevationState.OneNotePids"/> is not
    /// empty: it names those processes ("OneNote process 1234", or "OneNote processes 1234, 5678" for more than one) and,
    /// by the tool's side, which side to restart. Null otherwise: no OneNote process at the other elevation (none
    /// running, whose COM start then follows the tool's own elevation; all at the tool's elevation; or none whose token
    /// could be read), or the tool's elevation unknown.
    /// </summary>
    public static string? Problem(ElevationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ToolElevated is not bool tool || state.OneNotePids is not { Count: > 0 } pids)
        {
            return null;
        }

        string processes = (pids.Count == 1 ? "OneNote process " : "OneNote processes ") + JoinPids(pids);
        return tool
            ? string.Format(
                CultureInfo.InvariantCulture,
                "This tool is running as administrator but OneNote is not ({0}). OneNote refuses automation across that boundary and answers E_FAIL. Close this window and run the tool from a normal, non-elevated prompt or shortcut, then retry.",
                processes)
            : string.Format(
                CultureInfo.InvariantCulture,
                "OneNote is running as administrator ({0}) but this tool is not. OneNote refuses automation across that boundary and answers E_FAIL. Close OneNote completely (also its icon in the notification area) and open it normally, not as administrator, then retry.",
                processes);
    }

    /// <summary>
    /// Reads both sides the same way: the TokenElevation flag of a process's token (OpenProcess with
    /// PROCESS_QUERY_LIMITED_INFORMATION, the least access that opens a token, then OpenProcessToken with TOKEN_QUERY and
    /// GetTokenInformation), for this tool's own process and for every process named ONENOTE, and compares them
    /// (<see cref="FromProcesses"/>). Under UAC a token is elevated only in a process started as administrator. A side that
    /// cannot be read is left out instead of raising an exception: the tool's elevation is null when its own token cannot
    /// be read; a OneNote process whose token cannot be read (access denied, or a process that exited after the listing)
    /// is only counted; and a process list that cannot be read counts as no OneNote process.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ElevationState Read()
    {
        bool? toolElevated = ReadToolElevated();
        List<(int Pid, bool Elevated)> readable = new();
        int unreadable = 0;
        foreach (Process process in ListOneNoteProcesses())
        {
            using (process)
            {
                if (TryRead(process) is { } read)
                {
                    readable.Add(read);
                }
                else
                {
                    unreadable++;
                }
            }
        }

        return FromProcesses(toolElevated, readable, unreadable);
    }

    /// <summary>
    /// The state for the tool's elevation and the OneNote processes whose elevation was read. With the tool's elevation
    /// known, the processes at the other elevation are <see cref="ElevationState.OneNotePids"/>, in ascending order, and
    /// that elevation is <see cref="ElevationState.OneNoteElevated"/>. Without such a process, or with the tool's elevation
    /// unknown, there are no such IDs, and OneNote's elevation is the one every readable process shares, or null when none
    /// was read or they differ among themselves.
    /// </summary>
    internal static ElevationState FromProcesses(bool? toolElevated, IReadOnlyCollection<(int Pid, bool Elevated)> readable, int unreadableCount)
    {
        ArgumentNullException.ThrowIfNull(readable);
        ArgumentOutOfRangeException.ThrowIfNegative(unreadableCount);
        if (toolElevated is bool tool)
        {
            int[] differing = readable.Where(p => p.Elevated != tool).Select(p => p.Pid).Order().ToArray();
            if (differing.Length > 0)
            {
                return new ElevationState(tool, !tool, differing, unreadableCount);
            }
        }

        bool? shared = readable.Select(p => p.Elevated).Distinct().ToList() is [bool only] ? only : null;
        return new ElevationState(toolElevated, shared, Array.Empty<int>(), unreadableCount);
    }

    /// <summary>The process IDs as the problem and the log show them: in the order given, joined by ", ".</summary>
    internal static string JoinPids(IEnumerable<int> pids) =>
        string.Join(", ", pids.Select(pid => pid.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Whether this tool's own process token is elevated; null when it cannot be read.</summary>
    [SupportedOSPlatform("windows")]
    private static bool? ReadToolElevated()
    {
        try
        {
            return IsElevated(Environment.ProcessId);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Every process named ONENOTE (not ONENOTEM, its notification area helper); none when the list cannot be read.</summary>
    [SupportedOSPlatform("windows")]
    private static Process[] ListOneNoteProcesses()
    {
        try
        {
            return Process.GetProcessesByName(OneNoteProcessName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return Array.Empty<Process>();
        }
    }

    /// <summary>A listed OneNote process's ID and elevation; null when its token cannot be read.</summary>
    [SupportedOSPlatform("windows")]
    private static (int Pid, bool Elevated)? TryRead(Process process)
    {
        try
        {
            int pid = process.Id;
            return (pid, IsElevated(pid));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the process's token is elevated (TOKEN_ELEVATION.TokenIsElevated nonzero). Throws
    /// <see cref="Win32Exception"/> with the Win32 error when the process or its token cannot be opened or queried (an
    /// exited process included); both handles are closed either way.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool IsElevated(int processId)
    {
        using SafeProcessHandle process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        bool opened = NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out SafeAccessTokenHandle token);
        int openError = Marshal.GetLastPInvokeError();
        using (token)
        {
            if (!opened)
            {
                throw new Win32Exception(openError);
            }

            if (!NativeMethods.GetTokenInformation(
                token,
                NativeMethods.TokenElevationClass,
                out NativeMethods.TokenElevation elevation,
                Marshal.SizeOf<NativeMethods.TokenElevation>(),
                out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return elevation.TokenIsElevated != 0;
        }
    }

    /// <summary>The Win32 calls that read a process's token elevation (kernel32 and advapi32, from System32 only).</summary>
    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        /// <summary>PROCESS_QUERY_LIMITED_INFORMATION: enough to open the process's token for a query.</summary>
        internal const uint ProcessQueryLimitedInformation = 0x1000;

        /// <summary>TOKEN_QUERY.</summary>
        internal const uint TokenQuery = 0x0008;

        /// <summary>TokenElevation in the TOKEN_INFORMATION_CLASS enumeration.</summary>
        internal const int TokenElevationClass = 20;

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(SafeProcessHandle processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(
            SafeAccessTokenHandle tokenHandle,
            int tokenInformationClass,
            out TokenElevation tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        /// <summary>TOKEN_ELEVATION: TokenIsElevated is nonzero when the token is elevated.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct TokenElevation
        {
            public int TokenIsElevated;
        }
    }
}
