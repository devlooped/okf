using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Devlooped;

/// <summary>
/// Builds the index-driven navigation tree for a knowledge graph (SPEC §6).
/// Condensed at graph generation time when <c>includeNav</c> is true.
/// </summary>
public static partial class IndexNavBuilder
{
    static readonly TextInfo TitleCasing = CultureInfo.CurrentCulture.TextInfo;
    static readonly StringComparer Ordinal = StringComparer.Ordinal;

    public static GraphBuilder.NavNode Build(string bundleRoot, IReadOnlyList<GraphBuilder.Node> nodes)
    {
        bundleRoot = Path.GetFullPath(bundleRoot);
        var nodeById = nodes.ToDictionary(n => n.Id, n => n, Ordinal);
        var conceptIds = nodeById.Keys.ToHashSet(Ordinal);
        var directorySet = CollectDirectorySet(conceptIds);
        var built = new Dictionary<string, GraphBuilder.NavNode>(Ordinal);

        return BuildDir("", bundleRoot, nodeById, conceptIds, directorySet, built);
    }

    static HashSet<string> CollectDirectorySet(IEnumerable<string> conceptIds)
    {
        var dirs = new HashSet<string>(Ordinal) { "" };
        foreach (var id in conceptIds)
        {
            var slash = id.LastIndexOf('/');
            while (slash > 0)
            {
                var parent = id[..slash];
                dirs.Add(parent);
                slash = parent.LastIndexOf('/');
            }

            // immediate parent of root-level concepts is ""
            if (slash == 0)
            {
                // path like "/foo" shouldn't happen
            }
            else if (!id.Contains('/'))
            {
                // root-level concept
            }
            else
            {
                var parent = id[..id.LastIndexOf('/')];
                dirs.Add(parent);
            }
        }

        // Ensure intermediate segments (already added via walk)
        // Also add every prefix of each concept path
        foreach (var id in conceptIds.ToArray())
        {
            var parts = id.Split('/');
            if (parts.Length <= 1)
                continue;
            var acc = parts[0];
            dirs.Add(acc);
            for (var i = 1; i < parts.Length - 1; i++)
            {
                acc = acc + "/" + parts[i];
                dirs.Add(acc);
            }
        }

        return dirs;
    }

    static GraphBuilder.NavNode BuildDir(
        string dirId,
        string bundleRoot,
        Dictionary<string, GraphBuilder.Node> nodeById,
        HashSet<string> conceptIds,
        HashSet<string> directorySet,
        Dictionary<string, GraphBuilder.NavNode> built)
    {
        if (built.TryGetValue(dirId, out var cached))
            return cached;

        // Placeholder to break cycles if any (shouldn't happen for trees)
        built[dirId] = new GraphBuilder.NavNode { Kind = "dir", Id = dirId };

        var indexRel = string.IsNullOrEmpty(dirId)
            ? GraphBuilder.IndexName
            : dirId + "/" + GraphBuilder.IndexName;
        var indexAbs = Path.Combine(bundleRoot, indexRel.Replace('/', Path.DirectorySeparatorChar));

        string body;
        bool synthetic;
        string? titleFromIndex = null;

        List<GraphBuilder.NavNode> childNodes;
        if (File.Exists(indexAbs))
        {
            var text = File.ReadAllText(indexAbs);
            body = GraphBuilder.GetIndexBody(indexRel, text);
            synthetic = false;
            var entriesByGroup = ParseIndexEntries(body, out titleFromIndex, out _);
            childNodes = MaterializeChildren(
                dirId,
                bundleRoot,
                indexRel,
                entriesByGroup,
                nodeById,
                conceptIds,
                directorySet,
                built);
        }
        else
        {
            synthetic = true;
            (body, childNodes) = SynthesizeDir(dirId, bundleRoot, nodeById, conceptIds, directorySet, built);
            titleFromIndex = null;
        }

        var nodeAuthored = FinishDir(dirId, bundleRoot, body, synthetic, childNodes, titleFromIndex);
        built[dirId] = nodeAuthored;
        return nodeAuthored;
    }

    static GraphBuilder.NavNode FinishDir(
        string dirId,
        string bundleRoot,
        string body,
        bool synthetic,
        List<GraphBuilder.NavNode> children,
        string? titleFromIndex)
    {
        string label;
        if (!string.IsNullOrWhiteSpace(titleFromIndex))
        {
            label = titleFromIndex!;
        }
        else if (string.IsNullOrEmpty(dirId))
        {
            label = new DirectoryInfo(bundleRoot).Name;
        }
        else
        {
            var seg = dirId.Contains('/') ? dirId[(dirId.LastIndexOf('/') + 1)..] : dirId;
            label = TitleCaseSegment(seg);
        }

        return new GraphBuilder.NavNode
        {
            Kind = "dir",
            Id = dirId,
            Label = label,
            Body = body,
            Synthetic = synthetic ? true : null,
            Children = children.Count > 0 ? children : null,
        };
    }

