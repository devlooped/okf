namespace Devlooped;

/// <summary>
/// Pure resolver for <c>okf view --out</c> dual-file destinations.
/// </summary>
public static class ViewOutputPaths
{
    public readonly record struct Paths(string JsonPath, string HtmlPath);

    /// <summary>
    /// Resolve output paths for okf.json and index.html from an optional --out argument.
    /// Extension-less paths are always treated as directories (created if missing).
    /// </summary>
    public static Paths Resolve(string bundleRoot, string? outArg)
    {
        bundleRoot = Path.GetFullPath(bundleRoot);

        if (string.IsNullOrWhiteSpace(outArg))
        {
            return new Paths(
                Path.Combine(bundleRoot, "okf.json"),
                Path.Combine(bundleRoot, "index.html"));
        }

        var path = Path.GetFullPath(outArg);
        var endsWithSep = outArg.EndsWith('/') || outArg.EndsWith('\\')
            || outArg.EndsWith(Path.DirectorySeparatorChar)
            || outArg.EndsWith(Path.AltDirectorySeparatorChar);

        if (Directory.Exists(path) || endsWithSep)
        {
            return DirPair(path);
        }

        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                dir = bundleRoot;
            return new Paths(Path.Combine(dir, "okf.json"), path);
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                dir = bundleRoot;
            return new Paths(path, Path.Combine(dir, "index.html"));
        }

        // Extension-less bare path → always directory mode
        return DirPair(path);
    }

    static Paths DirPair(string dir)
        => new(Path.Combine(dir, "okf.json"), Path.Combine(dir, "index.html"));
}
