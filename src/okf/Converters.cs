using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Devlooped;

static class Converters
{
    static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonElement ToJsonElement<T>(T value)
        => WriteElement(value);

    /// <summary>
    /// Converts YAML/extension scalars, maps, and lists without reflection.
    /// Native AOT cannot use <c>JsonSerializer.SerializeToElement(object)</c>.
    /// </summary>
    static JsonElement WriteElement(object? value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            WriteValue(writer, value);

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case JsonElement element:
                element.WriteTo(writer);
                return;
            case string text:
                writer.WriteStringValue(text);
                return;
            case bool flag:
                writer.WriteBooleanValue(flag);
                return;
            case char character:
                writer.WriteStringValue(character.ToString());
                return;
            case Guid id:
                writer.WriteStringValue(id);
                return;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                return;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset);
                return;
            case DateOnly date:
                writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return;
            case TimeOnly time:
                writer.WriteStringValue(time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
                return;
        }

        if (TryWriteNumber(writer, value))
            return;

        if (value is IDictionary map)
        {
            writer.WriteStartObject();
            foreach (DictionaryEntry entry in map)
            {
                var key = entry.Key as string
                    ?? Convert.ToString(entry.Key, CultureInfo.InvariantCulture)
                    ?? "";
                writer.WritePropertyName(key);
                WriteValue(writer, entry.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (value is IEnumerable sequence)
        {
            writer.WriteStartArray();
            foreach (var item in sequence)
                WriteValue(writer, item);

            writer.WriteEndArray();
            return;
        }

        throw new NotSupportedException(
            $"Cannot convert {value.GetType().FullName} to JSON without reflection.");
    }

    static bool TryWriteNumber(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case byte number:
                writer.WriteNumberValue(number);
                return true;
            case sbyte number:
                writer.WriteNumberValue(number);
                return true;
            case short number:
                writer.WriteNumberValue(number);
                return true;
            case ushort number:
                writer.WriteNumberValue(number);
                return true;
            case int number:
                writer.WriteNumberValue(number);
                return true;
            case uint number:
                writer.WriteNumberValue(number);
                return true;
            case long number:
                writer.WriteNumberValue(number);
                return true;
            case ulong number:
                writer.WriteNumberValue(number);
                return true;
            case float number:
                writer.WriteNumberValue(number);
                return true;
            case double number:
                writer.WriteNumberValue(number);
                return true;
            case decimal number:
                writer.WriteNumberValue(number);
                return true;
            default:
                return false;
        }
    }

    public static Dictionary<string, JsonElement>? ToExtensionData(IReadOnlyDictionary<string, object?>? extensionData)
    {
        if (extensionData is null || extensionData.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in extensionData)
        {
            if (value is null)
            {
                continue;
            }

            result[key] = ToJsonElement(value);
        }

        return result.Count > 0 ? result : null;
    }

    public static Dictionary<string, string>? ParseKeyValue(string[]? args)
    {
        Dictionary<string, string>? result = null;

        if (args is { Length: > 0 })
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in args)
            {
                var equalsIndex = entry.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var name = entry[..equalsIndex];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                map[name] = entry[(equalsIndex + 1)..];
            }

            if (map.Count > 0)
            {
                result = map;
            }
        }

        return result;
    }
}