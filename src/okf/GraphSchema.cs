namespace Devlooped;

/// <summary>
/// Bundled OKF graph JSON Schema (SchemaStore <c>okf-0.1</c> and later).
/// </summary>
public static class GraphSchema
{
    /// <summary>SchemaStore URL for the latest bundled graph format.</summary>
    public static string Url => UrlFor(null);

    public static string UrlFor(string? version)
        => $"https://www.schemastore.org/okf-{OkfVersions.Resolve(version)}.json";

    /// <summary>The latest schema document embedded in the tool.</summary>
    public static string Json => Get(null);

    public static string Get(string? version) => OkfVersions.Get(version).Schema;

    /// <summary>Write the bundled schema to <paramref name="path"/> (UTF-8, no BOM).</summary>
    public static void Write(string path, string? version = null)
        => OkfVersions.Write(path, Get(version));
}
