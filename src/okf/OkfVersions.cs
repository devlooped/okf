using System.Collections.Frozen;
using System.Text;

namespace Devlooped;

/// <summary>
/// Bundled OKF format versions (spec + graph schema). <c>latest</c> is the
/// highest version shipped with this tool build.
/// </summary>
public static class OkfVersions
{
    public const string Latest = "0.1";

    static readonly FrozenDictionary<string, Documents> Bundled =
        new Dictionary<string, Documents>(StringComparer.OrdinalIgnoreCase)
        {
            ["0.1"] = new(ThisAssembly.Resources.okf_0_1.Text, ThisAssembly.Resources.Specs.okf_0_1.Text),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static IReadOnlyCollection<string> All { get; } = Bundled.Keys;

    public static string Resolve(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) ||
            version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return Latest;
        }

        var normalized = version.Trim().TrimStart('v', 'V');
        foreach (var key in Bundled.Keys)
        {
            if (key.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        throw new ArgumentException(
            $"Unknown OKF version '{version}'. Bundled: {string.Join(", ", All.Order(StringComparer.Ordinal))} (or 'latest').");
    }

    public static Documents Get(string? version)
    {
        var resolved = Resolve(version);
        return Bundled[resolved];
    }

    public static void Write(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            throw new IOException($"Output path is a directory: {fullPath}");
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, content, Utf8NoBom);
    }

    public readonly record struct Documents(string Schema, string Spec);
}
