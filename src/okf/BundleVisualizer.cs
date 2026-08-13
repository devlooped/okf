using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devlooped;

public sealed record VisualizationStats(int Concepts, int Edges, int Bytes);

public static partial class BundleVisualizer
{
    static readonly Dictionary<string, string> TypePalette = new(StringComparer.Ordinal)
    {
        ["BigQuery Dataset"] = "#8b5cf6",
        ["BigQuery Table"] = "#3b82f6",
        ["Reference"] = "#10b981",
        ["Attested Computation"] = "#f59e0b",
        ["Metric"] = "#ec4899",
    };

    const string DefaultNodeColor = "#94a3b8";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static VisualizationStats Generate(string bundleRoot, string outPath, string? bundleName = null)
    {
        // Delegate to the common in-memory graph model (same as `graph` command)
        var graph = GraphBuilder.Build(bundleRoot, includeBody: true);
        return Generate(graph, outPath, bundleName);
    }

    /// <summary>
    /// Generate visualization from an in-memory KnowledgeGraph (supports both
    /// directory builds and loaded okf.json graphs).
    /// </summary>
    public static VisualizationStats Generate(GraphBuilder.KnowledgeGraph graph, string outPath, string? displayName = null)
    {
        outPath = Path.GetFullPath(outPath);

        var name = displayName ?? "";

        var vizData = BuildVizData(graph);

        var html = ThisAssembly.Resources.Google.viz_template.Text
            .Replace("/*__VIZ_CSS__*/", ThisAssembly.Resources.Google.viz_styles.Text, StringComparison.Ordinal)
            .Replace("/*__VIZ_JS__*/", ThisAssembly.Resources.Google.viz_script.Text, StringComparison.Ordinal)
            .Replace("__BUNDLE_NAME__", JsonSerializer.Serialize(name, JsonOptions), StringComparison.Ordinal)
            .Replace("__BUNDLE_DATA__", JsonSerializer.Serialize(vizData, JsonOptions), StringComparison.Ordinal);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, html, Encoding.UTF8);

