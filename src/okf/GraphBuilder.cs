using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devlooped;

public static partial class GraphBuilder
{
    internal const string IndexName = "index.md";
    const string LogName = "log.md";

    static readonly TextInfo TitleCasing = CultureInfo.CurrentCulture.TextInfo;

    // UTF-8 without BOM for clean script/JSON files (browser + node friendly; Load() already strips BOM if present)
    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static JsonSerializerOptions JsonOptions { get; } = new(GraphJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record KnowledgeGraph
    {
        public List<Node> Nodes { get; init; } = [];
        public List<Edge> Edges { get; init; } = [];

        /// <summary>Index-driven nav tree; null when includeNav is false.</summary>
        [JsonPropertyOrder(-8)]
        public NavNode? Nav { get; init; }

        [JsonPropertyOrder(-10)]
        public string Version { get; init; } = "0.1";

        [JsonPropertyOrder(-9)]
        public DateTimeOffset? Timestamp { get; init; }

        public Bundle? Bundle { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>
    /// Index-driven navigation tree node. Directories are not concepts (SPEC §3.1).
    /// </summary>
    public sealed record NavNode
    {
        /// <summary>"group" | "dir" | "concept" | "orphans"</summary>
        public required string Kind { get; init; }

        /// <summary>
        /// Concept id, or directory id (relative path; empty string for root).
        /// Null for pure groups and orphans wrappers.
        /// </summary>
        public string? Id { get; init; }

        public string? Label { get; init; }
        public string? Description { get; init; }

        /// <summary>
        /// Index markdown for kind=dir (authored after frontmatter strip, or synthetic).
        /// Parallel to <see cref="Node.Body"/>.
        /// </summary>
        public string? Body { get; init; }

        /// <summary>True when body/children were synthesized because index.md was missing.</summary>
        public bool? Synthetic { get; init; }

        public IReadOnlyList<NavNode>? Children { get; init; }
    }

    public sealed record Bundle
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed record Node
    {
        public required string Id { get; init; }
        /// <summary>Short unique slug for the node (computed via first letters + disambiguation). Nice for URLs / #fragments.</summary>
        public string? Slug { get; init; }
        public required string Type { get; init; }
        public string? Title { get; init; }
        public string? Label { get; init; }
        public string? Description { get; init; }
        public string? Resource { get; init; }
        public IReadOnlyList<string>? Tags { get; init; }
        public DateTimeOffset? Timestamp { get; init; }
        public string? Body { get; init; }
        public string? Path { get; init; }
        public int? Degree { get; init; }
        public int? In { get; init; }
        public int? Out { get; init; }
        public double? Weight { get; init; }
        public int? Rank { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed record Edge
    {
        public required string Source { get; init; }
        public required string Target { get; init; }
        public string Id { get; init; } = "";
        public string? Label { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

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

        return EnsureNodeWeights(EnsureEdgeIds(graph));
    }

    static KnowledgeGraph EnsureEdgeIds(KnowledgeGraph graph)
    {
        var needEdgeIds = graph.Edges.Any(e => string.IsNullOrWhiteSpace(e.Id));
        var needSlugs = graph.Nodes.Any(n => string.IsNullOrWhiteSpace(n.Slug));

        if (!needEdgeIds && !needSlugs)
            return graph;

        var conceptIds = graph.Nodes
            .Select(n => n.Id)
            .Concat(graph.Edges.SelectMany(e => new[] { e.Source, e.Target }))
            .Distinct(StringComparer.Ordinal);
        var conceptAbbrs = ShortIds.ComputeConceptAbbreviations(conceptIds);

        var edges = needEdgeIds
            ? graph.Edges
                .Select(e => string.IsNullOrWhiteSpace(e.Id)
                    ? e with { Id = FormatEdgeId(conceptAbbrs, e.Source, e.Target) }
                    : e)
                .ToList()
            : graph.Edges;

        var nodes = needSlugs
            ? graph.Nodes
                .Select(n => string.IsNullOrWhiteSpace(n.Slug) && conceptAbbrs.TryGetValue(n.Id, out var s)
                    ? n with { Slug = s }
                    : n)
                .ToList()
            : graph.Nodes;

        return graph with { Nodes = nodes, Edges = edges };
    }

    static KnowledgeGraph EnsureNodeWeights(KnowledgeGraph graph)
    {
        if (graph.Nodes.All(n => n.Weight.HasValue && n.Rank.HasValue))
            return graph;

        Dictionary<string, double> weights;
        if (graph.Nodes.All(n => n.Weight.HasValue))
        {
            weights = graph.Nodes.ToDictionary(n => n.Id, n => n.Weight!.Value, StringComparer.Ordinal);
        }
        else
        {
            var computed = PageRank.Compute(graph.Nodes, graph.Edges);
            weights = graph.Nodes.ToDictionary(
                n => n.Id,
                n => n.Weight ?? computed.Weights[n.Id],
                StringComparer.Ordinal);
        }

        var ranks = PageRank.ComputeRanks(weights);
        var nodes = graph.Nodes
            .Select(n => n with
            {
                Weight = n.Weight ?? weights[n.Id],
                Rank = n.Rank ?? ranks[n.Id],
            })
            .ToList();

        return graph with { Nodes = nodes };
    }

    static string FormatEdgeId(IReadOnlyDictionary<string, string> conceptAbbrs, string source, string target)
        => $"{conceptAbbrs[source]}_{conceptAbbrs[target]}";

    /// <summary>
    /// Builds the knowledge graph for a bundle and returns the serializable structure.
    /// </summary>
    public static KnowledgeGraph Build(
        string bundleRoot,
        bool includeBody = false,
        IReadOnlyDictionary<string, string>? bundleProperties = null,
        bool includeNav = false)
    {
        bundleRoot = Path.GetFullPath(bundleRoot);

        if (!Directory.Exists(bundleRoot))
        {
            throw new DirectoryNotFoundException($"Bundle directory not found: {bundleRoot}");
        }

        var concepts = WalkConcepts(bundleRoot, includeBody);
        var graph = BuildGraph(concepts, bundleRoot, bundleProperties, includeNav);
        return graph;
    }

    /// <summary>
    /// Builds the graph and writes it as pretty JSON to the given path. Returns basic stats.
    /// </summary>
    public static (int Nodes, int Edges) Generate(
        string bundleRoot,
        string outPath,
        bool includeBody = false,
        IReadOnlyDictionary<string, string>? bundleProperties = null,
        bool script = false,
        bool includeNav = false)
    {
        var graph = Build(bundleRoot, includeBody, bundleProperties, includeNav);

        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }
        var json = JsonSerializer.Serialize(graph, JsonOptions);
        var content = script
            ? FormatAsScriptWithWindowGlobal(json)
            : json;
        File.WriteAllText(outPath, content, Utf8NoBom);

        return (graph.Nodes.Count, graph.Edges.Count);
    }

    internal static string FormatAsScriptWithWindowGlobal(string json)
    {
        return $"""
            /*
             * Generated OKF graph data.
             * Load with a plain script tag:
             *
             *   <script src="okf.js"></script>
             *   <script>
             *     const data = window.data;
             *     // use data
             *   </script>
             */
            const data = {json};
            window.data = data;

            """;
    }

    /// <summary>
    /// Writes an existing KnowledgeGraph to disk as JSON or as a plain JS script that sets window.data.
    /// </summary>
    public static (int Nodes, int Edges) WriteGraph(
        KnowledgeGraph graph,
        string outPath,
        bool asScript = false)
    {
        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }
        var json = JsonSerializer.Serialize(graph, JsonOptions);
        var content = asScript
            ? FormatAsScriptWithWindowGlobal(json)
            : json;
        File.WriteAllText(outPath, content, Utf8NoBom);

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

            if (!ConceptDocument.TryDeserialize(document!.FrontmatterYaml, out var conceptDoc))
            {
                // Invalid OKF document per spec - skip
                continue;
            }

            concepts.Add(new Concept(
                conceptId,
                relativePath,
                conceptDoc!,
                includeBody ? document.Body : null,
                ExtractLinks(document.Body, relativePath, bundleRootFull)));
        }

        return concepts;
    }

    static Dictionary<string, JsonElement>? CreateBundleExtensionData(
        IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return null;
        }

        var extensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            extensionData[key] = Converters.ToJsonElement(value);
        }

        return extensionData;
    }

    static KnowledgeGraph BuildGraph(
        List<Concept> concepts,
        string bundleRoot,
        IReadOnlyDictionary<string, string>? bundleProperties = null,
        bool includeNav = false)
    {
        var ids = concepts.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var idToTitle = concepts.ToDictionary(c => c.Id, c => c.Document.Title ?? c.Id, StringComparer.Ordinal);

        var conceptAbbrs = ShortIds.ComputeConceptAbbreviations(concepts.Select(c => c.Id));

        var nodes = new List<Node>();
        var edges = new List<Edge>();
        var seenEdges = new HashSet<(string Source, string Target)>(comparer: null);

        // Build nodes first (without degrees)
        var nodeLookup = new Dictionary<string, Node>(StringComparer.Ordinal);

        foreach (var c in concepts.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var node = new Node
            {
                Id = c.Id,
                Slug = conceptAbbrs.TryGetValue(c.Id, out var slug) ? slug : null,
                Type = c.Document.Type!,
                Path = c.Path,
                Degree = 0,
                In = 0,
                Out = 0,
                Title = c.Document.Title,
                Label = GetFrontmatterLabel(c.Document.ExtensionData),
                Description = c.Document.Description,
                Resource = c.Document.Resource,
                Tags = c.Document.Tags,
                Timestamp = c.Document.Timestamp,
                ExtensionData = CopyExtensionDataExcluding(c.Document.ExtensionData, "label"),
                Body = c.Body,
            };

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

                edges.Add(new Edge
                {
                    Source = c.Id,
                    Target = targetId,
                    Id = FormatEdgeId(conceptAbbrs, c.Id, targetId),
                    Label = label,
                });
            }
        }

        var indexLabels = LoadIndexLinkLabels(bundleRoot, ids);
        var incomingLabels = CollectIncomingLinkTexts(concepts, ids, bundleRoot);
        var derivedLabels = ResolveDerivedLabels(concepts, indexLabels, incomingLabels);

        // Compute degrees
        var outDeg = new Dictionary<string, int>(StringComparer.Ordinal);
        var inDeg = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var e in edges)
        {
            outDeg[e.Source] = outDeg.GetValueOrDefault(e.Source) + 1;
            inDeg[e.Target] = inDeg.GetValueOrDefault(e.Target) + 1;
        }

