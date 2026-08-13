using System.Text.Json;
using Devlooped;

namespace Tests;

public class GraphSchemaTests
{
    [Fact]
    public void Bundled_schema_is_valid_json_with_schemastore_id()
    {
        using var doc = JsonDocument.Parse(GraphSchema.Json);
        var root = doc.RootElement;

        Assert.Equal(GraphSchema.Url, root.GetProperty("$id").GetString());
        Assert.Equal("0.1", root.GetProperty("properties").GetProperty("version").GetProperty("const").GetString());
        Assert.Equal(GraphSchema.Url, root.GetProperty("properties").GetProperty("$schema").GetProperty("const").GetString());
    }

    [Fact]
    public void Bundled_schema_matches_repo_schema_file()
    {
        var repoSchema = File.ReadAllText(FindRepoSchema());
        Assert.Equal(Normalize(repoSchema), Normalize(GraphSchema.Json));
        Assert.Equal(GraphSchema.Json, GraphSchema.Get("latest"));
        Assert.Equal(GraphSchema.Json, GraphSchema.Get("0.1"));
        Assert.Equal(GraphSchema.Json, GraphSchema.Get("v0.1"));
    }

    [Fact]
    public void Unknown_version_lists_bundled_versions()
    {
        var ex = Assert.Throws<ArgumentException>(() => GraphSchema.Get("99.0"));
        Assert.Contains("0.1", ex.Message);
        Assert.Contains("latest", ex.Message);
    }

    [Fact]
    public void Write_creates_parent_directories_and_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "okf-schema-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "nested", "okf-0.1.json");

        try
        {
            GraphSchema.Write(path);
            Assert.True(File.Exists(path));
            Assert.Equal(Normalize(GraphSchema.Json), Normalize(File.ReadAllText(path)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_rejects_existing_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "okf-schema-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var ex = Assert.Throws<IOException>(() => GraphSchema.Write(dir));
            Assert.Contains("directory", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    static string FindRepoSchema()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "schemas", "okf-0.1.json");
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException("Could not locate schemas/okf-0.1.json from " + AppContext.BaseDirectory);
    }

    static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
