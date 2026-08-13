using System.Text;

namespace Devlooped;

/// <summary>
/// Bundled OKF graph JSON Schema (SchemaStore <c>okf-0.1</c>).
/// </summary>
public static class GraphSchema
{
    public const string Url = "https://www.schemastore.org/okf-0.1.json";

    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The schema document embedded in the tool.</summary>
    public static string Json => ThisAssembly.Resources.okf_0_1.Text;

    /// <summary>Write the bundled schema to <paramref name="path"/> (UTF-8, no BOM).</summary>
    public static void Write(string path)
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

        File.WriteAllText(fullPath, Json, Utf8NoBom);
    }
}
