namespace Devlooped;

/// <summary>Bundled OKF specification markdown, versioned alongside the graph schema.</summary>
public static class OkfSpec
{
    /// <summary>The latest spec document embedded in the tool.</summary>
    public static string Markdown => Get(null);

    public static string Get(string? version) => OkfVersions.Get(version).Spec;

    /// <summary>Write the bundled spec to <paramref name="path"/> (UTF-8, no BOM).</summary>
    public static void Write(string path, string? version = null)
        => OkfVersions.Write(path, Get(version));
}