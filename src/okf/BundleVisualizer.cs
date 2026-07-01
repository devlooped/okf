using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace okf;

public sealed record VisualizationStats(int Concepts, int Edges, int Bytes);

public static partial class BundleVisualizer
{
    const string IndexName = "index.md";

    static readonly Dictionary<string, string> TypePalette = new(StringComparer.Ordinal)
    {
        ["BigQuery Dataset"] = "#8b5cf6",
        ["BigQuery Table"] = "#3b82f6",
        ["Reference"] = "#10b981",
    };

    const string DefaultNodeColor = "#94a3b8";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static VisualizationStats Generate(string bundleRoot, string outPath, string? bundleName = null)
    {
        bundleRoot = Path.GetFullPath(bundleRoot);
        outPath = Path.GetFullPath(outPath);

        if (!Directory.Exists(bundleRoot))
        {
            throw new DirectoryNotFoundException($"Bundle directory not found: {bundleRoot}");
        }

        var concepts = WalkConcepts(bundleRoot);
        var graph = BuildGraph(concepts);
        var name = bundleName ?? new DirectoryInfo(bundleRoot).Name;

        var html = ThisAssembly.Resources.Google.viz_template.Text
            .Replace("/*__VIZ_CSS__*/", ThisAssembly.Resources.Google.viz_styles.Text, StringComparison.Ordinal)
            .Replace("/*__VIZ_JS__*/", ThisAssembly.Resources.Google.viz_script.Text, StringComparison.Ordinal)
            .Replace("__BUNDLE_NAME__", JsonSerializer.Serialize(name, JsonOptions), StringComparison.Ordinal)
            .Replace("__BUNDLE_DATA__", JsonSerializer.Serialize(graph, JsonOptions), StringComparison.Ordinal);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, html, Encoding.UTF8);

        return new VisualizationStats(concepts.Count, graph.Edges.Count, Encoding.UTF8.GetByteCount(html));
    }

    static List<Concept> WalkConcepts(string bundleRoot)
    {
        var concepts = new List<Concept>();

        foreach (var absolutePath in Directory.EnumerateFiles(bundleRoot, "*.md", SearchOption.AllDirectories).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(absolutePath).Equals(IndexName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(bundleRoot, absolutePath).Replace('\\', '/');
            var conceptId = Path.ChangeExtension(relativePath, null)!.Replace('\\', '/');

            var text = File.ReadAllText(absolutePath);
            if (!OKFDocument.TryParse(text, out var document, out _))
            {
                continue;
            }

            var frontmatter = document!.Frontmatter;
            concepts.Add(new Concept(
                conceptId,
                GetString(frontmatter, "type", "Unknown"),
                GetString(frontmatter, "title", conceptId),
                GetString(frontmatter, "description", ""),
                GetString(frontmatter, "resource", ""),
                GetTags(frontmatter),
                document.Body,
                ExtractLinks(document.Body, Path.GetDirectoryName(absolutePath)!, bundleRoot)));
        }

        return concepts;
    }

    static GraphData BuildGraph(List<Concept> concepts)
    {
        var ids = concepts.Select(static concept => concept.Id).ToHashSet(StringComparer.Ordinal);
        var nodes = concepts.Select(static concept => concept.ToNode()).ToList();
        var edges = new List<EdgeElement>();
        var seenEdges = new HashSet<(string Source, string Target)>();

        foreach (var concept in concepts)
        {
            foreach (var target in concept.LinksTo)
            {
                if (target == concept.Id || !ids.Contains(target))
                {
                    continue;
                }

                var key = (concept.Id, target);
                if (!seenEdges.Add(key))
                {
                    continue;
                }

                edges.Add(new EdgeElement
                {
                    Data = new EdgeData
                    {
                        Id = $"{concept.Id}__{target}",
                        Source = concept.Id,
                        Target = target,
                    },
                });
            }
        }

        return new GraphData
        {
            Nodes = nodes,
            Edges = edges,
            Bodies = concepts.ToDictionary(static concept => concept.Id, static concept => concept.Body, StringComparer.Ordinal),
            Types = concepts.Select(static concept => concept.Type).Distinct().OrderBy(static type => type, StringComparer.Ordinal).ToList(),
            Palette = TypePalette,
        };
    }

    static List<string> ExtractLinks(string body, string docDirectory, string bundleRoot)
    {
        var bundleRootResolved = Path.GetFullPath(bundleRoot);
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in ConceptLinkRegex().Matches(body))
        {
            var target = match.Groups[1].Value;
            if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith('/'))
            {
                continue;
            }

            var resolved = Path.GetFullPath(Path.Combine(docDirectory, target.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(bundleRootResolved, resolved).Replace('\\', '/');
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                continue;
            }

            if (relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative[..^3];
            }

            if (relative.Length > 0 && seen.Add(relative))
            {
                links.Add(relative);
            }
        }

        return links;
    }

    static string GetString(IReadOnlyDictionary<string, object?> frontmatter, string key, string defaultValue)
    {
        if (!frontmatter.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            string text => text,
            _ => value.ToString() ?? defaultValue,
        };
    }

    static List<string> GetTags(IReadOnlyDictionary<string, object?> frontmatter)
    {
        if (!frontmatter.TryGetValue("tags", out var value) || value is null)
        {
            return [];
        }

        if (value is string text)
        {
            return [text];
        }

        if (value is IEnumerable enumerable)
        {
            var tags = new List<string>();
            foreach (var item in enumerable)
            {
                tags.Add(item?.ToString() ?? "");
            }

            return tags;
        }

        return [value.ToString() ?? ""];
    }

    [GeneratedRegex(@"\]\(([^)\s]+\.md)(?:#[A-Za-z0-9_\-]*)?\)", RegexOptions.Compiled)]
    private static partial Regex ConceptLinkRegex();

    sealed class Concept
    {
        public Concept(
            string id,
            string type,
            string title,
            string description,
            string resource,
            List<string> tags,
            string body,
            List<string> linksTo)
        {
            Id = id;
            Type = type;
            Title = title;
            Description = description;
            Resource = resource;
            Tags = tags;
            Body = body;
            LinksTo = linksTo;
        }

        public string Id { get; }
        public string Type { get; }
        public string Title { get; }
        public string Description { get; }
        public string Resource { get; }
        public List<string> Tags { get; }
        public string Body { get; }
        public List<string> LinksTo { get; }

        public NodeElement ToNode()
        {
            var color = TypePalette.GetValueOrDefault(Type, DefaultNodeColor);
            return new NodeElement
            {
                Data = new NodeData
                {
                    Id = Id,
                    Label = string.IsNullOrEmpty(Title) ? Id : Title,
                    Type = Type,
                    Description = Description,
                    Resource = Resource,
                    Tags = Tags,
                    Color = color,
                    Size = 30 + Math.Min(60, Body.Length / 200),
                },
            };
        }
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

        [JsonPropertyName("color")]
        public string Color { get; init; } = "";

        [JsonPropertyName("size")]
        public int Size { get; init; }
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
