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
[JsonSerializable(typeof(ActorEvent))]
[JsonSerializable(typeof(UsageWindow))]
[JsonSerializable(typeof(SourceEntry))]
[JsonSerializable(typeof(ComputationParameter))]
[JsonSerializable(typeof(ExecutorContract))]
[JsonSerializable(typeof(AttesterContract))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<ActorEvent>))]
[JsonSerializable(typeof(List<SourceEntry>))]
[JsonSerializable(typeof(List<ComputationParameter>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class GraphJsonContext : JsonSerializerContext;