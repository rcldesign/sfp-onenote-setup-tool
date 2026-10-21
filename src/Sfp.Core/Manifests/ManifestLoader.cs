using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sfp.Core.Manifests;

/// <summary>Reads the shipped manifest, a manifest file, or a user manifest merged over the shipped one.</summary>
public interface IManifestLoader
{
    /// <summary>Loads templates/sfp-quarterly.json from the templates folder.</summary>
    Manifest LoadShipped(string templatesDir);

    /// <summary>Loads one complete manifest file.</summary>
    Manifest Load(string path);

    /// <summary>
    /// Merges the user manifest over the shipped one key by key at the JSON level (objects merge recursively, arrays and
    /// scalars replace), then deserializes and validates the result. A tweak to one baseline is a three-line file.
    /// </summary>
    Manifest Merge(string shippedPath, string userPath);
}

/// <summary>
/// The one production <see cref="IManifestLoader"/>. Every load ends with the structural checks and the token validation
/// of <see cref="ManifestChecks"/>, so a <see cref="Manifest"/> instance is always internally consistent.
/// </summary>
public sealed class ManifestLoader : IManifestLoader
{
    /// <summary>File name of the shipped manifest inside the templates folder.</summary>
    public const string ShippedFileName = "sfp-quarterly.json";

    /// <summary>Key the manifest files may use anywhere for a free-text note; stripped before deserialization.</summary>
    public const string CommentKey = "$comment";

    private static readonly JsonDocumentOptions NodeOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The serializer settings every JSON file of the tool shares (manifest, sidecar, settings): camelCase names, enums
    /// as camelCase strings, comments and trailing commas tolerated on read, unknown keys rejected, indented output.
    /// The instance is read-only; derive a copy with <c>new JsonSerializerOptions(ManifestLoader.JsonOptions)</c> to vary it.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public Manifest LoadShipped(string templatesDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesDir);
        return Load(Path.Combine(templatesDir, ShippedFileName));
    }

    public Manifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        JsonNode node = ReadNode(path);
        return Materialize(node, path);
    }

    public Manifest Merge(string shippedPath, string userPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shippedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPath);
        JsonNode shipped = ReadNode(shippedPath);
        JsonNode user = ReadNode(userPath);
        JsonNode merged = MergeNodes(shipped, user)
            ?? throw new ManifestException($"Manifest '{userPath}' replaced the whole shipped manifest with null.");
        return Materialize(merged, $"{shippedPath} with {userPath}");
    }

    /// <summary>Parses manifest JSON held in memory; <paramref name="sourceName"/> names it in error messages.</summary>
    public static Manifest Parse(string json, string sourceName = "manifest")
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode node = ParseNode(json, sourceName);
        return Materialize(node, sourceName);
    }

    /// <summary>Serializes a manifest with <see cref="JsonOptions"/>, the inverse of <see cref="Parse"/>.</summary>
    public static string Serialize(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    /// <summary>
    /// Merges <paramref name="user"/> over <paramref name="shipped"/>: when both are objects the result holds every
    /// shipped key with the user's keys merged in recursively; otherwise the user's node (an array, a scalar or null)
    /// replaces the shipped one. Neither input is modified.
    /// </summary>
    public static JsonNode? MergeNodes(JsonNode? shipped, JsonNode? user)
    {
        if (shipped is JsonObject shippedObject && user is JsonObject userObject)
        {
            JsonObject result = new();
            foreach ((string key, JsonNode? value) in shippedObject)
            {
                result[key] = value?.DeepClone();
            }

            foreach ((string key, JsonNode? value) in userObject)
            {
                result[key] = shippedObject.TryGetPropertyValue(key, out JsonNode? shippedValue)
                    ? MergeNodes(shippedValue, value)
                    : value?.DeepClone();
            }

            return result;
        }

        return user?.DeepClone();
    }

    /// <summary>Removes every <c>$comment</c> key from the tree in place.</summary>
    public static void RemoveComments(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove(CommentKey);
                foreach (JsonNode? child in obj.Select(p => p.Value).ToList())
                {
                    RemoveComments(child);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? child in array)
                {
                    RemoveComments(child);
                }

                break;
            default:
                break;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // Files, not HTML: keep "·", "‹" and "›" readable instead of \u escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static JsonNode ReadNode(string path)
    {
        if (!File.Exists(path))
        {
            throw new ManifestException($"Manifest file not found: {path}");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ManifestException($"Manifest '{path}' could not be read: {ex.Message}", ex);
        }

        return ParseNode(json, path);
    }

    private static JsonNode ParseNode(string json, string sourceName)
    {
        try
        {
            // JsonNode builds objects lazily, so a duplicate key or an unpaired surrogate escape would only fail at first
            // use (in MergeNodes, RemoveComments or Deserialize). The walk finds both here, inside this try.
            using (JsonDocument document = JsonDocument.Parse(json, NodeOptions))
            {
                CheckWellFormed(document.RootElement, "$", sourceName);
            }

            return JsonNode.Parse(json, documentOptions: NodeOptions)
                ?? throw new ManifestException($"Manifest '{sourceName}' is empty (JSON null).");
        }
        catch (JsonException ex)
        {
            throw new ManifestException($"Manifest '{sourceName}' is not valid JSON: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new ManifestException($"Manifest '{sourceName}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Rejects what <see cref="JsonNode"/> would only report on first use: a key that appears twice in one object (two
    /// <c>$comment</c> notes included) and a key or string holding an unpaired UTF-16 surrogate escape ("\ud800").
    /// </summary>
    private static void CheckWellFormed(JsonElement element, string path, string sourceName)
    {
        try
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    HashSet<string> keys = new(StringComparer.Ordinal);
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        string key = property.Name;
                        if (!keys.Add(key))
                        {
                            throw new ManifestException(
                                $"Manifest '{sourceName}' is not valid JSON: {path} holds the key '{key}' twice; a key may appear only once in an object.");
                        }

                        CheckWellFormed(property.Value, path + "." + key, sourceName);
                    }

                    break;
                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        CheckWellFormed(item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", sourceName);
                        index++;
                    }

                    break;
                case JsonValueKind.String:
                    _ = element.GetString();
                    break;
                default:
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new ManifestException(
                $"Manifest '{sourceName}' is not valid JSON: {path} holds an unpaired UTF-16 surrogate escape in a key or string: {ex.Message}", ex);
        }
    }

    private static Manifest Materialize(JsonNode node, string sourceName)
    {
        Manifest manifest;
        try
        {
            RemoveComments(node);
            manifest = node.Deserialize<Manifest>(JsonOptions)
                ?? throw new ManifestException($"Manifest '{sourceName}' is empty (JSON null).");
        }
        catch (JsonException ex)
        {
            string where = string.IsNullOrEmpty(ex.Path) ? "" : string.Format(CultureInfo.InvariantCulture, " at {0}", ex.Path);
            throw new ManifestException($"Manifest '{sourceName}' does not match the manifest schema{where}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // ParseNode rejects duplicate keys and unpaired surrogates first; this keeps any other lazy JsonNode failure a
            // ManifestException too.
            throw new ManifestException($"Manifest '{sourceName}' is not valid JSON: {ex.Message}", ex);
        }

        ManifestChecks.Run(manifest, sourceName);
        return manifest;
    }
}
