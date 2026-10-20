using System.Text.Json;
using System.Text.Json.Serialization;
using Sfp.Core.Manifests;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>
/// Loads templates/sfp-quarterly.json straight into the Manifest record for tests. The production loader (M1.2) adds
/// merge-over and token checks; the calendar tests only need the slot geometry, so plain deserialization is enough.
/// </summary>
public static class TestManifest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly Lazy<Manifest> Loaded = new(() =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(TestPaths.ManifestPath), Options)
        ?? throw new InvalidOperationException("The manifest deserialized to null."));

    public static Manifest Load() => Loaded.Value;
}
