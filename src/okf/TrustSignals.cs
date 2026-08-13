using System.Collections;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Devlooped;

/// <summary>Actor event shared by concept <c>generated</c>/<c>verified</c> and the graph document.</summary>
public sealed record ActorEvent
{
    [JsonPropertyName("by")]
    public string? By { get; init; }

    [JsonPropertyName("at")]
    public DateTimeOffset? At { get; init; }
}

public sealed record UsageWindow
{
    [JsonPropertyName("from")]
    public DateOnly? From { get; init; }

    [JsonPropertyName("to")]
    public DateOnly? To { get; init; }
}

public sealed record SourceEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("resource")]
    public string? Resource { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("usageCount")]
    public int? UsageCount { get; init; }

    [JsonPropertyName("lastModified")]
    public DateOnly? LastModified { get; init; }

    [JsonPropertyName("usageWindow")]
    public UsageWindow? UsageWindow { get; init; }
}

public sealed record ComputationParameter
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("required")]
    public bool? Required { get; init; }
}

public sealed record ExecutorContract
{
    [JsonPropertyName("resource")]
    public string? Resource { get; init; }

    [JsonPropertyName("receipt")]
    public IReadOnlyList<string>? Receipt { get; init; }
}

public sealed record AttesterContract
{
    [JsonPropertyName("resource")]
    public string? Resource { get; init; }
}

public sealed record ComputationContract
{
    public string? Runtime { get; init; }
    public IReadOnlyList<ComputationParameter>? Parameters { get; init; }
    public string? Computation { get; init; }
    public ExecutorContract? Executor { get; init; }
    public AttesterContract? Attester { get; init; }

    public static ComputationContract Parse(IReadOnlyDictionary<string, object?> frontmatter)
        => new()
        {
            Runtime = YamlValue.GetString(frontmatter, "runtime"),
            Parameters = YamlValue.GetList(frontmatter, "parameters")
                ?.Select(ParseParameter)
                .OfType<ComputationParameter>()
                .ToList() is { Count: > 0 } parameters
                ? parameters
                : null,
            Computation = YamlValue.GetString(frontmatter, "computation"),
            Executor = ParseExecutor(YamlValue.GetMap(frontmatter, "executor")),
            Attester = ParseAttester(YamlValue.GetMap(frontmatter, "attester")),
        };

    static ComputationParameter? ParseParameter(object? raw)
    {
        var map = YamlValue.AsMap(raw);
        if (map is null)
        {
            return null;
        }

        var name = YamlValue.GetString(map, "name");
        var type = YamlValue.GetString(map, "type");
        var required = YamlValue.GetBool(map, "required");
        if (name is null && type is null && required is null)
        {
            return null;
        }

        return new ComputationParameter { Name = name, Type = type, Required = required };
    }

    static ExecutorContract? ParseExecutor(IReadOnlyDictionary<string, object?>? map)
    {
        if (map is null)
        {
            return null;
        }

        var resource = YamlValue.GetString(map, "resource");
        var receipt = YamlValue.GetList(map, "receipt")
            ?.Select(YamlValue.AsString)
            .OfType<string>()
            .ToList();
        if (resource is null && (receipt is null || receipt.Count == 0))
        {
            return null;
        }

        return new ExecutorContract
        {
            Resource = resource,
            Receipt = receipt is { Count: > 0 } ? receipt : null,
        };
    }

    static AttesterContract? ParseAttester(IReadOnlyDictionary<string, object?>? map)
    {
        var resource = YamlValue.GetString(map, "resource");
        return resource is null ? null : new AttesterContract { Resource = resource };
    }
}

/// <summary>
/// OKF v0.2 §5 provenance, trust, and lifecycle helpers over raw frontmatter.
/// </summary>
public static class TrustSignals
{
    public const string Unverified = "unverified";
    public const string MachineConfirmed = "machine-confirmed";
    public const string HumanReviewed = "human-reviewed";
    public const string HumanPrefix = "human:";

    public static ActorEvent GraphProducer(DateTimeOffset at)
        => new() { By = $"okf/{OkfVersions.Latest}", At = at };

    public static IReadOnlyList<ActorEvent> NormalizeVerified(IReadOnlyDictionary<string, object?> frontmatter)
    {
        if (!frontmatter.TryGetValue("verified", out var raw) || raw is null)
        {
            return [];
        }

        return NormalizeVerifiedValue(raw);
    }

