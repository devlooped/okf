using System.Globalization;
using System.Text.RegularExpressions;
using SharpYaml;

namespace okf;

public sealed partial class OKFDocument
{
    const string FrontmatterDelimiter = "---";

    public IReadOnlyDictionary<string, object?> Frontmatter { get; }
    public string FrontmatterYaml { get; }
    public string Body { get; }

    OKFDocument(IReadOnlyDictionary<string, object?> frontmatter, string frontmatterYaml, string body)
    {
        Frontmatter = frontmatter;
        FrontmatterYaml = frontmatterYaml;
        Body = body;
    }

    public static bool TryParse(string text, out OKFDocument? document, out string? error, out SourceSnippet? snippet)
    {
        document = null;
        error = null;
        snippet = null;

        if (text.Length == 0 || !text.StartsWith(FrontmatterDelimiter, StringComparison.Ordinal))
        {
            error = "Missing YAML frontmatter block (file must start with '---').";
            return false;
        }

        var newlineIndex = text.IndexOf('\n');
        if (newlineIndex < 0)
        {
            error = "Unterminated YAML frontmatter block.";
            return false;
        }

        var endIndex = text.IndexOf(
            $"\n{FrontmatterDelimiter}\n",
            newlineIndex + 1,
            StringComparison.Ordinal);
        if (endIndex < 0)
        {
            endIndex = text.IndexOf(
                $"\n{FrontmatterDelimiter}\r\n",
                newlineIndex + 1,
                StringComparison.Ordinal);
        }

        if (endIndex < 0)
        {
            error = "Unterminated YAML frontmatter block.";
            return false;
        }

        var frontmatterText = text[(newlineIndex + 1)..endIndex];
        IReadOnlyDictionary<string, object?> frontmatter;
        try
        {
            frontmatter = ParseYamlMapping(frontmatterText);
        }
        catch (YamlException ex)
        {
            error = GetYamlErrorMessage(ex);
            snippet = BuildYamlSnippet(frontmatterText, ex);
            return false;
        }
        catch (Exception ex)
        {
            error = $"Invalid YAML in frontmatter: {ex.Message}";
            return false;
        }

        var bodyStart = endIndex + FrontmatterDelimiter.Length + 2;
        if (bodyStart < text.Length && text[bodyStart] == '\r')
        {
            bodyStart++;
        }

        var body = bodyStart < text.Length ? text[bodyStart..] : string.Empty;
        if (body.StartsWith('\n'))
        {
            body = body[1..];
        }

        document = new OKFDocument(frontmatter, frontmatterText, body);
        return true;
    }

    public static bool TryParse(string text, out OKFDocument? document, out string? error)
        => TryParse(text, out document, out error, out _);

    public static bool HasFrontmatterBlock(string text)
    {
        if (!text.StartsWith(FrontmatterDelimiter, StringComparison.Ordinal))
        {
            return false;
        }

        var newlineIndex = text.IndexOf('\n');
        if (newlineIndex < 0)
        {
            return false;
        }

        return text.IndexOf($"\n{FrontmatterDelimiter}\n", newlineIndex + 1, StringComparison.Ordinal) >= 0
            || text.IndexOf($"\n{FrontmatterDelimiter}\r\n", newlineIndex + 1, StringComparison.Ordinal) >= 0;
    }

