using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Reads step settings and journey data into typed values. They're written by admins and
/// other systems, so names match without regard to case, numbers and booleans are also
/// accepted as text, a list of text may be comma-separated text, and a list or map is
/// read entry by entry: an entry that's empty or
/// can't be read is left out (and reported) rather than losing the whole setting.
/// </summary>
public static class StepValueReader
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new TextBooleanConverter());
        return options;
    }

    /// <param name="onProblem">Told about anything left out or unreadable.</param>
    public static T? Read<T>(object? value, T? defaultValue = default, Action<string>? onProblem = null)
    {
        if (value is T typed) return typed;
        if (value == null) return defaultValue;

        try
        {
            JsonElement element;
            switch (value)
            {
                case JsonElement json:
                    element = json;
                    break;
                // A section rebuilt in memory (e.g. once a secret in it is decrypted).
                case IEnumerable and not string:
                    element = JsonSerializer.SerializeToElement(value);
                    break;
                default:
                    return ConvertScalar(value, defaultValue);
            }

            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return defaultValue;
            return (T?)ReadElement(element, typeof(T), onProblem) ?? defaultValue;
        }
        catch (Exception ex) when (ex is JsonException or InvalidCastException or FormatException or NotSupportedException or OverflowException)
        {
            onProblem?.Invoke(ex.Message);
            return defaultValue;
        }
    }

    private static T? ConvertScalar<T>(object value, T? defaultValue)
    {
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (target == typeof(string)) return (T)(object)Convert.ToString(value, CultureInfo.InvariantCulture)!;
        if (target == typeof(bool) && value is string text) return TextBooleanConverter.TryParse(text, out var flag) ? (T)(object)flag : defaultValue;
        return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    private static object? ReadElement(JsonElement element, Type type, Action<string>? onProblem)
    {
        if (type == typeof(string) && element.ValueKind != JsonValueKind.String)
        {
            return JsonLookup.AsString(element);
        }

        // A list of text may also be written as comma-separated text ("acme.com, beta.io").
        if (ListItemType(type) == typeof(string) && element.ValueKind == JsonValueKind.String)
        {
            return (element.GetString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        if (ListItemType(type) is { } itemType && element.ValueKind == JsonValueKind.Array)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Null && TryRead(item, itemType, onProblem, $"entry {index}", out var read))
                {
                    list.Add(read);
                }
                else if (item.ValueKind == JsonValueKind.Null)
                {
                    onProblem?.Invoke($"entry {index} is empty and was left out");
                }
                index++;
            }
            return list;
        }

        if (DictionaryValueType(type) is { } valueType && element.ValueKind == JsonValueKind.Object)
        {
            var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    onProblem?.Invoke($"'{property.Name}' is empty and was left out");
                    continue;
                }
                if (TryRead(property.Value, valueType, onProblem, $"'{property.Name}'", out var read))
                {
                    dictionary[property.Name] = read;
                }
            }
            return dictionary;
        }

        return JsonSerializer.Deserialize(element, type, Options);
    }

    private static bool TryRead(JsonElement element, Type type, Action<string>? onProblem, string what, out object? value)
    {
        try
        {
            value = ReadElement(element, type, onProblem);
            return value != null || !type.IsValueType;
        }
        catch (Exception ex) when (ex is JsonException or InvalidCastException or FormatException or NotSupportedException or OverflowException)
        {
            onProblem?.Invoke($"{what} couldn't be read and was left out: {ex.Message}");
            value = null;
            return false;
        }
    }

    private static Type? ListItemType(Type type)
    {
        if (type.IsArray || !type.IsGenericType) return null;
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IEnumerable<>)
            || definition == typeof(IReadOnlyList<>) || definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>)
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static Type? DictionaryValueType(Type type)
    {
        if (!type.IsGenericType) return null;
        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();
        return (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            && arguments[0] == typeof(string)
            ? arguments[1]
            : null;
    }

    /// <summary>Booleans written as JSON booleans, "true"/"false", or 1/0.</summary>
    private sealed class TextBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when TryParse(reader.GetString(), out var parsed) => parsed,
            JsonTokenType.Number when reader.TryGetInt64(out var number) && number is 0 or 1 => number == 1,
            _ => throw new JsonException($"'{(reader.TokenType == JsonTokenType.String ? reader.GetString() : reader.TokenType.ToString())}' is not true or false"),
        };

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);

        public static bool TryParse(string? text, out bool value)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case "true" or "1":
                    value = true;
                    return true;
                case "false" or "0":
                    value = false;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }
    }
}
