using System.Globalization;
using System.Text.RegularExpressions;

namespace okf;

public sealed partial class BundleChecker
{
    static readonly HashSet<string> ReservedFilenames = new(StringComparer.OrdinalIgnoreCase)
    {
        "index.md",
        "log.md",
    };

    static readonly HashSet<string> RootIndexFrontmatterKeys = new(StringComparer.Ordinal)
    {
        "okf_version",
    };

    readonly string bundleRoot;
    readonly List<ValidationIssue> issues = [];

    public BundleChecker(string bundlePath)
    {
        bundleRoot = Path.GetFullPath(bundlePath);
    }

    public IReadOnlyList<ValidationIssue> Check(bool validateLinks = true)
    {
        issues.Clear();

        if (!Directory.Exists(bundleRoot))
        {
            issues.Add(new ValidationIssue(CheckRule.BundleExists, bundleRoot, "Bundle directory not found."));
            return issues;
        }

        var markdownFiles = Directory
            .EnumerateFiles(bundleRoot, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(bundleRoot, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in markdownFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var absolutePath = Path.Combine(bundleRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(absolutePath);
            var fileName = Path.GetFileName(relativePath);

            if (fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase))
            {
                ValidateIndex(relativePath, text, validateLinks, markdownFiles);
            }
            else if (fileName.Equals("log.md", StringComparison.OrdinalIgnoreCase))
            {
                ValidateLog(relativePath, text, validateLinks, markdownFiles);
            }
            else
            {
                ValidateConcept(relativePath, text, validateLinks, markdownFiles);
            }
        }

        return issues;
    }

    void ValidateConcept(string relativePath, string text, bool validateLinks, HashSet<string> markdownFiles)
    {
        if (!OKFDocument.TryParse(text, out var document, out var error, out var snippet))
        {
            AddError(
                CheckRule.ConceptFrontmatter,
                relativePath,
                error!,
                IssueLocation.FromYamlSnippet(text, snippet),
                snippet);
            return;
        }

        var type = OKFDocument.GetTypeValue(document!.Frontmatter);
        if (string.IsNullOrWhiteSpace(type))
        {
            AddError(CheckRule.ConceptType, relativePath, "Frontmatter must contain a non-empty 'type' field.");
        }

        if (validateLinks)
        {
            ValidateLinks(relativePath, document.Body, markdownFiles);
        }
    }

    void ValidateIndex(string relativePath, string text, bool validateLinks, HashSet<string> markdownFiles)
    {
        var isBundleRoot = relativePath.Equals("index.md", StringComparison.OrdinalIgnoreCase);
        string body;

        if (OKFDocument.HasFrontmatterBlock(text))
        {
            if (!isBundleRoot)
            {
                AddError(CheckRule.IndexFrontmatter, relativePath, "index.md must not contain frontmatter.");
                body = text;
            }
            else if (!OKFDocument.TryParse(text, out var document, out var error, out var snippet))
            {
                AddError(
                    CheckRule.IndexFrontmatter,
                    relativePath,
                    error!,
                    IssueLocation.FromYamlSnippet(text, snippet),
                    snippet);
                body = text;
            }
            else
            {
                var unexpectedKeys = document!.Frontmatter.Keys
                    .Where(key => !RootIndexFrontmatterKeys.Contains(key))
                    .ToArray();
                if (unexpectedKeys.Length > 0)
                {
                    AddError(
                        CheckRule.IndexFrontmatter,
                        relativePath,
                        $"Bundle-root index.md frontmatter may only contain 'okf_version'; unexpected keys: {string.Join(", ", unexpectedKeys)}.");
                }

                body = document.Body;
            }
        }
        else
        {
            body = text;
        }

        ValidateIndexBody(relativePath, body);

        if (validateLinks)
        {
            ValidateLinks(relativePath, body, markdownFiles);
        }
    }

    void ValidateIndexBody(string relativePath, string body)
    {
        var hasSection = false;
        var hasEntry = false;
        var lineNumber = 1;

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (SectionHeadingRegex().IsMatch(line))
            {
                hasSection = true;
            }
            else if (IndexEntryRegex().IsMatch(line))
            {
                hasEntry = true;
            }
            else if (line.StartsWith("* ", StringComparison.Ordinal) && line.Contains("]("))
            {
                AddError(CheckRule.IndexStructure, relativePath, "Index entry must use the form '* [Title](url) - description'.", new IssueLocation(lineNumber));
            }

            lineNumber++;
        }

        if (!hasSection)
        {
            AddError(CheckRule.IndexStructure, relativePath, "index.md must contain at least one section heading ('# …').");
        }

        if (!hasEntry)
        {
            AddError(CheckRule.IndexStructure, relativePath, "index.md must contain at least one list entry linking to a concept or subdirectory.");
        }
    }