    public static string? GetTypeValue(IReadOnlyDictionary<string, object?> frontmatter)
    {
        if (!frontmatter.TryGetValue("type", out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value.ToString(),
        };
    }

    static IReadOnlyDictionary<string, object?> ParseYamlMapping(string yaml)
    {
        var parsed = YamlSerializer.Deserialize(yaml, ConceptDocumentYamlContext.Default.DictionaryStringObject);
        if (parsed is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return parsed.ToDictionary(static kv => kv.Key, static kv => (object?)kv.Value, StringComparer.Ordinal);
    }

    static string GetYamlErrorMessage(YamlException ex)
    {
        var message = ex.Message;
        var detailIndex = message.LastIndexOf(": ", StringComparison.Ordinal);
        if (detailIndex >= 0 && message.Contains("(Lin:", StringComparison.Ordinal))
        {
            return message[(detailIndex + 2)..];
        }

        return message;
    }

    static SourceSnippet? BuildYamlSnippet(string frontmatterText, YamlException ex)
    {
        var lines = frontmatterText
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

        var (startLine, startColumn, endLine, endColumn) = GetYamlErrorPosition(ex, frontmatterText);
        if (startLine <= 0 || startLine > lines.Length)
        {
            return null;
        }

        endLine = Math.Clamp(endLine, startLine, lines.Length);
        var highlightedLines = new List<HighlightedSourceLine>();

        for (var lineNumber = startLine; lineNumber <= endLine; lineNumber++)
        {
            var text = lines[lineNumber - 1];
            var lineStartColumn = lineNumber == startLine ? startColumn : 1;
            var lineEndColumn = lineNumber == endLine ? endColumn : text.Length;
            (lineStartColumn, lineEndColumn) = NormalizeHighlightRange(text, lineStartColumn, lineEndColumn);

            highlightedLines.Add(new HighlightedSourceLine(lineNumber, text, lineStartColumn, lineEndColumn));
        }

        return highlightedLines.Count == 0 ? null : new SourceSnippet(highlightedLines);
    }

    static (int StartColumn, int EndColumn) NormalizeHighlightRange(string text, int startColumn, int endColumn)
    {
        if (text.Length == 0)
        {
            return (1, 1);
        }

        if (startColumn <= 0)
        {
            startColumn = 1;
        }

        if (endColumn <= 0 || endColumn < startColumn || endColumn > text.Length)
        {
            var bracketIndex = text.IndexOf('[');
            if (bracketIndex >= 0)
            {
                startColumn = bracketIndex + 1;
                endColumn = text.Length;
            }
            else
            {
                startColumn = 1;
                endColumn = text.Length;
            }
        }

        startColumn = Math.Clamp(startColumn, 1, text.Length);
        endColumn = Math.Clamp(endColumn, startColumn, text.Length);
        return (startColumn, endColumn);
    }

    static (int StartLine, int StartColumn, int EndLine, int EndColumn) GetYamlErrorPosition(
        YamlException ex,
        string frontmatterText)
    {
        var rangeMatch = YamlPositionRangeRegex().Match(ex.Message);
        if (rangeMatch.Success)
        {
            var startLine = int.Parse(rangeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var startColumn = int.Parse(rangeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var startCharacter = int.Parse(rangeMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            var endLine = int.Parse(rangeMatch.Groups[4].Value, CultureInfo.InvariantCulture);
            var endColumn = int.Parse(rangeMatch.Groups[5].Value, CultureInfo.InvariantCulture);
            var endCharacter = int.Parse(rangeMatch.Groups[6].Value, CultureInfo.InvariantCulture);

            if (startColumn <= 0)
            {
                startColumn = GetColumnFromCharacterOffset(frontmatterText, startLine, startCharacter);
            }

            if (endColumn <= 0)
            {
                endColumn = GetColumnFromCharacterOffset(frontmatterText, endLine, endCharacter);
            }

            return (startLine, startColumn, endLine, endColumn);
        }

        var endLineFallback = ex.End.Line > 0 ? ex.End.Line : ex.Start.Line;
        return (ex.Start.Line, ex.Start.Column, endLineFallback, ex.End.Column);
    }

    static int GetColumnFromCharacterOffset(string text, int lineNumber, int characterOffset)
    {
        var lines = text.Split('\n');
        var lineStartOffset = 0;

        for (var index = 0; index < lineNumber - 1; index++)
        {
            lineStartOffset += lines[index].TrimEnd('\r').Length + 1;
        }

        return characterOffset - lineStartOffset + 1;
    }

    [GeneratedRegex(@"\(Lin: (\d+), Col: (\d+), Chr: (\d+)\) - \(Lin: (\d+), Col: (\d+), Chr: (\d+)\)", RegexOptions.Compiled)]
    private static partial Regex YamlPositionRangeRegex();
}
