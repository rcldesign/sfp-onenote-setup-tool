using System.Runtime.Versioning;
using Serilog;
using Sfp.OneNote.Com;

namespace Sfp.OneNote.Writer;

/// <summary>
/// Answers "is OneNote desktop here" without starting it, and hands out the run's one COM session on demand. The
/// preflight asks the first question and uses the session for the sign-in check; the writer uses the same session.
/// Tests inject a fake; the production class is Windows-only behind the interface.
/// </summary>
public interface IOneNoteLocator
{
    /// <summary>True when the OneNote.Application ProgID resolves on this machine (always false off Windows).</summary>
    bool IsInstalled { get; }

    /// <summary>
    /// The type library check, read without starting OneNote: the registration against the bitness of the executable
    /// registered as OneNote's server
    /// (<see cref="OneNoteTypeLibrary.Check(OneNoteTypeLibrary.Registration, Func{string, bool}, Bitness)"/>). An error
    /// stops the run before OneNote is started, a warning is reported and the run goes on. Both are null for a sound
    /// registration, and off Windows (<see cref="TypeLibraryCheck.None"/>).
    /// </summary>
    TypeLibraryCheck TypeLibrary { get; }

    /// <summary>The run's COM session, created on first use and owned by the locator. Throws <see cref="OneNoteUnavailableException"/> off Windows.</summary>
    IOneNoteApp Open();
}

/// <summary>Production <see cref="IOneNoteLocator"/> over <see cref="OneNoteApp"/>. Dispose at the end of the run.</summary>
public sealed class OneNoteLocator : IOneNoteLocator, IDisposable
{
    private readonly ILogger _log;
    private IOneNoteApp? _app;

    public OneNoteLocator(ILogger log)
    {
        _log = log;
    }

    public bool IsInstalled => OperatingSystem.IsWindows() && ProgIdResolves();

    /// <summary>
    /// Reads the registration and OneNote's server (<see cref="OneNoteServer.Read"/>) on each access and logs them as one
    /// line (both values, or "absent", whether each file exists, and the server's path, or "absent", with its bitness), so
    /// the run's log shows what the type library check saw; a warning is logged as well, since it reaches the user only
    /// with the run's warnings.
    /// </summary>
    public TypeLibraryCheck TypeLibrary
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return TypeLibraryCheck.None;
            }

            OneNoteTypeLibrary.Registration registration = OneNoteTypeLibrary.Read();
            OneNoteServer server = OneNoteServer.Read();

            // The bitness goes in as its name: the run log's {Message:lj} renders a string as it is, an enum as a JSON string in quotes.
            _log.Information(
                @"OneNote type library HKEY_CLASSES_ROOT\{Key}: win64 = {Win64} (file exists: {Win64Exists}), win32 = {Win32} (file exists: {Win32Exists}), OneNote server = {ServerPath} ({ServerBitness})",
                OneNoteTypeLibrary.KeyPath,
                registration.Win64 ?? "absent",
                FileExists(registration.Win64),
                registration.Win32 ?? "absent",
                FileExists(registration.Win32),
                server.Path ?? "absent",
                server.Bitness.ToString());
            TypeLibraryCheck check = OneNoteTypeLibrary.Check(registration, File.Exists, server.Bitness);
            if (check.Warning is not null)
            {
                _log.Warning("OneNote type library (reported with the run's warnings): {Warning}", check.Warning);
            }

            return check;
        }
    }

    public IOneNoteApp Open()
    {
        if (_app is null)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new OneNoteUnavailableException(OneNoteUnavailableException.UserMessage + " This machine is not running Windows.");
            }

            _app = Create(_log);
        }

        return _app;
    }

    public void Dispose()
    {
        _app?.Dispose();
        _app = null;
    }

    [SupportedOSPlatform("windows")]
    private static bool ProgIdResolves() => Type.GetTypeFromProgID(ProgIdActivator.ProgId, throwOnError: false) is not null;

    /// <summary>Whether the file a registered type library value names exists; false for an absent or blank value.</summary>
    private static bool FileExists(string? value) => !string.IsNullOrWhiteSpace(value) && File.Exists(OneNoteTypeLibrary.FilePath(value));

    [SupportedOSPlatform("windows")]
    private static IOneNoteApp Create(ILogger log) => new OneNoteApp(log);
}
