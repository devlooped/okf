using System.Collections;
using System.Globalization;
using SharpYaml;
using SharpYaml.Serialization;

namespace okf;

public sealed class OKFDocument
{
    const string FrontmatterDelimiter = "---";

    static readonly Serializer Serializer = new();

    public IReadOnlyDictionary<string, object?> Frontmatter { get; }
    public string Body { get; }

    OKFDocument(IReadOnlyDictionary<string, object?> frontmatter, string body)
    {
        Frontmatter = frontmatter;
        Body = body;
    }

    public static bool TryParse(string text, out OKFDocument? document, out string? error)
    {
        document = null;
        error = null;

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
            error = $"Invalid YAML in frontmatter: {ex.Message}";
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

        document = new OKFDocument(frontmatter, body);
        return true;
    }

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
        var parsed = Serializer.Deserialize(yaml);
        if (parsed is null)
        {
            return new Dictionary<string, object?>();
        }

        if (parsed is not IDictionary dictionary)
        {
            throw new InvalidOperationException("Frontmatter must be a YAML mapping.");
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)!] = entry.Value;
        }

        return result;
    }
}
