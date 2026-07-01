using System.Text.Json;
using System.Text.RegularExpressions;
using okf;

namespace Tests;

public class BundleVisualizerTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Generates_self_contained_html()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"okf-viz-{Guid.NewGuid():N}.html");

        try
        {
            var stats = BundleVisualizer.Generate(FixturePath("valid"), outPath);

            Assert.True(File.Exists(outPath));
            var html = File.ReadAllText(outPath);

            Assert.Contains("window.BUNDLE = ", html);
            Assert.Contains("window.BUNDLE_NAME = ", html);
            Assert.Contains("cytoscape.min.js", html);
            Assert.DoesNotContain("/*__VIZ_CSS__*/", html);
            Assert.DoesNotContain("/*__VIZ_JS__*/", html);
            Assert.Equal(2, stats.Concepts);
            Assert.Equal(0, stats.Edges);
            Assert.True(stats.Bytes > 0);
        }
        finally
        {
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }
        }
    }

    [Fact]
    public void Graph_json_contains_expected_concepts()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"okf-viz-{Guid.NewGuid():N}.html");

        try
        {
            BundleVisualizer.Generate(FixturePath("valid"), outPath);
            var html = File.ReadAllText(outPath);
            var match = Regex.Match(html, @"window\.BUNDLE = (\{.*?\});", RegexOptions.Singleline);
            Assert.True(match.Success);

            using var document = JsonDocument.Parse(match.Groups[1].Value);
            var root = document.RootElement;

            var nodeIds = root.GetProperty("nodes")
                .EnumerateArray()
                .Select(node => node.GetProperty("data").GetProperty("id").GetString()!)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(["tables/customers", "tables/orders"], nodeIds);
            Assert.Equal("BigQuery Table", root.GetProperty("nodes")[0].GetProperty("data").GetProperty("type").GetString());
            Assert.True(root.GetProperty("bodies").TryGetProperty("tables/orders", out var body));
            Assert.Contains("customers", body.GetString());
        }
        finally
        {
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }
        }
    }

    [Fact]
    public void Relative_links_create_edges()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "source.md"), """
                ---
                type: Reference
                title: Source
                ---
                See [target](target.md).
                """);

            File.WriteAllText(Path.Combine(bundlePath, "target.md"), """
                ---
                type: Reference
                title: Target
                ---
                Target body.
                """);

            var outPath = Path.Combine(bundlePath, "viz.html");
            var stats = BundleVisualizer.Generate(bundlePath, outPath);

            Assert.Equal(2, stats.Concepts);
            Assert.Equal(1, stats.Edges);

            var html = File.ReadAllText(outPath);
            var match = Regex.Match(html, @"window\.BUNDLE = (\{.*?\});", RegexOptions.Singleline);
            using var document = JsonDocument.Parse(match.Groups[1].Value);
            var edge = document.RootElement.GetProperty("edges")[0].GetProperty("data");

            Assert.Equal("source", edge.GetProperty("source").GetString());
            Assert.Equal("target", edge.GetProperty("target").GetString());
        }
        finally
        {
            if (Directory.Exists(bundlePath))
            {
                Directory.Delete(bundlePath, recursive: true);
            }
        }
    }

    [Fact]
    public void Custom_name_is_embedded_in_html()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"okf-viz-{Guid.NewGuid():N}.html");

        try
        {
            BundleVisualizer.Generate(FixturePath("valid"), outPath, bundleName: "Custom Bundle Name");
            var html = File.ReadAllText(outPath);

            Assert.Contains("window.BUNDLE_NAME = \"Custom Bundle Name\";", html);
        }
        finally
        {
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }
        }
    }

    [Fact]
    public void Missing_bundle_throws()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"okf-missing-{Guid.NewGuid():N}");
        var outPath = Path.Combine(Path.GetTempPath(), $"okf-viz-{Guid.NewGuid():N}.html");

        Assert.Throws<DirectoryNotFoundException>(() =>
            BundleVisualizer.Generate(missingPath, outPath));
    }
}