    static List<GraphBuilder.NavNode> MaterializeChildren(
        string dirId,
        string bundleRoot,
        string sourcePath,
        List<(string? GroupLabel, List<IndexEntry> Entries)> buckets,
        Dictionary<string, GraphBuilder.Node> nodeById,
        HashSet<string> conceptIds,
        HashSet<string> directorySet,
        Dictionary<string, GraphBuilder.NavNode> built)
    {
        // Build group nodes from buckets, applying local membership filter
        var groupNodes = new List<GraphBuilder.NavNode>();
        var listedConceptIds = new HashSet<string>(Ordinal);
        var listedDirIds = new HashSet<string>(Ordinal);

        foreach (var (groupLabel, entries) in buckets)
        {
            var members = new List<GraphBuilder.NavNode>();
            foreach (var entry in entries)
            {
                var resolved = ResolveEntry(entry, sourcePath, bundleRoot, dirId, conceptIds, directorySet);
                if (resolved is null)
                    continue;

                if (resolved.Kind == "concept")
                {
                    listedConceptIds.Add(resolved.Id);
                    var concept = nodeById[resolved.Id];
                    members.Add(new GraphBuilder.NavNode
                    {
                        Kind = "concept",
                        Id = resolved.Id,
                        Label = !string.IsNullOrWhiteSpace(entry.Title)
                            ? entry.Title
                            : (concept.Label ?? concept.Title ?? resolved.Id),
                        Description = !string.IsNullOrWhiteSpace(entry.Description)
                            ? entry.Description
                            : concept.Description,
                    });
                }
                else
                {
                    listedDirIds.Add(resolved.Id);
                    var childDir = BuildDir(resolved.Id, bundleRoot, nodeById, conceptIds, directorySet, built);
                    // Overlay label/description from this index entry when provided
                    var label = !string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : childDir.Label;
                    var desc = !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description : childDir.Description;
                    members.Add(childDir with { Label = label, Description = desc });
                }
            }

            if (members.Count == 0)
                continue; // drop empty groups (K13)

            if (groupLabel is null)
            {
                // Implicit bucket: flat children (no group wrapper)
                groupNodes.Add(new GraphBuilder.NavNode
                {
                    Kind = "group",
                    Label = null,
                    Children = members,
                });
            }
            else
            {
                groupNodes.Add(new GraphBuilder.NavNode
                {
                    Kind = "group",
                    Label = groupLabel,
                    Children = members,
                });
            }
        }

        // Flatten rule + implicit promotion
        var result = ApplyFlatten(groupNodes);

        // Orphans
        var orphans = CollectOrphans(dirId, conceptIds, directorySet, listedConceptIds, listedDirIds, nodeById, bundleRoot, built);
        if (orphans.Count > 0)
        {
            result.Add(new GraphBuilder.NavNode
            {
                Kind = "orphans",
                Label = "Other",
                Children = orphans,
            });
        }

        return result;
    }

    static List<GraphBuilder.NavNode> ApplyFlatten(List<GraphBuilder.NavNode> groupNodes)
    {
        // Split implicit (label null) vs labeled groups
        var labeled = groupNodes.Where(g => g.Label is not null).ToList();
        var implicitGroups = groupNodes.Where(g => g.Label is null).ToList();

        var result = new List<GraphBuilder.NavNode>();

        // Promote implicit children always flat
        foreach (var ig in implicitGroups)
        {
            if (ig.Children is not null)
                result.AddRange(ig.Children);
        }

        // Flatten when exactly one labeled group and no ungrouped (implicit) entries
        if (labeled.Count == 1 && result.Count == 0)
        {
            if (labeled[0].Children is not null)
                result.AddRange(labeled[0].Children!);
            return result;
        }

        // Keep labeled groups when 0 or 2+ (or mixed with implicit)
        foreach (var g in labeled)
            result.Add(g);

        return result;
    }

