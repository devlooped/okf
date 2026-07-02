using System.Text.Json.Serialization;
using SharpYaml.Serialization;

namespace okf;

// SharpYaml extension data only supports Dictionary<string, object> values.
sealed class ConceptDocumentYaml
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? ExtensionDataYaml { get; set; }
}

[YamlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[YamlSerializable(typeof(ConceptDocumentYaml))]
[YamlSerializable(typeof(Dictionary<string, object?>))]
[YamlSerializable(typeof(List<string>))]
partial class ConceptDocumentYamlContext : YamlSerializerContext;