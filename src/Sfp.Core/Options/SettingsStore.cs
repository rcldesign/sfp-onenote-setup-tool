using System.Text.Json;
using System.Text.Json.Serialization;
using Sfp.Core.Manifests;

namespace Sfp.Core.Options;

/// <summary>
/// The production <see cref="ISettingsStore"/>: last-used values as JSON at %APPDATA%\sfp2onenote\settings.json
/// (the path is injectable for tests). A missing or corrupt file loads as the defaults, unknown keys are ignored so a
/// newer file still loads, a null member loads as its default, and every save writes a temporary file first and then
/// moves it into place. When <see cref="Load(out string?)"/> could not use the file as it is (corrupt or unreadable) or
/// the file comes from a newer version (whose extra settings a save would drop), the first save after it copies the
/// file to <see cref="BackupPath"/> before replacing it, and replaces nothing when that copy fails.
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

    /// <summary>Where the file is copied before the first save replaces a file the last load could not use as it is: settings.json.bak.</summary>
    public string BackupPath => Path + ".bak";

    /// <summary>True when the next <see cref="Save"/> first copies the file to <see cref="BackupPath"/> (set by the last load).</summary>
    public bool BackupPending { get; private set; }

    public AppSettings Load() => Load(out _);

    /// <summary>
    /// Loads the settings; <paramref name="problem"/> says why the defaults were returned instead of the file's values
    /// (a corrupt file, an unreadable file, a newer version's file this one cannot read), or is null when the file
    /// loaded or does not exist yet. A newer version's file that does load reports no problem but, like a problem, makes
    /// the next save keep a copy first (<see cref="BackupPending"/>).
    /// </summary>
    public AppSettings Load(out string? problem)
    {
        problem = null;
        BackupPending = false;
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }

        string json;
        try
        {
            json = File.ReadAllText(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"Settings file {Path} could not be read; defaults are used. {ex.Message}";
            BackupPending = true;
            return new AppSettings();
        }

        try
        {
            AppSettings settings = Deserialize(json) ?? new AppSettings();
            BackupPending = settings.SchemaVersion > AppSettings.CurrentSchemaVersion;
            return settings;
        }
        catch (JsonException ex)
        {
            problem = SchemaVersionOf(json) > AppSettings.CurrentSchemaVersion
                ? $"Settings file {Path} was written by a newer version of SFP to OneNote and cannot be read by this one; defaults are used. {ex.Message}"
                : $"Settings file {Path} is not valid; defaults are used. {ex.Message}";
            BackupPending = true;
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string json = Serialize(settings);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (BackupPending)
        {
            // A failed copy throws before anything is replaced, and the next save tries again.
            if (File.Exists(Path))
            {
                File.Copy(Path, BackupPath, overwrite: true);
            }

            BackupPending = false;
        }

        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, Path, overwrite: true);
    }

    /// <summary>The JSON text a save writes, for comparison without touching the disk.</summary>
    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, JsonOptions);
    }

    /// <summary>
    /// Parses settings JSON with null members replaced by their defaults (<see cref="AppSettings.WithDefaultsForNulls"/>);
    /// null for the JSON literal null. Throws <see cref="JsonException"/> on malformed input.
    /// </summary>
    public static AppSettings? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)?.WithDefaultsForNulls();
    }

    /// <summary>The file's schemaVersion when the text is a JSON object holding a whole number there, else null.</summary>
    private static int? SchemaVersionOf(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("schemaVersion", out JsonElement version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out int value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
