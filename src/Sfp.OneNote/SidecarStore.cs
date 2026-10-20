using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sfp.OneNote;

/// <summary>Reads and writes sfp-state.json beside a notebook folder.</summary>
public interface ISidecarStore
{
    /// <summary>The sidecar path for a notebook folder: the file sits next to the folder, named sfp-state.json inside a folder of the notebook's name plus ".sfp".</summary>
    string PathFor(string notebookFolder);

    Sidecar? Load(string path);

    void Save(string path, Sidecar sidecar);
}

/// <summary>JSON file implementation. Lenient on unknown keys so an older tool can read a newer file.</summary>
public sealed class SidecarStore : ISidecarStore
{
    /// <summary>Serializer options shared with any other reader of the sidecar.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The sidecar lives beside the notebook folder as "{NotebookFolder}.sfp-state.json" so OneNote never sees it inside the notebook.</summary>
    public string PathFor(string notebookFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookFolder);
        string trimmed = notebookFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        string parent = System.IO.Path.GetDirectoryName(trimmed) ?? trimmed;
        string name = System.IO.Path.GetFileName(trimmed);
        return System.IO.Path.Combine(parent, name + "." + Sidecar.FileName);
    }

    public Sidecar? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Sidecar>(stream, JsonOptions);
    }

    public void Save(string path, Sidecar sidecar)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tmp = path + ".tmp";
        using (FileStream stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, sidecar, JsonOptions);
        }

        File.Move(tmp, path, overwrite: true);
    }
}
