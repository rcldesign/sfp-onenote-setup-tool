using System.Text.Json;
using System.Text.Json.Serialization;
using Sfp.Core.Manifests;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Support;

/// <summary>
/// Loads templates/sfp-quarterly.json straight into the Manifest record for these tests, so every coordinate the
/// fixtures use comes from the manifest. (The production loader with merge-over is the M1.2 card in Sfp.Core.)
/// </summary>
internal static class TestManifest
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly Lazy<Manifest> Instance = new(() =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(TestPaths.ManifestPath), Options)
        ?? throw new InvalidOperationException("The manifest deserialized to null."));

    public static Manifest Load() => Instance.Value;
}