        // Update nodes with degrees and derived labels (records are immutable here)
        var finalNodes = new List<Node>(nodes.Count);
        foreach (var n in nodes)
        {
            var ins = inDeg.GetValueOrDefault(n.Id);
            var outs = outDeg.GetValueOrDefault(n.Id);
            var label = n.Label ?? derivedLabels.GetValueOrDefault(n.Id);
            finalNodes.Add(n with { In = ins, Out = outs, Degree = ins + outs, Label = label });
        }

        var pageRank = PageRank.Compute(finalNodes, edges);
        finalNodes = [.. finalNodes
            .Select(n => n with
            {
                Weight = pageRank.Weights[n.Id],
                Rank = pageRank.Ranks[n.Id],
            })];

        var bundleExt = CreateBundleExtensionData(bundleProperties);
        var bundle = bundleExt is not null
            ? new Bundle { ExtensionData = bundleExt }
            : null;

        NavNode? nav = null;
        if (includeNav)
        {
            nav = IndexNavBuilder.Build(bundleRoot, finalNodes);
        }

        return new KnowledgeGraph
        {
            Nodes = finalNodes,
            Edges = edges,
            Nav = nav,
            Timestamp = DateTimeOffset.UtcNow,
            Bundle = bundle,
        };
    }

    internal static string NormalizeToConceptId(string resolvedRelative)
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

    /// <summary>
    /// Returns true if the visible link text is essentially the same as the link reference/target.
    /// Such links (e.g. [foo](foo.md) or [definition](definition)) are not useful as human-readable labels
    /// for the target node and should be ignored when deriving labels from incoming links.
    /// </summary>
    static bool LinkTextEqualsRef(string linkText, string rawTarget, string? targetId = null)
    {
        if (string.IsNullOrWhiteSpace(linkText))
            return true;

        var text = linkText.Trim();
        var target = rawTarget.Trim();

        if (string.Equals(text, target, StringComparison.OrdinalIgnoreCase))
            return true;

        // Normalize by removing trailing slashes and .md extension for comparison
        static string Normalize(string s)
        {
            s = s.TrimEnd('/');
            if (s.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                s = s[..^3];
            // also strip leading / for bare names
            s = s.TrimStart('/');
            return s;
        }

        var normText = Normalize(text);
        var normTarget = Normalize(target);

        if (string.Equals(normText, normTarget, StringComparison.OrdinalIgnoreCase))
            return true;

        if (targetId != null)
        {
            var normId = Normalize(targetId);
            if (string.Equals(normText, normId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, targetId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static Dictionary<string, string> LoadIndexLinkLabels(string bundleRoot, HashSet<string> conceptIds)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var bundleRootFull = Path.GetFullPath(bundleRoot);

        foreach (var absolutePath in Directory.EnumerateFiles(bundleRootFull, IndexName, SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(bundleRootFull, absolutePath).Replace('\\', '/');
            var text = File.ReadAllText(absolutePath);
            var body = GetIndexBody(relativePath, text);

            foreach (var (linkText, rawTarget, _) in MarkdownLinks.ExtractWithText(body))
            {
                if (string.IsNullOrWhiteSpace(linkText) || !MarkdownLinks.IsInternalLink(rawTarget))
                {
                    continue;
                }

                if (!MarkdownLinks.TryResolve(rawTarget, relativePath, bundleRootFull, out var resolved))
                {
                    continue;
                }

                var targetId = NormalizeToConceptId(resolved);
                if (!conceptIds.Contains(targetId) || labels.ContainsKey(targetId))
                {
                    continue;
                }

                if (LinkTextEqualsRef(linkText, rawTarget, targetId))
                {
                    continue;
                }

                labels[targetId] = linkText;
            }
        }

        return labels;
    }

    internal static string GetIndexBody(string relativePath, string text)
    {
        var isBundleRoot = relativePath.Equals(IndexName, StringComparison.OrdinalIgnoreCase);
        if (isBundleRoot
            && OKFDocument.HasFrontmatterBlock(text)
            && OKFDocument.TryParse(text, out var document, out _))
        {
            return document!.Body;
        }

        return text;
    }

    static Dictionary<string, string> CollectIncomingLinkTexts(
        List<Concept> concepts,
        HashSet<string> conceptIds,
        string bundleRoot)
    {
        var incoming = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var concept in concepts)
        {
            foreach (var (linkText, rawTarget) in concept.LinksTo)
            {
                if (string.IsNullOrWhiteSpace(linkText) || !MarkdownLinks.IsInternalLink(rawTarget))
                {
                    continue;
                }

                if (!MarkdownLinks.TryResolve(rawTarget, concept.Path, bundleRoot, out var resolved))
                {
                    continue;
                }

                var targetId = NormalizeToConceptId(resolved);
                if (targetId == concept.Id || !conceptIds.Contains(targetId))
                {
                    continue;
                }

                if (LinkTextEqualsRef(linkText, rawTarget, targetId))
                {
                    continue;
                }

                if (!incoming.TryGetValue(targetId, out var texts))
                {
                    texts = [];
                    incoming[targetId] = texts;
                }

                texts.Add(linkText);
            }
        }

        return incoming.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .OrderBy(t => t.Length)
                .ThenBy(t => t, StringComparer.Ordinal)
                .First(),
            StringComparer.Ordinal);
    }

    static Dictionary<string, string> ResolveDerivedLabels(
        List<Concept> concepts,
        Dictionary<string, string> indexLabels,
        Dictionary<string, string> incomingLabels)
    {
        var needsFallback = concepts
            .Where(c => string.IsNullOrWhiteSpace(GetFrontmatterLabel(c.Document.ExtensionData)))
            .ToList();

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var idFallbackIds = new List<string>();

        foreach (var concept in needsFallback)
        {
            if (indexLabels.TryGetValue(concept.Id, out var indexLabel))
            {
                resolved[concept.Id] = indexLabel;
                continue;
            }

            if (incomingLabels.TryGetValue(concept.Id, out var incomingLabel))
            {
                resolved[concept.Id] = incomingLabel;
                continue;
            }

            idFallbackIds.Add(concept.Id);
        }

        foreach (var (conceptId, label) in DisambiguateIdFallbackLabels(idFallbackIds))
        {
            resolved[conceptId] = label;
        }

        return resolved;
    }

    static IEnumerable<(string ConceptId, string Label)> DisambiguateIdFallbackLabels(IReadOnlyList<string> conceptIds)
    {
        if (conceptIds.Count == 0)
        {
            yield break;
        }

        var baseLabels = conceptIds.ToDictionary(
            id => id,
            id => TitleCaseSegment(GetLastSegment(id)),
            StringComparer.Ordinal);

        var groups = baseLabels
            .GroupBy(kvp => kvp.Value, StringComparer.Ordinal)
            .ToList();

        foreach (var group in groups)
        {
            var members = group.Select(kvp => kvp.Key).ToList();
            if (members.Count == 1)
            {
                yield return (members[0], group.Key);
                continue;
            }

            var parentSegments = members.ToDictionary(
                id => id,
                GetParentSegments,
                StringComparer.Ordinal);

            var depth = 1;
            string[]? finalLabels = null;

            while (depth <= members.Max(id => parentSegments[id].Length))
            {
                var candidateLabels = members.ToDictionary(
                    id => id,
                    id => FormatIdFallbackLabel(group.Key, parentSegments[id], depth),
                    StringComparer.Ordinal);

                if (candidateLabels.Values.Distinct(StringComparer.Ordinal).Count() == members.Count)
                {
                    finalLabels = [.. members.Select(id => candidateLabels[id])];
                    break;
                }

                depth++;
            }

            finalLabels ??= [.. members.Select(id => FormatIdFallbackLabel(group.Key, parentSegments[id], parentSegments[id].Length))];

            for (var i = 0; i < members.Count; i++)
            {
                yield return (members[i], finalLabels[i]);
            }
        }
    }

    static string FormatIdFallbackLabel(string baseLabel, string[] parentSegments, int depth)
    {
        if (parentSegments.Length == 0)
        {
            return baseLabel;
        }

        var count = Math.Min(depth, parentSegments.Length);
        var parents = parentSegments[^count..]
            .Select(TitleCaseSegment)
            .ToArray();

        return $"{baseLabel} ({string.Join(", ", parents)})";
    }

    static string[] GetParentSegments(string conceptId)
        => conceptId.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[..^1];

    static string GetLastSegment(string conceptId)
    {
        var segments = conceptId.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 ? segments[^1] : conceptId;
    }

    static string TitleCaseSegment(string segment)
        => string.IsNullOrEmpty(segment)
            ? segment
            : TitleCasing.ToTitleCase(segment);

    static string? GetFrontmatterLabel(Dictionary<string, JsonElement>? extensionData)
    {
        if (extensionData?.TryGetValue("label", out var element) != true
            || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var label = element.GetString();
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }

    static Dictionary<string, JsonElement>? CopyExtensionDataExcluding(
        Dictionary<string, JsonElement>? extensionData,
        params string[] excludeKeys)
    {
        if (extensionData is null || extensionData.Count == 0)
        {
            return null;
        }

        var excluded = new HashSet<string>(excludeKeys, StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var (key, value) in extensionData)
        {
            if (!excluded.Contains(key))
            {
                result[key] = value;
            }
        }

        return result.Count > 0 ? result : null;
    }

    static List<(string Text, string Target)> ExtractLinks(string body, string sourceRelativePath, string bundleRoot)
    {
        var links = new List<(string, string)>();

        foreach (var (text, target, _) in MarkdownLinks.ExtractWithText(body))
        {
            if (!MarkdownLinks.IsInternalLink(target))
            {
                continue;
            }

            links.Add((text, target));
        }

        return links;
    }

    // Internal concept representation
    sealed record Concept(
        string Id,
        string Path,
        ConceptDocument Document,
        string? Body,
        List<(string Text, string Target)> LinksTo);
}
