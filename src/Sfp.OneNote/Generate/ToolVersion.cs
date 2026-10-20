using System.Reflection;

namespace Sfp.OneNote.Generate;

/// <summary>The tool version written into the sidecar: the assembly informational version ("1.0.0+commit").</summary>
public static class ToolVersion
{
    private static readonly Lazy<string> CurrentLazy = new(Read);

    /// <summary>The informational version of the entry assembly, else of this assembly, else "unknown".</summary>
    public static string Current => CurrentLazy.Value;

    private static string Read()
    {
        string? entry = Informational(Assembly.GetEntryAssembly());
        string? own = Informational(typeof(ToolVersion).Assembly);
        return entry ?? own ?? "unknown";
    }

    private static string? Informational(Assembly? assembly)
    {
        string? version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version) || !char.IsAsciiDigit(version[0]))
        {
            // Test hosts and other entry assemblies carry their own versions; only a numeric tool version is useful.
            return null;
        }

        return version;
    }
}