        return new VisualizationStats(graph.Nodes.Count, graph.Edges.Count, Encoding.UTF8.GetByteCount(html));
    }

    static GraphData BuildVizData(GraphBuilder.KnowledgeGraph graph)
    {
        var vizNodes = graph.Nodes
            .Select(n => new NodeElement { Data = ToVizNodeData(n) })
            .ToList();

        var vizEdges = graph.Edges
            .Select(e => new EdgeElement
            {
                Data = new EdgeData
                {
                    Id = e.Id,
                    Source = e.Source,
                    Target = e.Target,
                },
            })
            .ToList();

        var bodies = graph.Nodes
            .Where(n => !string.IsNullOrEmpty(n.Body))
            .ToDictionary(n => n.Id, n => n.Body!, StringComparer.Ordinal);

        var types = graph.Nodes
            .Select(n => n.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return new GraphData
        {
            Nodes = vizNodes,
            Edges = vizEdges,
            Bodies = bodies,
            Types = types,
            Palette = TypePalette,
        };
    }

    static NodeData ToVizNodeData(GraphBuilder.Node n)
    {
        var color = TypePalette.GetValueOrDefault(n.Type, DefaultNodeColor);

        // Primary size from graph importance (weight / rank / degree) so high-weight
        // hubs are visibly larger and the layout can treat them as more significant.
        double weight = n.Weight ?? 0;
        int degree = n.Degree ?? 0;
        int rank = n.Rank ?? 999;

        // Rough normalization. Top weights in typical bundles are ~0.10-0.15.
        double wScore = Math.Min(1.0, weight * 7.5);
        // Degree normalization (hubs often 50-90+)
        double dScore = Math.Min(1.0, degree / 95.0);
        // Rank: lower number = more important (rank 1 is top)
        double rScore = rank > 0 ? Math.Max(0.0, (25.0 - Math.Min(25, rank)) / 25.0) : 0.0;

        double importance = 0.62 * wScore + 0.28 * dScore + 0.10 * rScore;

        int minSize = 22;
        int maxSize = 98;
        int size = (int)Math.Round(minSize + importance * (maxSize - minSize));

        // Body-length fallback / augmentation only when we have very little graph signal
        // or when the node has substantial descriptive content.
        int bodyLen = n.Body?.Length ?? 0;
        if (importance < 0.12 || bodyLen > 450)
        {
            int bodySize = 26 + Math.Min(38, bodyLen / 220);
            // Only let body help when it is larger than the importance-based size
            if (bodySize > size)
                size = bodySize;
        }

        size = Math.Clamp(size, 20, 105);

        double fontSize = Math.Round(9.2 + importance * 5.8, 1); // ~9.2 - ~15

        return new NodeData
        {
            Id = n.Id,
            Slug = n.Slug,
            Label = string.IsNullOrEmpty(n.Label) ? n.Id : n.Label,
            Type = n.Type,
            Description = n.Description ?? "",
            Resource = n.Resource ?? "",
            Tags = n.Tags?.ToList() ?? [],
            Status = n.Status ?? "",
            Generated = n.Generated,
            Verified = n.Verified?.ToList() ?? [],
            StaleAfter = n.StaleAfter?.ToString("yyyy-MM-dd") ?? "",
            Sources = n.Sources?.ToList() ?? [],
            TrustTier = n.TrustTier ?? TrustSignals.Unverified,
            Stale = n.Stale ?? false,
            Color = color,
            Size = size,
            Weight = weight,
            Rank = rank,
            Degree = degree,
            In = n.In ?? 0,
            Out = n.Out ?? 0,
            FontSize = fontSize,
        };
    }



    sealed class GraphData
    {
        [JsonPropertyName("nodes")]
        public List<NodeElement> Nodes { get; init; } = [];

        [JsonPropertyName("edges")]
        public List<EdgeElement> Edges { get; init; } = [];

        [JsonPropertyName("bodies")]
        public Dictionary<string, string> Bodies { get; init; } = [];

        [JsonPropertyName("types")]
        public List<string> Types { get; init; } = [];

        [JsonPropertyName("palette")]
        public Dictionary<string, string> Palette { get; init; } = [];
    }

    sealed class NodeElement
    {
        [JsonPropertyName("data")]
        public NodeData Data { get; init; } = new();
    }

    sealed class NodeData
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("slug")]
        public string? Slug { get; init; }

        [JsonPropertyName("label")]
        public string Label { get; init; } = "";

        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("description")]
        public string Description { get; init; } = "";

        [JsonPropertyName("resource")]
        public string Resource { get; init; } = "";

        [JsonPropertyName("tags")]
        public List<string> Tags { get; init; } = [];

        [JsonPropertyName("status")]
        public string Status { get; init; } = "";

        [JsonPropertyName("generated")]
        public ActorEvent? Generated { get; init; }

        [JsonPropertyName("verified")]
        public List<ActorEvent> Verified { get; init; } = [];

        [JsonPropertyName("staleAfter")]
        public string StaleAfter { get; init; } = "";

        [JsonPropertyName("sources")]
        public List<SourceEntry> Sources { get; init; } = [];

        [JsonPropertyName("trustTier")]
        public string TrustTier { get; init; } = TrustSignals.Unverified;

        [JsonPropertyName("stale")]
        public bool Stale { get; init; }

        [JsonPropertyName("color")]
        public string Color { get; init; } = "";

        [JsonPropertyName("size")]
        public int Size { get; init; }

        // Graph importance signals (used for sizing + layout hints)
        [JsonPropertyName("weight")]
        public double Weight { get; init; }

        [JsonPropertyName("rank")]
        public int Rank { get; init; }

        [JsonPropertyName("degree")]
        public int Degree { get; init; }

        [JsonPropertyName("in")]
        public int In { get; init; }

        [JsonPropertyName("out")]
        public int Out { get; init; }

        [JsonPropertyName("fontSize")]
        public double FontSize { get; init; } = 11;
    }

    sealed class EdgeElement
    {
        [JsonPropertyName("data")]
        public EdgeData Data { get; init; } = new();
    }

    sealed class EdgeData
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("source")]
        public string Source { get; init; } = "";

        [JsonPropertyName("target")]
        public string Target { get; init; } = "";
    }
}
