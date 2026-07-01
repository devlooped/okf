using System.Text.RegularExpressions;

namespace okf;

/// <summary>
/// Computes short, OKF-aware abbreviations for concept IDs (and thus for edges)
/// using first letters of path segments, with smart incremental disambiguation:
/// - For multi-word segments (containing '_' or camelCase), add next word's initial.
/// - For simple segments, add next character.
/// Disambiguation is done globally per segment value but driven by concept abbr collisions.
/// </summary>
public static class ShortIds
{
    public static Dictionary<string, string> ComputeConceptAbbreviations(IEnumerable<string> conceptIds)
    {
        var idToSegments = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var allSegments = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in conceptIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var segs = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
            idToSegments[id] = segs;
            foreach (var s in segs)
            {
                allSegments.Add(s);
            }
        }

        if (allSegments.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var depths = allSegments.ToDictionary(s => s, _ => 1, StringComparer.Ordinal);

        bool stable = false;
        int guard = 0;
        while (!stable && guard++ < 20)
        {
            stable = true;

            var currentAbbrs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (id, segs) in idToSegments)
            {
                currentAbbrs[id] = string.Concat(segs.Select(s => GetSegmentAbbr(s, depths[s])));
            }

            var collisionGroups = currentAbbrs
                .GroupBy(kv => kv.Value, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in collisionGroups)
            {
                stable = false;
                var colIds = group.Select(g => g.Key).ToList();
                var segLists = colIds.Select(id => idToSegments[id]).ToList();

                int maxLen = segLists.Max(l => l.Length);
                for (int pos = 0; pos < maxLen; pos++)
                {
                    var valsAtPos = segLists
                        .Where(l => pos < l.Length)
                        .Select(l => l[pos])
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    if (valsAtPos.Count > 1)
                    {
                        foreach (var segVal in valsAtPos)
                        {
                            if (depths.ContainsKey(segVal))
                                depths[segVal]++;
                        }
                        break;
                    }
                }
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, segs) in idToSegments)
        {
            result[id] = string.Concat(segs.Select(s => GetSegmentAbbr(s, depths[s])));
        }
        return result;
    }

    private static string GetSegmentAbbr(string segment, int depth)
    {
        var words = SplitWords(segment);
        bool treatAsMulti = words.Count > 1 || segment.Contains('_', StringComparison.Ordinal);

        if (treatAsMulti)
        {
            // Take first letter of the first N words
            return new string(words.Take(depth)
                .Select(w => w.Length > 0 ? char.ToLowerInvariant(w[0]) : '_')
                .ToArray());
        }

        var clean = segment.Trim('_');
        if (clean.Length == 0) return "_";
        int len = Math.Min(depth, clean.Length);
        return clean[..len].ToLowerInvariant();
    }

    private static List<string> SplitWords(string segment)
    {
        // Split on underscores first
        var parts = segment.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var words = new List<string>();
        foreach (var part in parts)
        {
            // Split CamelCase / PascalCase (e.g. AvgPrice, closeAsOffTopic)
            var spaced = Regex.Replace(part, @"([a-z0-9])([A-Z])", "$1 $2");
            foreach (var w in spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.Length > 0) words.Add(w);
            }
        }
        if (words.Count == 0)
            words.Add(segment);
        return words;
    }
}
