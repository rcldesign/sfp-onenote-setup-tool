using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Sfp.OneNote.Com;

/// <summary>
/// What the type library check found
/// (<see cref="OneNoteTypeLibrary.Check(OneNoteTypeLibrary.Registration, Func{string, bool}, Bitness)"/>).
/// <see cref="Error"/> stops the run before OneNote is started (exit 3). <see cref="Warning"/> is reported with the run's
/// warnings and the run goes on. Both are null for a sound registration, and off Windows (<see cref="None"/>).
/// </summary>
public sealed record TypeLibraryCheck(string? Error, string? Warning)
{
    /// <summary>Nothing to report: a sound registration, or a machine where the check does not run.</summary>
    public static TypeLibraryCheck None { get; } = new(null, null);
}

/// <summary>
/// OneNote's registered type library (OneNote 2013 and later register version 1.1). OneNote needs it to answer automation
/// calls, late-bound (dynamic, as this tool calls it) and early-bound alike: when its registration points at a file that
/// does not exist, OneNote still starts, then refuses the calls (on the owner's PC the first one failed with a bare
/// E_FAIL). The Generate preflight reads the registration (<see cref="Read"/>) and OneNote's bitness
/// (<see cref="OneNoteServer.Read"/>) and checks them (<see cref="Check(Registration, Func{string, bool}, Bitness)"/>)
/// before it starts OneNote.
/// </summary>
public static class OneNoteTypeLibrary
{
    /// <summary>The type library's LIBID.</summary>
    public const string Guid = "{0EA692EE-BB50-4E3C-AEF0-356D91732725}";

    /// <summary>The type library version OneNote 2013 and later register.</summary>
    public const string Version = "1.1";

    /// <summary>The version's language-neutral key, relative to HKEY_CLASSES_ROOT; its win64 and win32 subkeys name the file.</summary>
    public const string KeyPath = @"TypeLib\" + Guid + @"\" + Version + @"\0";

    private const string FullKey = @"HKEY_CLASSES_ROOT\" + KeyPath;

    /// <summary>The key as reg.exe names it, for the command the bitness warning gives.</summary>
    private const string RegKey = @"HKCR\" + KeyPath;

    private const string InstalledExample = @"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE\3";

    private const string NotRegisteredWarning =
        "OneNote's type library is not registered in this computer's registry (" + FullKey + " has no win64 or win32 entry). "
        + "A virtualized Office may still carry it; if OneNote refuses the next call, run an Online Repair of Office (Settings, Apps, Microsoft Office, Modify).";

    private const string Win64Subkey = "win64";

    private const string Win32Subkey = "win32";

    /// <summary>
    /// The default values of the <see cref="KeyPath"/> subkeys win64 and win32, as registered (a path, usually with a
    /// resource index, such as C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE\3); null when the subkey or its
    /// value is absent.
    /// </summary>
    public sealed record Registration(string? Win64, string? Win32);

