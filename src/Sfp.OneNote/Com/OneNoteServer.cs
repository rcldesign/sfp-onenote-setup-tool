using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Sfp.OneNote.Com;

/// <summary>
/// The executable registered to serve OneNote.Application (the LocalServer32 of its CLSID) and its bitness, read without
/// starting OneNote (<see cref="Read"/>). The type library check needs the bitness: a 64-bit OneNote may not load a type
/// library registered only for 32-bit clients, and a 32-bit one may not load one registered only for 64-bit clients
/// (<see cref="OneNoteTypeLibrary.Check(OneNoteTypeLibrary.Registration, Func{string, bool}, Bitness)"/>).
/// </summary>
/// <param name="Path">
/// The executable as <see cref="ServerPath"/> took it from the registered value (the file may be missing); null when no
/// value is registered or a key cannot be read.
/// </param>
/// <param name="Bitness">The executable's bitness (<see cref="PeMachine.Read"/>); <see cref="Bitness.Unknown"/> without a readable file.</param>
public sealed record OneNoteServer(string? Path, Bitness Bitness)
{
    /// <summary>No server found: nothing registered, or a key that cannot be read.</summary>
    public static OneNoteServer NotFound { get; } = new(null, Bitness.Unknown);

    /// <summary>
    /// Reads the server from HKEY_CLASSES_ROOT (the machine's and the user's classes merged): the default value of
    /// OneNote.Application\CLSID, then the default value of CLSID\{that CLSID}\LocalServer32, and the bitness of the
    /// executable that value names (<see cref="FromLocalServer32"/>). The keys are read in this process's registry view
    /// (64-bit for the win-x64 release) and, when that view has no CLSID value or no LocalServer32 value, again in the
    /// 32-bit view (RegistryView.Registry32), where a 32-bit Office on 64-bit Windows can register its COM server
    /// (<see cref="PreferOwnView"/>). <see cref="NotFound"/> when neither view has both values. A key that cannot be read
    /// (IOException, UnauthorizedAccessException, SecurityException, or ArgumentException for a CLSID value the registry
    /// refuses as a key name) counts as absent.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static OneNoteServer Read() => FromLocalServer32(PreferOwnView(ReadLocalServer32(Registry.ClassesRoot), ReadLocalServer32InRegistry32));

    /// <summary>
    /// The LocalServer32 value read in this process's registry view, <paramref name="ownView"/>, unless it is absent or
    /// blank; otherwise the value <paramref name="registry32"/> reads in the 32-bit view, which is asked only then.
    /// </summary>
    internal static string? PreferOwnView(string? ownView, Func<string?> registry32)
    {
        ArgumentNullException.ThrowIfNull(registry32);
        return string.IsNullOrWhiteSpace(ownView) ? registry32() : ownView;
    }

    /// <summary>
    /// The executable a LocalServer32 value names: the value with surrounding white space and quotes trimmed, when
    /// <paramref name="fileExists"/> finds that file. Otherwise the part of it before its first argument separator,
    /// " /" or " -" (as in "C:\Office16\ONENOTE.EXE" /automation), trimmed the same way, when that file exists. When
    /// neither exists, the trimmed value. Null for an empty, white space or quotes-only value.
    /// </summary>
    public static string? ServerPath(string registryValue, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(registryValue);
        ArgumentNullException.ThrowIfNull(fileExists);
        string whole = OneNoteTypeLibrary.Unquote(registryValue);
        if (whole.Length == 0)
        {
            return null;
        }

        if (fileExists(whole))
        {
            return whole;
        }

        int separator = FirstArgumentSeparator(whole);
        if (separator > 0)
        {
            string command = OneNoteTypeLibrary.Unquote(whole[..separator]);
            if (command.Length > 0 && fileExists(command))
            {
                return command;
            }
        }

        return whole;
    }

    /// <summary>
    /// The server a LocalServer32 value names (<see cref="ServerPath"/>, asking <see cref="File.Exists"/>) with its
    /// bitness (<see cref="PeMachine.Read"/>, unknown when that file is missing or cannot be read); <see cref="NotFound"/>
    /// for an absent or blank value.
    /// </summary>
    internal static OneNoteServer FromLocalServer32(string? value)
    {
        string? path = value is null ? null : ServerPath(value, File.Exists);
        return path is null ? NotFound : new OneNoteServer(path, PeMachine.Read(path));
    }

    /// <summary>The index of the first " /" or " -" in <paramref name="value"/>, or -1 when it has neither.</summary>
    private static int FirstArgumentSeparator(string value)
    {
        int slash = value.IndexOf(" /", StringComparison.Ordinal);
        int dash = value.IndexOf(" -", StringComparison.Ordinal);
        return slash < 0 ? dash : dash < 0 ? slash : Math.Min(slash, dash);
    }

    /// <summary>The LocalServer32 value of OneNote.Application's CLSID read in the 32-bit registry view; null as <see cref="ReadLocalServer32"/> gives it, or when that view cannot be opened.</summary>
    [SupportedOSPlatform("windows")]
    private static string? ReadLocalServer32InRegistry32()
    {
        try
        {
            using RegistryKey classesRoot = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32);
            return ReadLocalServer32(classesRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The LocalServer32 value of OneNote.Application's CLSID under <paramref name="classesRoot"/>, in that key's registry
    /// view; null when a key or value is absent, and when a key cannot be read.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? ReadLocalServer32(RegistryKey classesRoot)
    {
        try
        {
            using RegistryKey? progId = classesRoot.OpenSubKey(ProgIdActivator.ProgId + @"\CLSID");
            if (progId?.GetValue(null) is not string clsid || string.IsNullOrWhiteSpace(clsid))
            {
                return null;
            }

            using RegistryKey? server = classesRoot.OpenSubKey(@"CLSID\" + clsid.Trim() + @"\LocalServer32");
            return server?.GetValue(null) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return null;
        }
    }
}
