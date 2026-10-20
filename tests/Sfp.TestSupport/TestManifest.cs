using System.Text.Json;
using System.Text.Json.Serialization;
using Sfp.Core.Manifests;

namespace Sfp.TestSupport;

/// <summary>
/// Reads templates/sfp-quarterly.json into the <see cref="Manifest"/> record for tests. The record mirrors the JSON one
/// to one (camelCase names, enums as lower-case strings), so System.Text.Json can load it directly; the production
/// loader with merge-over and token validation lives in Sfp.Core (M1.2) and is the one the application uses.
/// </summary>
public static class TestManifest
{
    private static readonly Lazy<Manifest> ShippedLazy = new(() => Load(TestPaths.ManifestPath));

    /// <summary>Serializer settings that match the manifest file's conventions.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The shipped manifest, loaded once per process.</summary>
    public static Manifest Shipped => ShippedLazy.Value;

    /// <summary>Loads a manifest file.</summary>
    public static Manifest Load(string path) =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Manifest is empty: " + path);
}
