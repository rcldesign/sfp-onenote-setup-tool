using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sfp.Core.Manifests;

/// <summary>
/// Finds members of a manifest that hold null although the record declares them non-nullable, and enum members that
/// hold a value the enum does not define. System.Text.Json lets two such nulls through: an explicit <c>null</c> in a
/// user manifest ({"slots": {"date": null}}, which <c>required</c> and [JsonRequired] do not reject because the key is
/// present) and a null entry in a list or dictionary. It also reads "align": 7, or the string "7", as an undefined
/// TextAlign. Without this walk such a manifest loads and fails later far from the cause. (A member that the file leaves
/// out is a deserialization error: the records mark every member required.)
/// </summary>
/// <remarks>
/// The walk is reflection over every public settable property that the JSON maps (not [JsonIgnore]), reading the C#
/// nullable annotations, and it recurses into list entries, dictionary values and every record type of this assembly,
/// so a member added to the manifest later is covered without a change here. Members declared nullable (a
/// <c>string?</c> title, a <c>MonthRule?</c>) may hold null. The first problem found is reported with its JSON path
/// ("$.slots.date.fields[0].name").
/// </remarks>
internal static class ManifestNullChecks
{
    private static readonly Assembly ManifestAssembly = typeof(Manifest).Assembly;

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<Member>> MembersByType = new();

    // NullabilityInfoContext is not thread-safe; it is only used inside Describe, under this lock.
    private static readonly object NullabilityGate = new();

    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>
    /// Throws <see cref="ManifestException"/> naming the JSON path of the first non-nullable member that holds null, or
    /// of the first enum member that holds an undefined value.
    /// </summary>
    public static void Run(Manifest manifest, string source)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(source);
        Visit(manifest, "$", source);
    }

    private static void Visit(object owner, string path, string source)
    {
        foreach (Member member in MembersByType.GetOrAdd(owner.GetType(), Describe))
        {
            object? value = member.Property.GetValue(owner);
            Check(value, member.Property.PropertyType, member.Nullability, path + "." + member.JsonName, source);
        }
    }

    /// <param name="value">The value found.</param>
    /// <param name="declared">The declared type of the member, list entry or dictionary value.</param>
    /// <param name="nullability">Its nullable annotation, or null when the shape gives none (null is then allowed).</param>
    /// <param name="path">JSON path of the value.</param>
    /// <param name="source">The manifest's name for the message.</param>
    private static void Check(object? value, Type declared, NullabilityInfo? nullability, string path, string source)
    {
        if (value is null)
        {
            if (!declared.IsValueType && nullability?.ReadState == NullabilityState.NotNull)
            {
                throw new ManifestException($"Manifest '{source}': {path} must not be null.");
            }

            return;
        }

        Type runtime = value.GetType();
        if (runtime.IsEnum)
        {
            if (!Enum.IsDefined(runtime, value))
            {
                string names = string.Join(", ", Enum.GetNames(runtime).Select(JsonNamingPolicy.CamelCase.ConvertName));
                throw new ManifestException(
                    $"Manifest '{source}': {path} holds {Convert.ToString(value, CultureInfo.InvariantCulture)}, which is not one of {names}.");
            }

            return;
        }

        if (value is string || runtime.IsPrimitive)
        {
            return;
        }

        if (DictionaryValueType(declared) is Type valueType)
        {
            NullabilityInfo? valueNullability = nullability?.GenericTypeArguments.Length == 2 ? nullability.GenericTypeArguments[1] : null;
            foreach ((string key, object? entry) in Entries((IEnumerable)value))
            {
                Check(entry, valueType, valueNullability, path + "." + key, source);
            }

            return;
        }

        if (value is IEnumerable items)
        {
            (Type elementType, NullabilityInfo? elementNullability) = Element(declared, nullability);
            int index = 0;
            foreach (object? item in items)
            {
                Check(item, elementType, elementNullability, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", source);
                index++;
            }

            return;
        }

        if (runtime.Assembly == ManifestAssembly)
        {
            Visit(value, path, source);
        }
    }

    /// <summary>The members System.Text.Json maps for a type: public, readable and settable (set or init), not [JsonIgnore].</summary>
    private static IReadOnlyList<Member> Describe(Type type)
    {
        lock (NullabilityGate)
        {
            List<Member> members = new();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetMethod?.IsPublic != true || property.SetMethod?.IsPublic != true)
                {
                    continue;
                }

                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
                {
                    continue;
                }

                string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                members.Add(new Member(property, name, Nullability.Create(property)));
            }

            return members;
        }
    }

    /// <summary>The value type of a dictionary-shaped declared type (IReadOnlyDictionary, IDictionary or Dictionary), else null.</summary>
    private static Type? DictionaryValueType(Type declared)
    {
        if (!declared.IsGenericType || declared.GenericTypeArguments.Length != 2)
        {
            return null;
        }

        Type definition = declared.GetGenericTypeDefinition();
        bool isDictionary = definition == typeof(IReadOnlyDictionary<,>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(Dictionary<,>);
        return isDictionary ? declared.GenericTypeArguments[1] : null;
    }

    /// <summary>The entries of a dictionary as (key, value), read from the KeyValuePair every generic dictionary enumerates.</summary>
    private static IEnumerable<(string Key, object? Value)> Entries(IEnumerable dictionary)
    {
        foreach (object? pair in dictionary)
        {
            if (pair is null)
            {
                continue;
            }

            Type pairType = pair.GetType();
            object? key = pairType.GetProperty(nameof(KeyValuePair<string, object>.Key))?.GetValue(pair);
            object? value = pairType.GetProperty(nameof(KeyValuePair<string, object>.Value))?.GetValue(pair);
            yield return (Convert.ToString(key, CultureInfo.InvariantCulture) ?? "", value);
        }
    }

    /// <summary>The entry type and its annotation for an array or a one-argument generic collection; object and none otherwise.</summary>
    private static (Type Type, NullabilityInfo? Nullability) Element(Type declared, NullabilityInfo? nullability)
    {
        if (declared.IsArray)
        {
            return (declared.GetElementType() ?? typeof(object), nullability?.ElementType);
        }

        if (declared.IsGenericType && declared.GenericTypeArguments.Length == 1)
        {
            NullabilityInfo? element = nullability?.GenericTypeArguments.Length == 1 ? nullability.GenericTypeArguments[0] : null;
            return (declared.GenericTypeArguments[0], element);
        }

        return (typeof(object), null);
    }

    private sealed record Member(PropertyInfo Property, string JsonName, NullabilityInfo Nullability);
}
