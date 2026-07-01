using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace okf;

public static class CheckRenderer
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    public static void Render(IReadOnlyList<ValidationIssue> issues, string bundleRoot, bool linksChecked = true)
    {
        var bundleRootFull = Path.GetFullPath(bundleRoot);
        var issuesByRule = issues
            .GroupBy(issue => issue.Rule)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rules = linksChecked
            ? CheckRules.All
            : CheckRules.All.Where(rule => rule.Rule != CheckRule.InternalLinks).ToArray();

        if (issuesByRule.ContainsKey(CheckRule.BundleExists))
        {
            rules = [(CheckRule.BundleExists, GetDescription(CheckRule.BundleExists))];
        }

        foreach (var (rule, description) in rules)
        {
            if (issuesByRule.TryGetValue(rule, out var ruleIssues))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(description)}");
                foreach (var issue in ruleIssues)
                {
                    AnsiConsole.MarkupLine($"  {FormatIssue(issue, bundleRootFull)}");
                    if (issue.Snippet is not null)
                    {
                        foreach (var line in issue.Snippet.Lines)
                        {
                            AnsiConsole.MarkupLine($"    {FormatSnippetLine(line)}");
                        }
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(description)}");
            }
        }
    }

    public static void RenderJson(IReadOnlyList<ValidationIssue> issues, string bundleRoot, TextWriter writer)
    {
        var result = BuildJsonResult(issues, bundleRoot);
        writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
    }

    public static CheckJsonResult BuildJsonResult(IReadOnlyList<ValidationIssue> issues, string bundleRoot)
    {
        var bundleRootFull = Path.GetFullPath(bundleRoot);
        var issuesByRule = issues
            .GroupBy(issue => issue.Rule)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rules = CheckRules.All;
        if (issuesByRule.ContainsKey(CheckRule.BundleExists))
        {
            rules = [(CheckRule.BundleExists, GetDescription(CheckRule.BundleExists))];
        }

        var ruleResults = rules
            .Select(entry => new CheckJsonRule(
                entry.Rule,
                entry.Description,
                !issuesByRule.ContainsKey(entry.Rule)))
            .ToList();

        var issueResults = issues
            .Select(issue => ToJsonIssue(issue, bundleRootFull))
            .ToList();

        return new CheckJsonResult(
            bundleRootFull,
            issues.Count == 0,
            issues.Count,
            ruleResults,
            issueResults);
    }

    static CheckJsonIssue ToJsonIssue(ValidationIssue issue, string bundleRootFull)
    {
        var (_, displayPath) = ResolvePaths(issue.File, bundleRootFull);
        CheckJsonLocation? location = issue.Location is null
            ? null
            : new CheckJsonLocation(
                issue.Location.Line,
                issue.Location.Column,
                issue.Location.EndLine,
                issue.Location.EndColumn);

        CheckJsonSnippet? snippet = issue.Snippet is null
            ? null
            : new CheckJsonSnippet(
                issue.Snippet.Lines
                    .Select(line => new CheckJsonSnippetLine(
                        line.LineNumber,
                        line.Text,
                        line.StartColumn,
                        line.EndColumn))
                    .ToList());

        return new CheckJsonIssue(
            issue.Rule,
            displayPath,
            issue.Message,
            location,
            snippet);
    }

    static string GetDescription(CheckRule rule)
        => CheckRules.All.First(entry => entry.Rule == rule).Description;

    static string FormatSnippetLine(HighlightedSourceLine line)
        => $"[dim]{Markup.Escape(line.Text)}[/]";

    static string FormatIssue(ValidationIssue issue, string bundleRootFull)
    {
        var (fullPath, displayPath) = ResolvePaths(issue.File, bundleRootFull);
        var locationSuffix = issue.Location?.FormatSuffix() ?? string.Empty;
        var link = $"[link={fullPath}]{Markup.Escape(displayPath + locationSuffix)}[/]";
        return $"{link}: {Markup.Escape(issue.Message)}";
    }

    static (string FullPath, string DisplayPath) ResolvePaths(string file, string bundleRootFull)
    {
        if (Path.IsPathRooted(file))
        {
            var fullPath = Path.GetFullPath(file);
            return (fullPath, fullPath);
        }

        var relativePath = file.Replace('\\', '/');
        var full = Path.GetFullPath(Path.Combine(bundleRootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return (full, relativePath);
    }
}

public sealed record CheckJsonResult(
    string Path,
    bool Success,
    int Errors,
    IReadOnlyList<CheckJsonRule> Rules,
    IReadOnlyList<CheckJsonIssue> Issues);

public sealed record CheckJsonRule(
    CheckRule Rule,
    string Description,
    bool Passed);

public sealed record CheckJsonIssue(
    CheckRule Rule,
    string File,
    string Message,
    CheckJsonLocation? Location,
    CheckJsonSnippet? Snippet);

public sealed record CheckJsonLocation(
    int Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

public sealed record CheckJsonSnippet(
    IReadOnlyList<CheckJsonSnippetLine> Lines);

public sealed record CheckJsonSnippetLine(
    int LineNumber,
    string Text,
    int StartColumn,
    int EndColumn);
