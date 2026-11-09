using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oluso.Core.Locale;

/// <summary>
/// A string value that can be either a plain string or a dictionary of
/// locale-keyed translations. Deserialises transparently from both shapes
/// so step config can migrate from <c>"title": "Hello"</c> to
/// <c>"title": { "en": "Hello", "fr": "Bonjour" }</c> without breaking
/// existing policies.
/// </summary>
[JsonConverter(typeof(LocalizedStringConverter))]
public sealed class LocalizedString
{
    private readonly string? _plain;
    private readonly IReadOnlyDictionary<string, string>? _translations;

    public LocalizedString(string value)
    {
        _plain = value;
    }

    public LocalizedString(IReadOnlyDictionary<string, string> translations)
    {
        _translations = translations;
    }

    public bool IsLocalized => _translations is { Count: > 0 };

    /// <summary>
    /// All locale codes that have a value. Empty when the string is plain.
    /// </summary>
    public IEnumerable<string> Locales => _translations?.Keys ?? Enumerable.Empty<string>();

    /// <summary>
    /// Resolves the best-matching value for the given locale, walking the
    /// culture hierarchy (e.g. fr-FR → fr → en) just like resource bundles.
    /// Returns the plain value when no translations exist.
    /// </summary>
    public string Resolve(string? locale, string fallback = "en")
    {
        if (_translations == null || _translations.Count == 0)
            return _plain ?? string.Empty;

        if (!string.IsNullOrEmpty(locale))
        {
            foreach (var candidate in CandidateTags(locale))
            {
                if (_translations.TryGetValue(candidate, out var value))
                    return value;
            }
        }

        if (_translations.TryGetValue(fallback, out var fb))
            return fb;

        return _translations.Values.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Resolves using a <see cref="CultureInfo"/> instance.
    /// </summary>
    public string Resolve(CultureInfo culture) => Resolve(culture.Name);

    public override string ToString() => Resolve((string?)null);

    public static implicit operator LocalizedString(string value) => new(value);

    private static IEnumerable<string> CandidateTags(string locale)
    {
        CultureInfo? culture;
        try { culture = CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { return new[] { locale }; }

        return Walk(culture);

        static IEnumerable<string> Walk(CultureInfo start)
        {
            for (var cursor = start; cursor != null && cursor != CultureInfo.InvariantCulture; cursor = cursor.Parent)
                yield return cursor.Name;
        }
    }
}

public sealed class LocalizedStringConverter : JsonConverter<LocalizedString>
{
    public override LocalizedString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new LocalizedString(reader.GetString()!);

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options)!;
            return new LocalizedString(dict);
        }

        throw new JsonException($"Cannot deserialize LocalizedString from {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, LocalizedString value, JsonSerializerOptions options)
    {
        if (!value.IsLocalized)
        {
            writer.WriteStringValue(value.Resolve((string?)null));
            return;
        }

        writer.WriteStartObject();
        foreach (var locale in value.Locales)
        {
            writer.WriteString(locale, value.Resolve(locale));
        }
        writer.WriteEndObject();
    }
}
