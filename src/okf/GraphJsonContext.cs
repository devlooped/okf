using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devlooped;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphBuilder.KnowledgeGraph))]
[JsonSerializable(typeof(GraphBuilder.Bundle))]
[JsonSerializable(typeof(GraphBuilder.Node))]
[JsonSerializable(typeof(GraphBuilder.Edge))]
[JsonSerializable(typeof(GraphBuilder.NavNode))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class GraphJsonContext : JsonSerializerContext;