    public static IReadOnlyList<ActorEvent> NormalizeVerifiedValue(object raw)
    {
        if (YamlValue.AsMap(raw) is { } single)
        {
            return ParseActorEvent(single) is { } ev ? [ev] : [];
        }

        if (YamlValue.AsList(raw) is { } list)
        {
            return [.. list.Select(item => ParseActorEvent(YamlValue.AsMap(item))).OfType<ActorEvent>()];
        }

        return [];
    }

    public static string TrustTier(IReadOnlyList<ActorEvent> verified)
    {
        if (verified.Count == 0)
        {
            return Unverified;
        }

        foreach (var ev in verified)
        {
            if (ev.By is { } by && by.StartsWith(HumanPrefix, StringComparison.Ordinal))
            {
                return HumanReviewed;
            }
        }

        return MachineConfirmed;
    }

    public static string TrustTier(IReadOnlyDictionary<string, object?> frontmatter)
        => TrustTier(NormalizeVerified(frontmatter));

    public static ActorEvent? ParseGenerated(IReadOnlyDictionary<string, object?> frontmatter)
    {
        var map = YamlValue.GetMap(frontmatter, "generated");
        if (map is null)
        {
            return null;
        }

        var ev = ParseActorEvent(map);
        return ev?.By is null ? null : ev;
    }

    public static string? ParseStatus(IReadOnlyDictionary<string, object?> frontmatter)
        => YamlValue.GetString(frontmatter, "status");

    public static DateOnly? ParseStaleAfter(IReadOnlyDictionary<string, object?> frontmatter)
        => YamlValue.GetDateOnly(frontmatter, "stale_after");

    public static bool IsStale(DateOnly staleAfter, DateOnly? today = null)
        => (today ?? DateOnly.FromDateTime(DateTime.UtcNow)) >= staleAfter;

    public static bool IsStale(IReadOnlyDictionary<string, object?> frontmatter, DateOnly? today = null)
        => ParseStaleAfter(frontmatter) is { } date && IsStale(date, today);

    public static UsageWindow? ParseUsageWindow(IReadOnlyDictionary<string, object?> frontmatter)
        => ParseUsageWindowValue(YamlValue.GetMap(frontmatter, "usage_window"));

    public static IReadOnlyList<SourceEntry>? ParseSources(IReadOnlyDictionary<string, object?> frontmatter)
    {
        if (!frontmatter.TryGetValue("sources", out var raw) || raw is null)
        {
            return null;
        }

        var sharedWindow = ParseUsageWindow(frontmatter);
        IEnumerable<object?> entries;
        if (YamlValue.AsMap(raw) is { } single)
        {
            entries = [single];
        }
        else if (YamlValue.AsList(raw) is { } list)
        {
            entries = list;
        }
        else
        {
            return null;
        }

        var parsed = entries
            .Select(item => ParseSource(YamlValue.AsMap(item), sharedWindow))
            .OfType<SourceEntry>()
            .ToList();
        return parsed.Count > 0 ? parsed : null;
    }

    /// <summary>
    /// v0.1 fallback: links under a <c># Citations</c> heading become <see cref="SourceEntry"/> values.
    /// </summary>
    public static IReadOnlyList<SourceEntry>? ParseCitations(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var section = ExtractCitationsSection(body);
        if (section is null)
        {
            return null;
        }

        var sources = new List<SourceEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (text, target, _) in MarkdownLinks.ExtractWithText(section))
        {
            if (string.IsNullOrWhiteSpace(target) || !seen.Add(target))
            {
                continue;
            }

            sources.Add(new SourceEntry
            {
                Resource = target,
                Title = string.IsNullOrWhiteSpace(text) ? null : text,
            });
        }

        foreach (var rawLine in section.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                line = line[2..].Trim();
            }

            if (!LooksLikeBareUrl(line) || !seen.Add(line))
            {
                continue;
            }

