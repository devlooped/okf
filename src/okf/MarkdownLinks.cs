using System.Text.RegularExpressions;

namespace okf;

public static partial class MarkdownLinks
{
    [GeneratedRegex(@"\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex InlineLinkRegex();

    public static IEnumerable<(string Target, int Line)> Extract(string markdown)
    {
        var lineNumber = 1;
        foreach (var line in markdown.Split('\n'))
        {
            foreach (Match match in InlineLinkRegex().Matches(line))
            {
                yield return (match.Groups[1].Value.Trim(), lineNumber);
            }

            lineNumber++;
        }
    }

    public static bool IsInternalLink(string target)
    {
        if (target.Length == 0 || target[0] == '#')
        {
            return false;
        }

        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static bool TryResolve(
        string target,
        string sourceRelativePath,
        string bundleRoot,
        out string resolvedRelativePath)
    {
        resolvedRelativePath = string.Empty;

        var pathPart = target.Split('#', 2)[0];
        if (pathPart.Length == 0)
        {
            return false;
        }

        var sourceDirectory = Path.GetDirectoryName(sourceRelativePath) ?? string.Empty;
        string candidate;

        if (pathPart.StartsWith('/'))
        {
            candidate = pathPart.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        }
        else
        {
            var combined = Path.Combine(bundleRoot, sourceDirectory, pathPart.Replace('/', Path.DirectorySeparatorChar));
            candidate = Path.GetRelativePath(bundleRoot, Path.GetFullPath(combined));
        }

        if (pathPart.EndsWith('/'))
        {
            candidate = Path.Combine(candidate, "index.md");
        }

        resolvedRelativePath = candidate.Replace('\\', '/');
        return true;
    }

    public static bool TargetExists(string resolvedRelativePath, string bundleRoot, HashSet<string> markdownFiles)
    {
        var normalized = resolvedRelativePath.Replace('\\', '/');

        if (markdownFiles.Contains(normalized))
        {
            return true;
        }

        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var withExtension = normalized + ".md";
            if (markdownFiles.Contains(withExtension))
            {
                return true;
            }
        }

        var absolutePath = Path.Combine(bundleRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(absolutePath) || Directory.Exists(absolutePath);
    }
}
