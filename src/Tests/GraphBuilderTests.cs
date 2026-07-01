using System.Text.Json;
using okf;
using Xunit;

namespace Tests;

public class GraphBuilderTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Generates_expected_shape_and_counts_for_valid_bundle()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal(1, graph.Edges.Count); // absolute /tables/customers link from orders
        Assert.Equal("valid", graph.Bundle.Name); // dir name of fixture (lowercase)
        Assert.Equal(2, graph.Bundle.Concepts);
        Assert.True(graph.Bundle.Timestamp != default);
        Assert.NotNull(graph.Bundle.Root);
        Assert.Contains("valid", graph.Bundle.Root.Replace('\\', '/'));
    }

    [Fact]
    public void Absolute_link_produces_edge_and_correct_degrees()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        var orders = graph.Nodes.First(n => n.Id == "tables/orders");
        var customers = graph.Nodes.First(n => n.Id == "tables/customers");

        Assert.Equal(1, orders.Out);
        Assert.Equal(0, orders.In);
        Assert.Equal(1, orders.Degree);

        Assert.Equal(0, customers.Out);
        Assert.Equal(1, customers.In);
        Assert.Equal(1, customers.Degree);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal("tables/orders", edge.Source);
        Assert.Equal("tables/customers", edge.Target);
        // Link text was "customers"
        Assert.Equal("customers", edge.Label);
    }

    [Fact]
    public void Body_is_included_when_requested()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"), includeBody: true);

        var orders = graph.Nodes.First(n => n.Id == "tables/orders");
        Assert.NotNull(orders.Body);
        Assert.Contains("customers", orders.Body);

        var graphNoBody = GraphBuilder.Build(FixturePath("valid"), includeBody: false);
        var orders2 = graphNoBody.Nodes.First(n => n.Id == "tables/orders");
        Assert.Null(orders2.Body);
    }

    [Fact]
    public void Nodes_without_type_are_skipped()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), "okf-graph-debug-skip");
        if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        Directory.CreateDirectory(bundlePath);

        try
        {
            // valid
            File.WriteAllText(Path.Combine(bundlePath, "good.md"), """
                ---
                type: Reference
                title: Good
                ---
                See [other](other.md).
                """);

            // missing type -> skipped (but must parse successfully)
            File.WriteAllText(Path.Combine(bundlePath, "bad.md"), """
                ---
                title: Bad No Type
                ---

                """);

            // other (no body text, but explicit newline after closing fm so parser succeeds)
            File.WriteAllText(Path.Combine(bundlePath, "other.md"), """
                ---
                type: Reference
                title: Other
                ---

                """);

            var graph = GraphBuilder.Build(bundlePath);

            // Debug aid - write to a side file that will be easy to inspect
            var dbg = Path.Combine(bundlePath, "debug-nodes.txt");
            File.WriteAllText(dbg, "NODES:" + string.Join(",", graph.Nodes.Select(n => n.Id + "(" + n.Type + ")")) + "\nFILES:" + string.Join(",", Directory.GetFiles(bundlePath, "*.md").Select(Path.GetFileName)));

            Assert.Equal(2, graph.Nodes.Count);
            Assert.Equal(1, graph.Edges.Count);
            Assert.Contains(graph.Nodes, n => n.Id == "good");
            Assert.Contains(graph.Nodes, n => n.Id == "other");
            Assert.DoesNotContain(graph.Nodes, n => n.Id == "bad");
            Assert.DoesNotContain(graph.Nodes, n => n.Id == "bad");
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Label_falls_back_to_title_or_id_when_link_text_missing()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "source.md"), """
                ---
                type: Reference
                title: Source Title
                ---
                See [](target.md).
                """);

            File.WriteAllText(Path.Combine(bundlePath, "target.md"), """
                ---
                type: Reference
                title: The Real Target Title
                ---
                Target content.
                """);

            var graph = GraphBuilder.Build(bundlePath);

            Assert.Equal(1, graph.Edges.Count);
            var edge = graph.Edges[0];
            // text was empty -> fallback to title
            Assert.Equal("The Real Target Title", edge.Label);
            Assert.Equal("source", edge.Source);
            Assert.Equal("target", edge.Target);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Type_is_lifted_to_node_level_not_duplicated_in_meta()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        foreach (var node in graph.Nodes)
        {
            Assert.False(string.IsNullOrWhiteSpace(node.Type));
            Assert.DoesNotContain(node.Meta, kvp => string.Equals(kvp.Key, "type", StringComparison.Ordinal));
        }

        var orders = graph.Nodes.First(n => n.Id == "tables/orders");
        Assert.Equal("BigQuery Table", orders.Type);
        Assert.Equal("Orders", orders.Meta["title"]);
    }

    [Fact]
    public void Title_is_copied_to_node_label_when_present()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        var orders = graph.Nodes.First(n => n.Id == "tables/orders");
        Assert.Equal("Orders", orders.Label);
        Assert.Equal("Orders", orders.Meta["title"]);

        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "untitled.md"), """
                ---
                type: Reference
                ---
                No title here.
                """);

            var untitledGraph = GraphBuilder.Build(bundlePath);
            var untitled = Assert.Single(untitledGraph.Nodes);
            Assert.Null(untitled.Label);
            Assert.DoesNotContain(untitled.Meta, kvp => string.Equals(kvp.Key, "title", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Produces_valid_json_with_lowercase_keys()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));
        var json = System.Text.Json.JsonSerializer.Serialize(graph, new JsonSerializerOptions { WriteIndented = true });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("bundle", out var b));
        Assert.True(b.TryGetProperty("root", out _));
        Assert.True(b.TryGetProperty("timestamp", out _));
        Assert.True(b.TryGetProperty("concepts", out _));

        var firstNode = root.GetProperty("nodes")[0];
        Assert.True(firstNode.TryGetProperty("id", out _));
        Assert.True(firstNode.TryGetProperty("path", out _));
        Assert.True(firstNode.TryGetProperty("meta", out _));
        Assert.True(firstNode.TryGetProperty("label", out _));
        Assert.True(firstNode.TryGetProperty("in", out _));
        Assert.True(firstNode.TryGetProperty("out", out _));

        var edge = root.GetProperty("edges")[0];
        Assert.True(edge.TryGetProperty("source", out _));
        Assert.True(edge.TryGetProperty("target", out _));
        Assert.True(edge.TryGetProperty("label", out _));
    }
}