    static List<GraphBuilder.NavNode> CollectOrphans(
        string dirId,
        HashSet<string> conceptIds,
        HashSet<string> directorySet,
        HashSet<string> listedConceptIds,
        HashSet<string> listedDirIds,
        Dictionary<string, GraphBuilder.Node> nodeById,
        string bundleRoot,
        Dictionary<string, GraphBuilder.NavNode> built)
    {
        var orphans = new List<GraphBuilder.NavNode>();

        foreach (var id in conceptIds.Where(c => IsDirectChildConcept(dirId, c)).OrderBy(c => c, Ordinal))
        {
            if (listedConceptIds.Contains(id))
                continue;
            var concept = nodeById[id];
            orphans.Add(new GraphBuilder.NavNode
            {
                Kind = "concept",
                Id = id,
                Label = concept.Label ?? concept.Title ?? id,
                Description = concept.Description,
            });
        }

        foreach (var childDir in directorySet.Where(d => IsDirectChildDir(dirId, d)).OrderBy(d => d, Ordinal))
        {
            if (listedDirIds.Contains(childDir))
                continue;
            var dirNode = BuildDir(childDir, bundleRoot, nodeById, conceptIds, directorySet, built);
            orphans.Add(dirNode);
        }

        // Sort by label then id
        return orphans
            .OrderBy(n => n.Label ?? n.Id ?? "", Ordinal)
            .ThenBy(n => n.Id ?? "", Ordinal)
            .ToList();
    }

    static bool IsDirectChildConcept(string dirId, string conceptId)
    {
        if (string.IsNullOrEmpty(dirId))
            return !conceptId.Contains('/');
        if (!conceptId.StartsWith(dirId + "/", StringComparison.Ordinal))
            return false;
        var rest = conceptId[(dirId.Length + 1)..];
        return !rest.Contains('/');
    }

    static bool IsDirectChildDir(string dirId, string childDirId)
    {
        if (string.IsNullOrEmpty(childDirId))
            return false;
        if (string.IsNullOrEmpty(dirId))
            return !childDirId.Contains('/');
        if (!childDirId.StartsWith(dirId + "/", StringComparison.Ordinal))
            return false;
        var rest = childDirId[(dirId.Length + 1)..];
        return !rest.Contains('/');
    }

    sealed record IndexEntry(string Title, string Href, string? Description);

    sealed record ResolvedTarget(string Kind, string Id); // Kind: concept | dir

    static ResolvedTarget? ResolveEntry(
        IndexEntry entry,
        string sourcePath,
        string bundleRoot,
        string dirId,
        HashSet<string> conceptIds,
        HashSet<string> directorySet)
    {
        if (!MarkdownLinks.IsInternalLink(entry.Href))
            return null;

        if (!MarkdownLinks.TryResolve(entry.Href, sourcePath, bundleRoot, out var resolved))
            return null;

        var targetId = GraphBuilder.NormalizeToConceptId(resolved);

        // Directory link: ends with / or resolved to index
        var isDirLink = entry.Href.TrimEnd().TrimEnd('#').Split('#')[0].EndsWith('/')
            || resolved.EndsWith("/index.md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(resolved), GraphBuilder.IndexName, StringComparison.OrdinalIgnoreCase);

        if (isDirLink || directorySet.Contains(targetId))
        {
            // targetId for dir index is the dir path
            var dirTarget = targetId;
            if (!IsDirectChildDir(dirId, dirTarget) && !(string.IsNullOrEmpty(dirId) && !dirTarget.Contains('/') && directorySet.Contains(dirTarget)))
            {
                // local membership: direct child only
                if (!IsDirectChildDir(dirId, dirTarget))
                    return null;
            }

            if (!directorySet.Contains(dirTarget))
                return null;

            return new ResolvedTarget("dir", dirTarget);
        }

        if (conceptIds.Contains(targetId) && IsDirectChildConcept(dirId, targetId))
            return new ResolvedTarget("concept", targetId);

        return null;
    }

    static List<(string? GroupLabel, List<IndexEntry> Entries)> ParseIndexEntries(
        string body,
        out string? titleFromIndex,
        out string mode)
    {
        titleFromIndex = null;
        var lines = body.Split('\n');
        mode = DetectMode(lines);

        string? currentGroupLabel = null; // null = implicit
        var buckets = new List<(string? Label, List<IndexEntry> Entries)>();
        // Map bucket key: empty string means implicit (null label)
        var index = new Dictionary<string, int>(Ordinal);

        string Key(string? label) => label ?? "";

        void EnsureBucket(string? label)
        {
            var key = Key(label);
            if (!index.ContainsKey(key))
            {
                index[key] = buckets.Count;
                buckets.Add((label, []));
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            var entryMatch = IndexEntryRegex().Match(line);
            if (entryMatch.Success)
            {
                EnsureBucket(currentGroupLabel);
                var title = entryMatch.Groups[1].Value.Trim();
                var href = entryMatch.Groups[2].Value.Trim();
                var desc = entryMatch.Groups[3].Success && entryMatch.Groups[3].Length > 0
                    ? entryMatch.Groups[3].Value.Trim()
                    : null;
                if (string.IsNullOrEmpty(desc))
                    desc = null;
                buckets[index[Key(currentGroupLabel)]].Entries.Add(new IndexEntry(title, href, desc));
                continue;
            }

            var h1 = H1Regex().Match(line);
            if (h1.Success)
            {
                var text = h1.Groups[1].Value.Trim();
                if (mode == "COMPAT")
                {
                    titleFromIndex ??= text;
                    // do not change currentGroupLabel
                }
                else
                {
                    currentGroupLabel = text;
                    EnsureBucket(currentGroupLabel);
                    titleFromIndex ??= text;
                }
                continue;
            }

            var h2 = H2Regex().Match(line);
            if (h2.Success)
            {
                if (mode == "COMPAT")
                {
                    currentGroupLabel = h2.Groups[1].Value.Trim();
                    EnsureBucket(currentGroupLabel);
                }
                // SPEC: H2 non-structural
                continue;
            }

            // free prose / H3+ ignored for tree
        }

        return buckets;
    }

    static string DetectMode(string[] lines)
    {
        var h1Indexes = new List<int>();
        var h2WithEntry = false;
        var entryBeforeFirstH2 = false;
        var seenH1 = false;
        var seenH2 = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (H1Regex().IsMatch(line))
            {
                h1Indexes.Add(i);
                seenH1 = true;
            }
            else if (H2Regex().IsMatch(line))
            {
                seenH2 = true;
                // check if followed by an entry before next H1/H2
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var l2 = lines[j].TrimEnd('\r');
                    if (H1Regex().IsMatch(l2) || H2Regex().IsMatch(l2))
                        break;
                    if (IndexEntryRegex().IsMatch(l2))
                    {
                        h2WithEntry = true;
                        break;
                    }
                }
            }
            else if (IndexEntryRegex().IsMatch(line))
            {
                if (seenH1 && !seenH2)
                    entryBeforeFirstH2 = true;
            }
        }

        // Compat: exactly one H1 AND ≥1 H2 followed by entry AND no IndexEntry after H1 before first H2
        if (h1Indexes.Count == 1 && h2WithEntry && !entryBeforeFirstH2)
            return "COMPAT";

        return "SPEC";
    }