    /// <summary>
    /// The file a registered value names: surrounding quotes and white space trimmed, a trailing backslash plus digits
    /// (the resource index, as in ONENOTE.EXE\3) removed, and environment variables expanded. A value without a resource
    /// index is kept as it is.
    /// </summary>
    public static string FilePath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string path = Unquote(value);
        int slash = path.LastIndexOf('\\');
        if (slash >= 0 && slash < path.Length - 1 && path.AsSpan(slash + 1).IndexOfAnyExceptInRange('0', '9') < 0)
        {
            // Only digits follow the last backslash: that is the index of the type library resource inside the file.
            path = Unquote(path[..slash]);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// <see cref="Check(Registration, Func{string, bool}, Bitness)"/> with OneNote's bitness unknown
    /// (<see cref="Bitness.Unknown"/>): the registration alone decides.
    /// </summary>
    public static TypeLibraryCheck Check(Registration registration, Func<string, bool> fileExists) =>
        Check(registration, fileExists, Bitness.Unknown);

    /// <summary>
    /// Checks the registration against OneNote's bitness. <paramref name="fileExists"/> is asked once about
    /// <see cref="FilePath"/> of each registered value (an empty or white space value names no file and counts as
    /// absent); an entry is usable when its file exists. An error, which stops the run before OneNote is started, when at
    /// least one entry is registered and none is usable: it names each registered entry with its value as registered. A
    /// warning, and the run goes on, when nothing is registered: an App-V or packaged Office can keep the type library in a
    /// virtual registry this process cannot see, so a missing key is not proof. A warning when
    /// <paramref name="serverBitness"/> is <see cref="Bitness.X64"/> and win32 is the only usable entry, or
    /// <see cref="Bitness.X86"/> and win64 is: OneNote may not load a type library registered only for clients of the
    /// other bitness, so the warning gives the reg add command that registers the same file for OneNote's own. Otherwise
    /// a warning when one entry's file is missing beside a usable one: a 32-bit OneNote reads only win32 and a 64-bit one
    /// may read a stale win64 entry. One warning at most: the bitness warning takes precedence over the stale entry
    /// warning. Nothing when every registered entry is usable and the bitness warning does not apply. With
    /// <see cref="Bitness.Unknown"/> the bitness rule does not apply, and no bitness changes what is an error.
    /// </summary>
    public static TypeLibraryCheck Check(Registration registration, Func<string, bool> fileExists, Bitness serverBitness)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(fileExists);
        List<(string Subkey, string Value)> registered = new();
        if (!string.IsNullOrWhiteSpace(registration.Win64))
        {
            registered.Add((Win64Subkey, registration.Win64));
        }

        if (!string.IsNullOrWhiteSpace(registration.Win32))
        {
            registered.Add((Win32Subkey, registration.Win32));
        }

        if (registered.Count == 0)
        {
            return new TypeLibraryCheck(null, NotRegisteredWarning);
        }

        List<(string Subkey, string Value)> missing = registered.Where(entry => !fileExists(FilePath(entry.Value))).ToList();
        if (missing.Count == registered.Count)
        {
            string entries = string.Join(" and ", missing.Select(entry => FullKey + @"\" + entry.Subkey + " = " + entry.Value));
            string values = missing.Count == 1 ? "that value" : "those values";
            return new TypeLibraryCheck(
                "OneNote's type library registration points at a file that does not exist: " + entries + ", so every automation call fails. "
                + "Run an Online Repair of Office (Settings, Apps, Microsoft Office, Modify), or set " + values + " to the installed ONENOTE.EXE, for example " + InstalledExample + ", then retry.",
                null);
        }

        // At least one entry is usable from here on, so nothing below is an error.
        List<(string Subkey, string Value)> usable = registered.Where(entry => !missing.Contains(entry)).ToList();
        string? bitnessWarning = BitnessWarning(usable, serverBitness);
        if (bitnessWarning is not null)
        {
            return new TypeLibraryCheck(null, bitnessWarning);
        }

        if (missing.Count == 0)
        {
            return TypeLibraryCheck.None;
        }

        (string stale, string value) = missing[0];
        string sound = usable[0].Subkey;
        return new TypeLibraryCheck(
            null,
            "OneNote's type library entry " + FullKey + @"\" + stale + " = " + value + " points at a file that does not exist; the " + sound + " entry is sound. "
            + "If OneNote refuses the next call, set the stale value to the installed ONENOTE.EXE, for example " + InstalledExample + ", or run an Online Repair of Office.");
    }

    /// <summary>
    /// The warning for a type library that only clients of the other bitness can use: OneNote <see cref="Bitness.X64"/>
    /// with win32 the only usable entry, or <see cref="Bitness.X86"/> with win64 the only usable entry. It names that
    /// entry with its value as registered (resource index included) and gives the reg add command that registers the same
    /// value under OneNote's own subkey; /f overwrites a stale value there. Null when the bitness is unknown or OneNote's
    /// own entry is usable.
    /// </summary>
    private static string? BitnessWarning(List<(string Subkey, string Value)> usable, Bitness serverBitness)
    {
        (string Own, string Other, string Bits, string OtherBits)? sides = serverBitness switch
        {
            Bitness.X64 => (Win64Subkey, Win32Subkey, "64", "32"),
            Bitness.X86 => (Win32Subkey, Win64Subkey, "32", "64"),
            _ => null,
        };
        if (sides is not { } side || usable.Count != 1 || usable[0].Subkey != side.Other)
        {
            return null;
        }

        string value = usable[0].Value;
        return "OneNote is " + side.Bits + "-bit but its type library is registered only for " + side.OtherBits + "-bit clients: "
            + FullKey + " has a " + side.Other + " entry (" + value + ") and no usable " + side.Own + " entry. "
            + "A " + side.Bits + "-bit OneNote may not load it, which fails every automation call with E_FAIL. "
            + "Add the entry from an administrator command prompt: reg add \"" + RegKey + @"\" + side.Own + "\" /ve /d \"" + value + "\" /f  "
            + "(the same file; remove the entry again with reg delete if it does not help), or run an Online Repair of Office.";
    }

    /// <summary>
    /// Reads the registration from HKEY_CLASSES_ROOT (the machine's and the user's classes merged). A subkey that cannot be
    /// read (IOException, UnauthorizedAccessException, SecurityException) counts as absent.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Registration Read() => new(ReadValue(Win64Subkey), ReadValue(Win32Subkey));

    [SupportedOSPlatform("windows")]
    private static string? ReadValue(string subkey)
    {
        try
        {
            using RegistryKey? key = Registry.ClassesRoot.OpenSubKey(KeyPath + @"\" + subkey);
            return key?.GetValue(null) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>The value with surrounding white space and quotes trimmed (also used for the OneNote server's path, <see cref="OneNoteServer.ServerPath"/>).</summary>
    internal static string Unquote(string value) => value.Trim().Trim('"').Trim();
}
