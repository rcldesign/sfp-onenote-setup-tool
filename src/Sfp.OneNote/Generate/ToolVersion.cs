using System.Reflection;

namespace Sfp.OneNote.Generate;

/// <summary>
/// The tool version written into the sidecar: the assembly informational version ("1.0.0+commit"). Every project in the
/// solution shares one Version (Directory.Build.props), so this assembly's own version is the tool's whatever the host
/// process (CLI, app or test runner); the entry assembly is the fallback.
/// </summary>
public static class ToolVersion
{
    private static readonly Lazy<string> CurrentLazy = new(Read);

    /// <summary>The informational version of this assembly, else of the entry assembly, else "unknown".</summary>
    public static string Current => CurrentLazy.Value;

    private static string Read()
    {
        string? own = Informational(typeof(ToolVersion).Assembly);
        string? entry = Informational(Assembly.GetEntryAssembly());
        return own ?? entry ?? "unknown";
    }

    private static string? Informational(Assembly? assembly)
    {
        string? version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) || !char.IsAsciiDigit(version[0]) ? null : version;
    }
}
