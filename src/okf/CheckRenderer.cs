using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace Devlooped;

public static class CheckRenderer
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Render(BundleCheckResult result, string bundleRoot, bool quiet = false)
    {
        if (quiet)
        {
            RenderQuiet(result, bundleRoot);
            return;
        }

        RenderFull(result, bundleRoot);
    }

    static void RenderFull(BundleCheckResult result, string bundleRoot)
    {
        var bundleRootFull = Path.GetFullPath(bundleRoot);
        var errorsByRule = result.Errors
            .GroupBy(issue => issue.Rule)
            .ToDictionary(group => group.Key, group => group.ToList());
        var warningsByRule = result.Warnings
            .GroupBy(issue => issue.Rule)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rules = CheckRules.All;
        if (errorsByRule.ContainsKey(CheckRule.BundleExists))
        {
            rules = [(CheckRule.BundleExists, GetDescription(CheckRule.BundleExists))];
        }

        foreach (var (rule, description) in rules)
        {
            if (errorsByRule.TryGetValue(rule, out var ruleIssues))
            {
                RenderErrorRule(description, ruleIssues, bundleRootFull);
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(description)}");
            }
        }

        foreach (var (rule, description) in CheckRules.Warnings)
        {
            if (warningsByRule.TryGetValue(rule, out var ruleWarnings))
            {
                RenderWarningRule(description, ruleWarnings, bundleRootFull);
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(description)}");
            }
        }
    }

    static void RenderQuiet(BundleCheckResult result, string bundleRoot)
    {
        if (result.Errors.Count == 0 && result.Warnings.Count == 0)
        {
            return;
        }

        var bundleRootFull = Path.GetFullPath(bundleRoot);
        var errorsByRule = result.Errors
            .GroupBy(issue => issue.Rule)
            .ToDictionary(group => group.Key, group => group.ToList());
        var warningsByRule = result.Warnings
            .GroupBy(issue => issue.Rule)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rules = CheckRules.All;
        if (errorsByRule.ContainsKey(CheckRule.BundleExists))
        {
            rules = [(CheckRule.BundleExists, GetDescription(CheckRule.BundleExists))];
        }

        foreach (var (rule, description) in rules)
        {
            if (errorsByRule.TryGetValue(rule, out var ruleIssues))
            {
                RenderErrorRule(description, ruleIssues, bundleRootFull);
            }
        }

        foreach (var (rule, description) in CheckRules.Warnings)
        {
            if (warningsByRule.TryGetValue(rule, out var ruleWarnings))
            {
                RenderWarningRule(description, ruleWarnings, bundleRootFull);
            }
        }
    }

    static void RenderErrorRule(string description, IReadOnlyList<ValidationIssue> issues, string bundleRootFull)
    {
        AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(description)}");
        foreach (var issue in issues)
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

    static void RenderWarningRule(string description, IReadOnlyList<ValidationIssue> issues, string bundleRootFull)
    {
        AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(description)}");
        foreach (var warning in issues)
        {
            AnsiConsole.MarkupLine($"  {FormatIssue(warning, bundleRootFull)}");
        }
    }

    public static void RenderJson(BundleCheckResult result, string bundleRoot, TextWriter writer)
    {
        var jsonResult = BuildJsonResult(result, bundleRoot);
        writer.WriteLine(JsonSerializer.Serialize(jsonResult, JsonOptions));
    }

    public static CheckJsonResult BuildJsonResult(BundleCheckResult result, string bundleRoot)
    {
        var bundleRootFull = Path.GetFullPath(bundleRoot);
        var errorsByRule = result.Errors
            .GroupBy(issue => issue.Rule)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rules = CheckRules.All;
        if (errorsByRule.ContainsKey(CheckRule.BundleExists))
        {
            rules = [(CheckRule.BundleExists, GetDescription(CheckRule.BundleExists))];
        }

        var ruleResults = rules
            .Select(entry => new CheckJsonRule(
                entry.Rule,
                entry.Description,
                !errorsByRule.ContainsKey(entry.Rule)))
            .ToList();

        var warningRuleResults = CheckRules.Warnings
            .Select(entry => new CheckJsonRule(
                entry.Rule,
                entry.Description,
                !result.Warnings.Any(issue => issue.Rule == entry.Rule)))
            .ToList();

        var issueResults = result.Errors
            .Select(issue => ToJsonIssue(issue, bundleRootFull))
            .ToList();

        var warningResults = result.Warnings
            .Select(issue => ToJsonIssue(issue, bundleRootFull))
            .ToList();

        return new CheckJsonResult(
            bundleRootFull,
            result.Errors.Count == 0,
            result.Errors.Count,
            result.Warnings.Count,
            ruleResults,
            warningRuleResults,
            issueResults,
            warningResults);
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
        => CheckRules.All
            .Concat(CheckRules.Warnings)
            .First(entry => entry.Rule == rule)
            .Description;

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
        var full = Path.GetFullPath(Path.Combine(bundleRootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace(" ", "%20");
        return (full, relativePath);
    }
}

public sealed record CheckJsonResult(
    string Path,
    bool Success,
    int Errors,
    int Warnings,
    IReadOnlyList<CheckJsonRule> Rules,
    IReadOnlyList<CheckJsonRule> WarningRules,
    IReadOnlyList<CheckJsonIssue> Issues,
    IReadOnlyList<CheckJsonIssue> WarningIssues);

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