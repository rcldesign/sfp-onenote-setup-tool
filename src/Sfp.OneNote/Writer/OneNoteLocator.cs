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

    [SupportedOSPlatform("windows")]
    private static IOneNoteApp Create(ILogger log) => new OneNoteApp(log);
}
