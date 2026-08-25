using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Serialization;

/// <summary>
/// Canonical JSON serialization for Harvest ledger artifacts.
///
/// Rules (per Task 4 spec):
///   - UTF-8 encoding.
///   - camelCase property names.
///   - Enum values serialized as camelCase strings.
///   - UTC DateTimes serialized as round-trip ISO 8601 ("O" format).
///   - Dictionary keys sorted lexicographically before serialization.
///   - No indentation, no trailing whitespace.
/// </summary>
public static class HarvestJson
{
    public static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented               = false,
            DefaultIgnoreCondition      = JsonIgnoreCondition.Never,
        };

        // Enums as camelCase strings
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // Sort dictionary keys lexicographically
        opts.Converters.Add(new SortedDictionaryConverterFactory());

        return opts;
    }

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize<T>(utf8, Options);

    // ── Sorted-dictionary converter factory ──────────────────────────────

    private sealed class SortedDictionaryConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsGenericType) return false;
            var def = typeToConvert.GetGenericTypeDefinition();
            return def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>);
        }

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var args        = typeToConvert.GetGenericArguments();
            var keyType     = args[0];
            var valueType   = args[1];
            var converterType = typeof(SortedDictionaryConverter<,>).MakeGenericType(keyType, valueType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
    }

    private sealed class SortedDictionaryConverter<TKey, TValue> : JsonConverter<Dictionary<TKey, TValue>>
        where TKey : notnull
    {
        public override Dictionary<TKey, TValue> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(ref reader, options)
               ?? new Dictionary<TKey, TValue>();

        public override void Write(
            Utf8JsonWriter writer, Dictionary<TKey, TValue> value, JsonSerializerOptions options)
        {
            // Write as a sorted sequence of key-value pairs
            writer.WriteStartObject();
            foreach (var kvp in value.OrderBy(k => k.Key?.ToString(), StringComparer.Ordinal))
            {
                var keyString = options.PropertyNamingPolicy?.ConvertName(kvp.Key.ToString()!)
                                ?? kvp.Key.ToString()!;
                writer.WritePropertyName(keyString);
                JsonSerializer.Serialize(writer, kvp.Value, options);
            }
            writer.WriteEndObject();
        }
    }
}
