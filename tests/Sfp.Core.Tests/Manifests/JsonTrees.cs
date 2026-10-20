using System.Globalization;
using System.Text.Json.Nodes;
using Sfp.Core.Manifests;

namespace Sfp.Core.Tests.Manifests;

/// <summary>Compares JSON trees for the manifest tests, ignoring $comment keys and formatting.</summary>
internal static class JsonTrees
{
    /// <summary>Every path at which <paramref name="expected"/> holds something <paramref name="actual"/> lacks or differs on.</summary>
    public static IReadOnlyList<string> MissingOrDifferent(JsonNode? expected, JsonNode? actual)
    {
        List<string> differences = new();
        Compare(expected, actual, "$", differences, symmetric: false);
        return differences;
    }

    /// <summary>Every path at which the two trees differ, in either direction.</summary>
    public static IReadOnlyList<string> Differences(JsonNode? a, JsonNode? b)
    {
        List<string> differences = new();
        Compare(a, b, "$", differences, symmetric: true);
        return differences;
    }

    private static void Compare(JsonNode? expected, JsonNode? actual, string path, List<string> differences, bool symmetric)
    {
        switch (expected)
        {
            case JsonObject eo when actual is JsonObject ao:
                foreach ((string key, JsonNode? value) in eo)
                {
                    if (key == ManifestLoader.CommentKey)
                    {
                        continue;
                    }

                    if (!ao.TryGetPropertyValue(key, out JsonNode? other))
                    {
                        differences.Add($"{path}.{key} (missing)");
                        continue;
                    }

                    Compare(value, other, $"{path}.{key}", differences, symmetric);
                }

                if (symmetric)
                {
                    foreach ((string key, JsonNode? _) in ao)
                    {
                        if (key != ManifestLoader.CommentKey && !eo.ContainsKey(key))
                        {
                            differences.Add($"{path}.{key} (extra)");
                        }
                    }
                }

                break;
            case JsonArray ea when actual is JsonArray aa:
                if (ea.Count != aa.Count)
                {
                    differences.Add($"{path} (array length {ea.Count} vs {aa.Count})");
                    break;
                }

                for (int i = 0; i < ea.Count; i++)
                {
                    Compare(ea[i], aa[i], $"{path}[{i.ToString(CultureInfo.InvariantCulture)}]", differences, symmetric);
                }

                break;
            case null when actual is null:
                break;
            case JsonValue ev when actual is JsonValue av:
                if (!ValuesEqual(ev, av))
                {
                    differences.Add($"{path} ({ev.ToJsonString()} vs {av.ToJsonString()})");
                }

                break;
            default:
                differences.Add($"{path} (kind {Kind(expected)} vs {Kind(actual)})");
                break;
        }
    }

    private static bool ValuesEqual(JsonValue a, JsonValue b)
    {
        if (a.TryGetValue(out double da) && b.TryGetValue(out double db))
        {
            return Math.Abs(da - db) < 1e-9;
        }

        return string.Equals(a.ToJsonString(), b.ToJsonString(), StringComparison.Ordinal);
    }

    private static string Kind(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        _ => "value",
    };
}
