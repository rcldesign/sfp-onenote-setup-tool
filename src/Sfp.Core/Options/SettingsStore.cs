using System.Text.Json;
using System.Text.Json.Serialization;
using Sfp.Core.Manifests;

namespace Sfp.Core.Options;

/// <summary>
/// The production <see cref="ISettingsStore"/>: last-used values as JSON at %APPDATA%\sfp2onenote\settings.json
/// (the path is injectable for tests). A missing or corrupt file loads as the defaults, unknown keys are ignored so a
/// newer file still loads, and every save writes a temporary file first and then moves it into place.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    /// <summary>Folder name under %APPDATA%.</summary>
    public const string FolderName = "sfp2onenote";

    /// <summary>File name inside the folder.</summary>
    public const string FileName = "settings.json";

    /// <summary>
    /// The manifest's JSON conventions (camelCase keys, camelCase enum strings, comments tolerated) with unknown keys
    /// skipped instead of rejected, so a file written by a newer version still loads.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(ManifestLoader.JsonOptions)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SettingsStore()
        : this(DefaultPath)
    {
    }

    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>%APPDATA%\sfp2onenote\settings.json.</summary>
    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName, FileName);

    public string Path { get; }

    public AppSettings Load() => Load(out _);

    /// <summary>
    /// Loads the settings; <paramref name="problem"/> says why the defaults were returned instead of the file's values
    /// (a corrupt file, an unreadable file), or is null when the file loaded or does not exist yet.
    /// </summary>
    public AppSettings Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(Path);
            return Deserialize(json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            problem = $"Settings file {Path} is not valid; defaults are used. {ex.Message}";
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"Settings file {Path} could not be read; defaults are used. {ex.Message}";
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, Serialize(settings));
        File.Move(tmp, Path, overwrite: true);
    }

    /// <summary>The JSON text a save writes, for comparison without touching the disk.</summary>
    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, JsonOptions);
    }

    /// <summary>Parses settings JSON; null for the JSON literal null. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static AppSettings? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
    }
}