    void ValidateLog(string relativePath, string text, bool validateLinks, HashSet<string> markdownFiles)
    {
        if (OKFDocument.HasFrontmatterBlock(text))
        {
            AddError(CheckRule.LogFormat, relativePath, "log.md must not contain frontmatter.");
        }

        ValidateLogBody(relativePath, text);

        if (validateLinks)
        {
            ValidateLinks(relativePath, text, markdownFiles);
        }
    }

    void ValidateLogBody(string relativePath, string body)
    {
        var hasDateSection = false;
        var lineNumber = 1;
        var inDateSection = false;

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var dateMatch = LogDateHeadingRegex().Match(line);
            if (dateMatch.Success)
            {
                hasDateSection = true;
                inDateSection = true;

                if (!DateOnly.TryParseExact(
                        dateMatch.Groups[1].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out _))
                {
                    AddError(CheckRule.LogFormat, relativePath, "Log date heading must use ISO 8601 YYYY-MM-DD form.", new IssueLocation(lineNumber));
                }
            }
            else if (inDateSection && line.StartsWith("* ", StringComparison.Ordinal))
            {
                // Valid log entry.
            }
            else if (inDateSection && line.Length > 0 && !line.StartsWith('#'))
            {
                AddError(CheckRule.LogFormat, relativePath, "Log entries must be list items under a date heading.", new IssueLocation(lineNumber));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal) && !dateMatch.Success)
            {
                AddError(CheckRule.LogFormat, relativePath, "Log section headings must use ISO 8601 YYYY-MM-DD form.", new IssueLocation(lineNumber));
            }

            lineNumber++;
        }

        if (!hasDateSection)
        {
            AddError(CheckRule.LogFormat, relativePath, "log.md must contain at least one date heading ('## YYYY-MM-DD').");
        }
    }

    void ValidateLinks(string sourceRelativePath, string body, HashSet<string> markdownFiles)
    {
        foreach (var (target, line) in MarkdownLinks.Extract(body))
        {
            if (!MarkdownLinks.IsInternalLink(target))
            {
                continue;
            }

            if (!MarkdownLinks.TryResolve(target, sourceRelativePath, bundleRoot, out var resolved))
            {
                continue;
            }

            if (!MarkdownLinks.TargetExists(resolved, bundleRoot, markdownFiles))
            {
                AddError(
                    CheckRule.InternalLinks,
                    sourceRelativePath,
                    $"Broken link to '{target}' (resolved to '{resolved}').",
                    new IssueLocation(line));
            }
        }
    }

    void AddError(CheckRule rule, string file, string message, IssueLocation? location = null, SourceSnippet? snippet = null)
        => issues.Add(new ValidationIssue(rule, file, message, location, snippet));

    [GeneratedRegex(@"^# .+", RegexOptions.Compiled)]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"^\* \[.+?\]\(.+?\)(?: .+)?$", RegexOptions.Compiled)]
    private static partial Regex IndexEntryRegex();

    [GeneratedRegex(@"^## (\d{4}-\d{2}-\d{2})\s*$", RegexOptions.Compiled)]
    private static partial Regex LogDateHeadingRegex();
}