    static (string Body, List<GraphBuilder.NavNode> Children) SynthesizeDir(
        string dirId,
        string bundleRoot,
        Dictionary<string, GraphBuilder.Node> nodeById,
        HashSet<string> conceptIds,
        HashSet<string> directorySet,
        Dictionary<string, GraphBuilder.NavNode> built)
    {
        var dirLabel = string.IsNullOrEmpty(dirId)
            ? new DirectoryInfo(bundleRoot).Name
            : TitleCaseSegment(dirId.Contains('/') ? dirId[(dirId.LastIndexOf('/') + 1)..] : dirId);

        var sb = new StringBuilder();
        sb.Append("# ").Append(dirLabel).Append('\n');

        var children = new List<GraphBuilder.NavNode>();

        foreach (var id in conceptIds.Where(c => IsDirectChildConcept(dirId, c)).OrderBy(c => c, Ordinal))
        {
            var concept = nodeById[id];
            var title = concept.Title ?? concept.Label ?? TitleCaseSegment(id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id);
            var file = (id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id) + ".md";
            var desc = concept.Description;
            if (!string.IsNullOrEmpty(desc))
                sb.Append("\n* [").Append(title).Append("](").Append(file).Append(") - ").Append(desc).Append('\n');
            else
                sb.Append("\n* [").Append(title).Append("](").Append(file).Append(")\n");

            children.Add(new GraphBuilder.NavNode
            {
                Kind = "concept",
                Id = id,
                Label = concept.Label ?? title,
                Description = desc,
            });
        }

        foreach (var childDirId in directorySet.Where(d => IsDirectChildDir(dirId, d)).OrderBy(d => d, Ordinal))
        {
            var seg = childDirId.Contains('/') ? childDirId[(childDirId.LastIndexOf('/') + 1)..] : childDirId;
            var title = TitleCaseSegment(seg);
            sb.Append("\n* [").Append(title).Append("](").Append(seg).Append("/)\n");

            var childDir = BuildDir(childDirId, bundleRoot, nodeById, conceptIds, directorySet, built);
            children.Add(childDir with { Label = title });
        }

        return (sb.ToString().TrimEnd() + "\n", children);
    }

    static string TitleCaseSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return segment;
        // kebab/underscore to words
        var spaced = segment.Replace('-', ' ').Replace('_', ' ');
        return TitleCasing.ToTitleCase(spaced);
    }

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex H1Regex();

    [GeneratedRegex(@"^##\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex H2Regex();

    // * [Title](url) optional " - description"
    [GeneratedRegex(@"^\*\s+\[([^\]]+)\]\(([^)]+)\)(?:\s+-\s+(.*))?$", RegexOptions.Compiled)]
    private static partial Regex IndexEntryRegex();
}
