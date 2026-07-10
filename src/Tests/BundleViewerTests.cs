using Devlooped;

namespace Tests;

public class BundleViewerTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Generate_embeds_graph_and_bundle_name()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-basic"), includeBody: true, includeNav: true);
        var outPath = Path.Combine(Path.GetTempPath(), "okf-view-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            var stats = BundleViewer.Generate(graph, outPath, "Nav Basic");
            Assert.True(File.Exists(outPath));
            Assert.True(stats.Bytes > 0);
            Assert.Equal(graph.Nodes.Count, stats.Concepts);

            var html = File.ReadAllText(outPath);
            Assert.Contains("Nav Basic", html);
            Assert.Contains("\"nav\"", html);
            Assert.Contains("DOMPurify", html);
            Assert.Contains("marked@12.0.0", html);
            Assert.Contains("dompurify@3.1.6", html);
            Assert.Contains("3d-force-graph@1.73.3", html);
            Assert.Contains("graph-labels", html);
            Assert.Contains("graph-expand-btn", html);
            Assert.Contains("setGraphExpanded", html);
            Assert.Contains("buildLocalGraphData", html);
            Assert.Contains("#c/", html);
            Assert.Contains("tags-open-btn", html);
            Assert.Contains("theme-toggle", html);
            Assert.Contains("okf-theme", html);
            Assert.Contains("setTheme", html);
            Assert.Contains("tag-panel", html);
            Assert.Contains("setTagsExpanded", html);
            Assert.Contains("buildTagIndex", html);
            Assert.Contains("#t/", html);
            Assert.DoesNotContain("__GRAPH_DATA__", html);
            Assert.DoesNotContain("/*__VIEW_JS__*/", html);
        }
        finally
        {
            if (File.Exists(outPath))
                File.Delete(outPath);
        }
    }
}
