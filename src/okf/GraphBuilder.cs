using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace okf;

public static partial class GraphBuilder
{
    const string IndexName = "index.md";
    const string LogName = "log.md";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public sealed record KnowledgeGraph(
        [property: JsonPropertyName("bundle")] Bundle Bundle,
        [property: JsonPropertyName("nodes")] List<Node> Nodes,
        [property: JsonPropertyName("edges")] List<Edge> Edges);

    public sealed record Bundle(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("root")] string Root,
        [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
        [property: JsonPropertyName("concepts")] int Concepts);

    public sealed record Node(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("degree")] int Degree,
        [property: JsonPropertyName("in")] int In,
        [property: JsonPropertyName("out")] int Out,
        [property: JsonPropertyName("meta")] Dictionary<string, object?> Meta,
        [property: JsonPropertyName("label")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null,
        [property: JsonPropertyName("body")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body = null);

    public sealed record Edge(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("id")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null);

    /// <summary>
    /// Loads a previously generated knowledge graph from its JSON file.
    /// </summary>
    public static KnowledgeGraph Load(string graphJsonPath)
    {
        var fullPath = Path.GetFullPath(graphJsonPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Graph file not found: {fullPath}");
        }

        var json = File.ReadAllText(fullPath);
        // Strip UTF-8 BOM if present (some older files)
        if (json.Length > 0 && json[0] == '\uFEFF')
            json = json[1..];

        var graph = JsonSerializer.Deserialize<KnowledgeGraph>(json, JsonOptions);
        if (graph is null)
            throw new InvalidDataException($"Failed to deserialize graph from {fullPath}");

        return graph;
    }

    /// <summary>
    /// Builds the knowledge graph for a bundle and returns the serializable structure.
    /// </summary>
    public static KnowledgeGraph Build(string bundleRoot, string? bundleName = null, bool includeBody = false)
    {
        bundleRoot = Path.GetFullPath(bundleRoot);

        if (!Directory.Exists(bundleRoot))
        {
            throw new DirectoryNotFoundException($"Bundle directory not found: {bundleRoot}");
        }

        var concepts = WalkConcepts(bundleRoot, includeBody);
        var graph = BuildGraph(concepts, bundleRoot, bundleName);
        return graph;
    }

    /// <summary>
    /// Builds the graph and writes it as pretty JSON to the given path. Returns basic stats.
    /// </summary>
    public static (int Concepts, int Edges) Generate(string bundleRoot, string outPath, string? bundleName = null, bool includeBody = false)
    {
        var graph = Build(bundleRoot, bundleName, includeBody);

        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }
        var json = JsonSerializer.Serialize(graph, JsonOptions);
        File.WriteAllText(outPath, json, Encoding.UTF8);

        return (graph.Nodes.Count, graph.Edges.Count);
    }