            sources.Add(new SourceEntry { Resource = line });
        }

        return sources.Count > 0 ? sources : null;
    }

    static string? ExtractCitationsSection(string body)
    {
        var lines = body.Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();
            if (trimmed.Equals("# Citations", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("## Citations", StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var end = lines.Length;
        for (var i = start; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r');
            if (trimmed.StartsWith("# ", StringComparison.Ordinal)
                || trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        return string.Join('\n', lines[start..end]);
    }

    static bool LooksLikeBareUrl(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith('/');

    static SourceEntry? ParseSource(IReadOnlyDictionary<string, object?>? map, UsageWindow? sharedWindow)
    {
        if (map is null)
        {
            return null;
        }

        var resource = YamlValue.GetString(map, "resource");
        var id = YamlValue.GetString(map, "id");
        var title = YamlValue.GetString(map, "title");
        var author = YamlValue.GetString(map, "author");
        var usageCount = YamlValue.GetInt(map, "usage_count");
        var lastModified = YamlValue.GetDateOnly(map, "last_modified");
        var window = ParseUsageWindowValue(YamlValue.GetMap(map, "usage_window")) ?? sharedWindow;
        if (resource is null && id is null && title is null && author is null
            && usageCount is null && lastModified is null && window is null)
        {
            return null;
        }

        return new SourceEntry
        {
            Id = id,
            Resource = resource,
            Title = title,
            Author = author,
            UsageCount = usageCount,
            LastModified = lastModified,
            UsageWindow = window,
        };
    }

    static UsageWindow? ParseUsageWindowValue(IReadOnlyDictionary<string, object?>? map)
    {
        if (map is null)
        {
            return null;
        }

        var from = YamlValue.GetDateOnly(map, "from");
        var to = YamlValue.GetDateOnly(map, "to");
        return from is null && to is null ? null : new UsageWindow { From = from, To = to };
    }

    static ActorEvent? ParseActorEvent(IReadOnlyDictionary<string, object?>? map)
    {
        if (map is null)
        {
            return null;
        }

        var by = YamlValue.GetString(map, "by");
        var at = YamlValue.GetDateTimeOffset(map, "at");
        if (by is null && at is null)
        {
            return null;
        }

        return new ActorEvent { By = by, At = at };
    }
}

static class YamlValue
{
    public static IReadOnlyDictionary<string, object?>? GetMap(
        IReadOnlyDictionary<string, object?>? source, string key)
        => source is not null && source.TryGetValue(key, out var raw) ? AsMap(raw) : null;

    public static IReadOnlyList<object?>? GetList(
        IReadOnlyDictionary<string, object?>? source, string key)
        => source is not null && source.TryGetValue(key, out var raw) ? AsList(raw) : null;

    public static string? GetString(IReadOnlyDictionary<string, object?>? source, string key)
        => source is not null && source.TryGetValue(key, out var raw) ? AsString(raw) : null;

    public static bool? GetBool(IReadOnlyDictionary<string, object?>? source, string key)
        => source is not null && source.TryGetValue(key, out var raw) ? AsBool(raw) : null;

    public static int? GetInt(IReadOnlyDictionary<string, object?>? source, string key)
        => source is not null && source.TryGetValue(key, out var raw) ? AsInt(raw) : null;

    public static DateOnly? GetDateOnly(IReadOnlyDictionary<string, object?>? source, string key)
        => source is not null && source.TryGetValue(key, out var raw) ? AsDateOnly(raw) : null;

    public static DateTimeOffset? GetDateTimeOffset(IReadOnlyDictionary<string, object?>? source, string key)
        => source is not null && source.TryGetValue(key, out var raw) ? AsDateTimeOffset(raw) : null;

    public static IReadOnlyDictionary<string, object?>? AsMap(object? raw) => raw switch
    {
        null => null,
        IReadOnlyDictionary<string, object?> typed => typed,
        IDictionary<string, object?> typed => typed.ToDictionary(static kv => kv.Key, static kv => kv.Value, StringComparer.Ordinal),
        IDictionary untyped => ToStringKeyed(untyped),
        _ => null,
    };

    public static IReadOnlyList<object?>? AsList(object? raw) => raw switch
    {
        null => null,
        IReadOnlyList<object?> typed => typed,
        IList list => list.Cast<object?>().ToList(),
        _ => null,
    };

    public static string? AsString(object? raw) => raw switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s,
        _ => raw.ToString() is { Length: > 0 } s ? s : null,
    };

    public static bool? AsBool(object? raw) => raw switch
    {
        null => null,
        bool b => b,
        string s when bool.TryParse(s, out var b) => b,
        _ => null,
    };

    public static int? AsInt(object? raw) => raw switch
    {
        null => null,
        int i => i,
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        short s => s,
        byte b => b,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => null,
    };

    public static DateOnly? AsDateOnly(object? raw) => raw switch
    {
        null => null,
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
        string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
        string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            => DateOnly.FromDateTime(dto.UtcDateTime),
        _ => null,
    };

    public static DateTimeOffset? AsDateTimeOffset(object? raw) => raw switch
    {
        null => null,
        DateTimeOffset dto => dto,
        DateTime dt => dt.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
            : new DateTimeOffset(dt),
        DateOnly d => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
        string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            => dto,
        _ => null,
    };

    static Dictionary<string, object?>? ToStringKeyed(IDictionary source)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is string key)
            {
                result[key] = entry.Value;
            }
            else if (entry.Key?.ToString() is { } s)
            {
                result[s] = entry.Value;
            }
        }

        return result.Count > 0 ? result : null;
    }

}
