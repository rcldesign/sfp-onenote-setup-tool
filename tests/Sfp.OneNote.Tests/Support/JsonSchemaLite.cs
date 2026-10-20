using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sfp.OneNote.Tests.Support;

/// <summary>
/// A small JSON Schema (draft 7) checker over JsonDocument for the sidecar schema: type, required, properties,
/// additionalProperties, propertyNames, items, enum, minimum, maximum, minLength, maxLength, pattern and local $ref.
/// Formats are not checked (date-time and date are covered by patterns where it matters). No schema package is used.
/// </summary>
internal static class JsonSchemaLite
{
    /// <summary>Every violation as "path: message"; empty when the instance validates.</summary>
    public static IReadOnlyList<string> Validate(string schemaJson, string instanceJson)
    {
        using JsonDocument schema = JsonDocument.Parse(schemaJson);
        using JsonDocument instance = JsonDocument.Parse(instanceJson);
        List<string> problems = new();
        Check(schema.RootElement, schema.RootElement, instance.RootElement, "$", problems);
        return problems;
    }

    public static void AssertValid(string schemaJson, string instanceJson)
    {
        IReadOnlyList<string> problems = Validate(schemaJson, instanceJson);
        problems.Should().BeEmpty("the document must validate against the schema");
    }

    private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> problems)
    {
        if (schema.ValueKind == JsonValueKind.True)
        {
            return;
        }

        if (schema.ValueKind == JsonValueKind.False)
        {
            problems.Add($"{path}: not allowed");
            return;
        }

        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            Check(root, Resolve(root, reference.GetString()!), value, path, problems);
            return;
        }

        if (schema.TryGetProperty("type", out JsonElement type) && !TypeMatches(type, value))
        {
            problems.Add($"{path}: expected type {type} but found {value.ValueKind}");
            return;
        }

        if (schema.TryGetProperty("enum", out JsonElement allowed) && !allowed.EnumerateArray().Any(a => a.GetRawText() == value.GetRawText()))
        {
            problems.Add($"{path}: value {value.GetRawText()} is not one of {allowed}");
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(root, schema, value, path, problems);
                break;
            case JsonValueKind.Array:
                if (schema.TryGetProperty("items", out JsonElement items))
                {
                    int i = 0;
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        Check(root, items, item, $"{path}[{i++}]", problems);
                    }
                }

                break;
            case JsonValueKind.String:
                CheckString(schema, value.GetString()!, path, problems);
                break;
            case JsonValueKind.Number:
                CheckNumber(schema, value.GetDouble(), path, problems);
                break;
            default:
                break;
        }
    }

    private static void CheckObject(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> problems)
    {
        if (schema.TryGetProperty("required", out JsonElement required))
        {
            foreach (JsonElement name in required.EnumerateArray())
            {
                if (!value.TryGetProperty(name.GetString()!, out _))
                {
                    problems.Add($"{path}: missing required property '{name.GetString()}'");
                }
            }
        }

        JsonElement properties = schema.TryGetProperty("properties", out JsonElement p) ? p : default;
        bool hasAdditional = schema.TryGetProperty("additionalProperties", out JsonElement additional);
        bool hasNames = schema.TryGetProperty("propertyNames", out JsonElement names);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string childPath = path + "." + property.Name;
            if (hasNames)
            {
                CheckString(names, property.Name, childPath + " (name)", problems);
            }

            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out JsonElement propertySchema))
            {
                Check(root, propertySchema, property.Value, childPath, problems);
            }
            else if (hasAdditional)
            {
                Check(root, additional, property.Value, childPath, problems);
            }
        }
    }

    private static void CheckString(JsonElement schema, string text, string path, List<string> problems)
    {
        if (schema.TryGetProperty("minLength", out JsonElement min) && text.Length < min.GetInt32())
        {
            problems.Add($"{path}: shorter than {min.GetInt32()}");
        }

        if (schema.TryGetProperty("maxLength", out JsonElement max) && text.Length > max.GetInt32())
        {
            problems.Add($"{path}: longer than {max.GetInt32()}");
        }

        if (schema.TryGetProperty("pattern", out JsonElement pattern) && !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            problems.Add($"{path}: '{text}' does not match {pattern.GetString()}");
        }
    }

    private static void CheckNumber(JsonElement schema, double number, string path, List<string> problems)
    {
        if (schema.TryGetProperty("minimum", out JsonElement min) && number < min.GetDouble())
        {
            problems.Add($"{path}: {number.ToString(CultureInfo.InvariantCulture)} is below {min.GetRawText()}");
        }

        if (schema.TryGetProperty("maximum", out JsonElement max) && number > max.GetDouble())
        {
            problems.Add($"{path}: {number.ToString(CultureInfo.InvariantCulture)} is above {max.GetRawText()}");
        }
    }

    private static bool TypeMatches(JsonElement type, JsonElement value)
    {
        if (type.ValueKind == JsonValueKind.Array)
        {
            return type.EnumerateArray().Any(t => TypeMatches(t, value));
        }

        return type.GetString() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            _ => false,
        };
    }

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Only local $ref values are supported: " + reference);
        }

        JsonElement current = root;
        foreach (string segment in reference[2..].Split('/'))
        {
            current = current.GetProperty(segment);
        }

        return current;
    }
}