    static List<Concept> WalkConcepts(string bundleRoot, bool includeBody)
    {
        var concepts = new List<Concept>();
        var bundleRootFull = Path.GetFullPath(bundleRoot);

        foreach (var absolutePath in Directory.EnumerateFiles(bundleRoot, "*.md", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(absolutePath);
            if (fileName.Equals(IndexName, StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(LogName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(bundleRootFull, absolutePath).Replace('\\', '/');
            var conceptId = Path.ChangeExtension(relativePath, null)!.Replace('\\', '/');

            var text = File.ReadAllText(absolutePath);

            if (!OKFDocument.TryParse(text, out var document, out _))
            {
                continue;
            }

            var frontmatter = document!.Frontmatter;
            var type = OKFDocument.GetTypeValue(frontmatter);
            if (string.IsNullOrWhiteSpace(type))
            {
                // Invalid OKF document per spec - skip
                continue;
            }

            var meta = SanitizeMeta(frontmatter);

            var title = GetStringValue(frontmatter, "title");
            var label = GetStringValue(frontmatter, "label");
            if (string.IsNullOrWhiteSpace(label))
            {
                label = null;
            }

            concepts.Add(new Concept(
                conceptId,
                relativePath,
                type!,
                title ?? conceptId,
                label,
                meta,
                includeBody ? document.Body : null,
                ExtractLinks(document.Body, relativePath, bundleRootFull)));
        }

        return concepts;
    }

    static KnowledgeGraph BuildGraph(List<Concept> concepts, string bundleRoot, string? bundleName)
    {
        var name = bundleName ?? new DirectoryInfo(bundleRoot).Name;
        var ids = concepts.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var idToTitle = concepts.ToDictionary(c => c.Id, c => c.Title, StringComparer.Ordinal);

        var conceptAbbrs = ShortIds.ComputeConceptAbbreviations(concepts.Select(c => c.Id));

        var nodes = new List<Node>();
        var edges = new List<Edge>();
        var seenEdges = new HashSet<(string Source, string Target)>(comparer: null);

        // Build nodes first (without degrees)
        var nodeLookup = new Dictionary<string, Node>(StringComparer.Ordinal);

        foreach (var c in concepts.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var node = new Node(
                Id: c.Id,
                Path: c.Path,
                Type: c.Type,
                Degree: 0,
                In: 0,
                Out: 0,
                Meta: c.Meta,
                Label: c.Label,
                Body: c.Body);

            nodes.Add(node);
            nodeLookup[c.Id] = node;
        }

        // Resolve links and build edges
        foreach (var c in concepts)
        {
            foreach (var (linkText, rawTarget) in c.LinksTo)
            {
                if (!MarkdownLinks.IsInternalLink(rawTarget))
                {
                    continue;
                }

                if (!MarkdownLinks.TryResolve(rawTarget, c.Path, bundleRoot, out var resolved))
                {
                    continue;
                }

                var targetId = NormalizeToConceptId(resolved);
                if (targetId == c.Id || !ids.Contains(targetId))
                {
                    continue;
                }

                var key = (c.Id, targetId);
                if (!seenEdges.Add(key))
                {
                    continue;
                }

                string? label = !string.IsNullOrWhiteSpace(linkText)
                    ? linkText
                    : (idToTitle.TryGetValue(targetId, out var t) && !string.IsNullOrEmpty(t) ? t : targetId);

                var shortId = $"{conceptAbbrs[c.Id]}_{conceptAbbrs[targetId]}";
                edges.Add(new Edge(c.Id, targetId, label, shortId));
            }
        }

        // Compute degrees
        var outDeg = new Dictionary<string, int>(StringComparer.Ordinal);
        var inDeg = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var e in edges)
        {
            outDeg[e.Source] = outDeg.GetValueOrDefault(e.Source) + 1;
            inDeg[e.Target] = inDeg.GetValueOrDefault(e.Target) + 1;
        }

        // Update nodes with degrees (recreate because records are immutable here)
        var finalNodes = new List<Node>(nodes.Count);
        foreach (var n in nodes)
        {
            var ins = inDeg.GetValueOrDefault(n.Id);
            var outs = outDeg.GetValueOrDefault(n.Id);
            finalNodes.Add(n with { In = ins, Out = outs, Degree = ins + outs });
        }

        var bundle = new Bundle(
            Name: name,
            Root: bundleRoot.Replace('\\', '/'),
            Timestamp: DateTimeOffset.UtcNow,
            Concepts: finalNodes.Count);

        return new KnowledgeGraph(bundle, finalNodes, edges);
    }

    static string NormalizeToConceptId(string resolvedRelative)
    {
        var id = resolvedRelative.Replace('\\', '/');
        if (id.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            id = id[..^3];
        }

        if (id.EndsWith("/index", StringComparison.OrdinalIgnoreCase))
        {
            id = id[..^6];
        }

        id = id.TrimEnd('/');
        return id;
    }

    static List<(string Text, string Target)> ExtractLinks(string body, string sourceRelativePath, string bundleRoot)
    {
        var links = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (text, target, _) in MarkdownLinks.ExtractWithText(body))
        {
            if (!MarkdownLinks.IsInternalLink(target))
            {
                continue;
            }

            // Dedup by target for the concept (we don't need per-line here)
            var key = target;
            if (seen.Add(key))
            {
                links.Add((text, target));
            }
        }

        return links;
    }

    static string? GetStringValue(IReadOnlyDictionary<string, object?> frontmatter, string key)
    {
        if (!frontmatter.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value.ToString(),
        };
    }

    static Dictionary<string, object?> SanitizeMeta(IReadOnlyDictionary<string, object?> frontmatter)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in frontmatter)
        {
            if (string.Equals(kvp.Key, "type", StringComparison.Ordinal) ||
                string.Equals(kvp.Key, "label", StringComparison.Ordinal))
            {
                continue;
            }

            result[kvp.Key] = SanitizeValue(kvp.Value);
        }
        return result;
    }

    static object? SanitizeValue(object? value)
    {
        if (value is null)
            return null;

        return value switch
        {
            string or int or long or double or float or bool or decimal => value,
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            IEnumerable<object?> enumerable => enumerable.Select(SanitizeValue).ToList(),
            System.Collections.IDictionary dict => SanitizeDictionary(dict),
            _ => value.ToString()
        };
    }

    static Dictionary<string, object?> SanitizeDictionary(System.Collections.IDictionary dict)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var key = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            result[key] = SanitizeValue(entry.Value);
        }
        return result;
    }

    // Internal concept representation
    sealed record Concept(
        string Id,
        string Path,
        string Type,
        string Title,
        string? Label,
        Dictionary<string, object?> Meta,
        string? Body,
        List<(string Text, string Target)> LinksTo);
}
