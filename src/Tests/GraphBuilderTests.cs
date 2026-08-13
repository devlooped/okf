using System.Text.Json;
using Devlooped;
using Xunit;

namespace Tests;

public class GraphBuilderTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    static string? ExtensionString(GraphBuilder.Node node, string key)
        => node.ExtensionData?.TryGetValue(key, out var element) == true
            && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    static bool HasExtensionKey(GraphBuilder.Node node, string key)
        => node.ExtensionData?.ContainsKey(key) == true;

    static string? BundleExtensionString(GraphBuilder.Bundle bundle, string key)
        => bundle.ExtensionData?.TryGetValue(key, out var element) == true
            && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    [Fact]
    public void Attested_sample_has_spec_trust_tiers_and_stale_profit()
    {
        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "attested"));
        Assert.True(Directory.Exists(sample), sample);

        var check = new BundleChecker(sample).Check();
        Assert.Empty(check.Errors);
        Assert.Empty(check.Warnings);

        var graph = GraphBuilder.Build(sample);
        var income = graph.Nodes.First(n => n.Id == "metrics/income-statement");
        var revenue = graph.Nodes.First(n => n.Id == "computations/revenue");
        var profit = graph.Nodes.First(n => n.Id == "computations/profit");
        var legacy = graph.Nodes.First(n => n.Id == "metrics/legacy-handbook");

        Assert.Equal(TrustSignals.HumanReviewed, income.TrustTier);
        Assert.Equal(2, income.Verified!.Count);
        Assert.Equal(TrustSignals.HumanReviewed, revenue.TrustTier);
        Assert.False(revenue.Stale);
        Assert.Equal(TrustSignals.MachineConfirmed, profit.TrustTier);
        Assert.True(profit.Stale);
        Assert.Equal(TrustSignals.Unverified, legacy.TrustTier);
        Assert.Null(legacy.Generated);
        Assert.Equal("https://wiki.acme/finance/fpa-handbook", Assert.Single(legacy.Sources!).Resource);
    }

    [Fact]
    public void Generates_expected_shape_and_counts_for_valid_bundle()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Single(graph.Edges); // absolute /tables/customers link from orders
        Assert.NotNull(graph.Timestamp);
        Assert.Null(graph.Bundle);
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
        Assert.Equal("to_tc", edge.Id);
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
            Assert.Single(graph.Edges);
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

            Assert.Single(graph.Edges);
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
    public void Type_is_lifted_to_node_level_not_duplicated_in_extension_data()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        foreach (var node in graph.Nodes)
        {
            Assert.False(string.IsNullOrWhiteSpace(node.Type));
            Assert.False(HasExtensionKey(node, "type"));
        }

        var orders = graph.Nodes.First(n => n.Id == "tables/orders");
        Assert.Equal("BigQuery Table", orders.Type);
        Assert.Equal("One row per order.", orders.Description);
        Assert.Equal("Orders", orders.Title);
        Assert.False(HasExtensionKey(orders, "title"));
        Assert.False(HasExtensionKey(orders, "description"));
    }

    [Fact]
    public void Label_is_lifted_to_node_level_not_duplicated_in_extension_data()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "labeled.md"), """
                ---
                type: Reference
                label: Display Name
                ---
                Content.
                """);

            File.WriteAllText(Path.Combine(bundlePath, "unlabeled.md"), """
                ---
                type: Reference
                title: Title Only
                ---
                Content.
                """);

            var graph = GraphBuilder.Build(bundlePath);

            var labeled = graph.Nodes.First(n => n.Id == "labeled");
            Assert.Equal("Display Name", labeled.Label);
            Assert.False(HasExtensionKey(labeled, "label"));

            var unlabeled = graph.Nodes.First(n => n.Id == "unlabeled");
            Assert.Equal("Unlabeled", unlabeled.Label);
            Assert.False(HasExtensionKey(unlabeled, "label"));
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Frontmatter_fields_are_lifted_to_node_level_not_duplicated_in_extension_data()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "full.md"), """
                ---
                type: BigQuery Table
                description: One row per order.
                resource: https://example.com/orders
                tags: [sales, orders]
                timestamp: 2026-05-28T14:30:00Z
                ---
                Content.
                """);

            File.WriteAllText(Path.Combine(bundlePath, "sparse.md"), """
                ---
                type: Reference
                ---
                Content.
                """);

            var graph = GraphBuilder.Build(bundlePath);

            var full = graph.Nodes.First(n => n.Id == "full");
            Assert.Equal("One row per order.", full.Description);
            Assert.Equal("https://example.com/orders", full.Resource);
            Assert.Equal(["sales", "orders"], full.Tags);
            Assert.Equal(DateTimeOffset.Parse("2026-05-28T14:30:00+00:00"), full.Timestamp);
            Assert.False(HasExtensionKey(full, "description"));
            Assert.False(HasExtensionKey(full, "resource"));
            Assert.False(HasExtensionKey(full, "tags"));
            Assert.False(HasExtensionKey(full, "timestamp"));

            var sparse = graph.Nodes.First(n => n.Id == "sparse");
            Assert.Null(sparse.Description);
            Assert.Null(sparse.Resource);
            Assert.Null(sparse.Tags);
            Assert.Null(sparse.Timestamp);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Timestamp_is_lifted_to_node_level_not_duplicated_in_extension_data()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "stamped.md"), """
                ---
                type: Reference
                timestamp: 2026-05-28T14:30:00Z
                ---
                Content.
                """);

            File.WriteAllText(Path.Combine(bundlePath, "unstamped.md"), """
                ---
                type: Reference
                ---
                Content.
                """);

            var graph = GraphBuilder.Build(bundlePath);

            var stamped = graph.Nodes.First(n => n.Id == "stamped");
            Assert.Equal(DateTimeOffset.Parse("2026-05-28T14:30:00+00:00"), stamped.Timestamp);
            Assert.False(HasExtensionKey(stamped, "timestamp"));

            var unstamped = graph.Nodes.First(n => n.Id == "unstamped");
            Assert.Null(unstamped.Timestamp);
            Assert.False(HasExtensionKey(unstamped, "timestamp"));
            Assert.Equal(TrustSignals.Unverified, unstamped.TrustTier);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void V02_trust_and_provenance_are_lifted_not_duplicated_in_extension_data()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "revenue.md"), """
                ---
                type: Attested Computation
                generated: { by: grok-4.6, at: 2026-06-20T22:53:05Z }
                verified:
                  - { by: process:finance-nightly, at: 2026-06-21T02:00:00Z }
                  - { by: human:ahormati, at: 2026-06-25T09:00:00Z }
                status: stable
                stale_after: 2020-01-01
                runtime: bigquery
                parameters:
                  - { name: year, type: integer, required: true }
                executor:
                  resource: references/run.md
                  receipt: [job_id, executed_sql]
                attester:
                  resource: references/attest.py
                sources:
                  - id: rev-policy
                    resource: https://wiki.acme/finance/revenue
                    title: Revenue recognition policy
                    author: team:finance
                    usage_count: 12
                    last_modified: 2026-04-02
                usage_window: { from: 2026-06-01, to: 2026-06-30 }
                ---
                # Computation

                SELECT 1
                """);

            File.WriteAllText(Path.Combine(bundlePath, "legacy.md"), """
                ---
                type: Metric
                timestamp: 2026-05-28T14:30:00Z
                ---
                See the handbook.

                # Citations
                [1] [Handbook](https://wiki.acme/handbook)
                """);

            var graph = GraphBuilder.Build(bundlePath);
            Assert.Equal("okf/0.2", graph.Generated!.By);
            Assert.Equal(graph.Timestamp, graph.Generated.At);

            var revenue = graph.Nodes.First(n => n.Id == "revenue");
            Assert.Equal("grok-4.6", revenue.Generated!.By);
            Assert.Equal(DateTimeOffset.Parse("2026-06-20T22:53:05Z"), revenue.Timestamp);
            Assert.Equal(2, revenue.Verified!.Count);
            Assert.Equal(TrustSignals.HumanReviewed, revenue.TrustTier);
            Assert.Equal("stable", revenue.Status);
            Assert.Equal(new DateOnly(2020, 1, 1), revenue.StaleAfter);
            Assert.True(revenue.Stale);
            Assert.Equal("bigquery", revenue.Runtime);
            Assert.Equal("year", Assert.Single(revenue.Parameters!).Name);
            Assert.Equal("references/run.md", revenue.Executor!.Resource);
            Assert.Equal(["job_id", "executed_sql"], revenue.Executor.Receipt);
            Assert.Equal("references/attest.py", revenue.Attester!.Resource);
            var source = Assert.Single(revenue.Sources!);
            Assert.Equal("rev-policy", source.Id);
            Assert.Equal(12, source.UsageCount);
            Assert.Equal(new DateOnly(2026, 6, 1), revenue.UsageWindow!.From);
            Assert.False(HasExtensionKey(revenue, "generated"));
            Assert.False(HasExtensionKey(revenue, "verified"));
            Assert.False(HasExtensionKey(revenue, "sources"));
            Assert.False(HasExtensionKey(revenue, "status"));
            Assert.False(HasExtensionKey(revenue, "stale_after"));

            var legacy = graph.Nodes.First(n => n.Id == "legacy");
            Assert.Null(legacy.Generated);
            Assert.Equal(DateTimeOffset.Parse("2026-05-28T14:30:00+00:00"), legacy.Timestamp);
            Assert.Equal(TrustSignals.Unverified, legacy.TrustTier);
            var citation = Assert.Single(legacy.Sources!);
            Assert.Equal("https://wiki.acme/handbook", citation.Resource);
            Assert.Equal("Handbook", citation.Title);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Title_is_lifted_to_node_level_not_duplicated_in_extension_data()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        var orders = graph.Nodes.First(n => n.Id == "tables/orders");
        Assert.Equal("Orders", orders.Label);
        Assert.Equal("Orders", orders.Title);
        Assert.False(HasExtensionKey(orders, "title"));

        var customers = graph.Nodes.First(n => n.Id == "tables/customers");
        Assert.Equal("customers", customers.Label);
        Assert.Equal("Customers", customers.Title);
        Assert.False(HasExtensionKey(customers, "title"));

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
            Assert.Equal("Untitled", untitled.Label);
            Assert.Null(untitled.Title);
            Assert.False(HasExtensionKey(untitled, "title"));
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Label_uses_index_link_text_when_concept_has_no_label()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);
        Directory.CreateDirectory(Path.Combine(bundlePath, "tables"));

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "index.md"), """
                # Tables

                * [Customer Records](tables/customers.md) - Customer master data.
                """);

            File.WriteAllText(Path.Combine(bundlePath, "tables", "customers.md"), """
                ---
                type: BigQuery Table
                title: Customers
                ---
                Customer data.
                """);

            var graph = GraphBuilder.Build(bundlePath);
            var customers = Assert.Single(graph.Nodes);

            Assert.Equal("Customer Records", customers.Label);
            Assert.Equal("Customers", customers.Title);
            Assert.False(HasExtensionKey(customers, "title"));
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Label_uses_shortest_incoming_link_text_when_index_missing()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "source.md"), """
                ---
                type: Reference
                ---
                See [the target concept](target.md) and [real target](target.md).
                """);

            File.WriteAllText(Path.Combine(bundlePath, "target.md"), """
                ---
                type: Reference
                title: Target Title
                ---
                Target body.
                """);

            var graph = GraphBuilder.Build(bundlePath);
            var target = graph.Nodes.First(n => n.Id == "target");

            // "real target" and "the target concept" are both collected; shortest is "real target"
            // Echo-style link texts like [target](target.md) are no longer considered label candidates.
            Assert.Equal("real target", target.Label);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Label_disambiguates_titlecased_id_fallback_with_parents()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(bundlePath, "family"));
        Directory.CreateDirectory(Path.Combine(bundlePath, "company"));

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "family", "account.md"), """
                ---
                type: Reference
                ---
                Family account.
                """);

            File.WriteAllText(Path.Combine(bundlePath, "company", "account.md"), """
                ---
                type: Reference
                ---
                Company account.
                """);

            var graph = GraphBuilder.Build(bundlePath);

            Assert.Equal("Account (Family)", graph.Nodes.First(n => n.Id == "family/account").Label);
            Assert.Equal("Account (Company)", graph.Nodes.First(n => n.Id == "company/account").Label);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Index_with_frontmatter_still_provides_labels()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "index.md"), """
                ---
                okf_version: "0.1"
                ---

                # Concepts

                * [Playbook Entry](playbook.md) - A playbook.
                """);

            File.WriteAllText(Path.Combine(bundlePath, "playbook.md"), """
                ---
                type: Playbook
                ---
                Steps go here.
                """);

            var graph = GraphBuilder.Build(bundlePath);
            var playbook = Assert.Single(graph.Nodes);

            Assert.Equal("Playbook Entry", playbook.Label);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Build_appends_bundle_properties_as_extension_data_strings()
    {
        var graph = GraphBuilder.Build(
            FixturePath("valid"),
            bundleProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["producer"] = "acme-agent",
                ["build"] = "42",
            });

        Assert.Equal("acme-agent", BundleExtensionString(graph.Bundle!, "producer"));
        Assert.Equal("42", BundleExtensionString(graph.Bundle!, "build"));
    }

    [Fact]
    public void Produces_valid_json_with_lowercase_keys()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));
        var json = System.Text.Json.JsonSerializer.Serialize(graph, GraphBuilder.JsonOptions);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("$schema", out var schema));
        Assert.Equal(GraphSchema.Url, schema.GetString());

        Assert.True(root.TryGetProperty("version", out var v));
        Assert.Equal("0.2", v.GetString());

        Assert.True(root.TryGetProperty("timestamp", out _));
        Assert.True(root.TryGetProperty("generated", out var generated));
        Assert.Equal("okf/0.2", generated.GetProperty("by").GetString());
        Assert.True(generated.TryGetProperty("at", out _));
        Assert.False(root.TryGetProperty("bundle", out _));  // no -p properties provided, so bundle is omitted

        var firstNode = root.GetProperty("nodes")[0];
        Assert.True(firstNode.TryGetProperty("id", out _));
        Assert.True(firstNode.TryGetProperty("path", out _));
        Assert.True(firstNode.TryGetProperty("title", out _));
        Assert.True(firstNode.TryGetProperty("description", out _));
        Assert.True(firstNode.TryGetProperty("label", out _));
        Assert.False(firstNode.TryGetProperty("meta", out _));
        Assert.True(firstNode.TryGetProperty("in", out _));
        Assert.True(firstNode.TryGetProperty("out", out _));
        Assert.True(firstNode.TryGetProperty("weight", out _));
        Assert.True(firstNode.TryGetProperty("rank", out _));

        var edge = root.GetProperty("edges")[0];
        Assert.True(edge.TryGetProperty("source", out _));
        Assert.True(edge.TryGetProperty("target", out _));
        Assert.True(edge.TryGetProperty("label", out _));
        Assert.True(edge.TryGetProperty("id", out _));
    }

    [Fact]
    public void Build_assigns_page_rank_weight_in_zero_to_one_range()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"));

        foreach (var node in graph.Nodes)
        {
            Assert.NotNull(node.Weight);
            Assert.InRange(node.Weight!.Value, 0.0, 1.0);
            Assert.NotNull(node.Rank);
            Assert.True(node.Rank >= 1);
        }

        Assert.Equal(1.0, graph.Nodes.Sum(n => n.Weight!.Value), precision: 6);

        var orders = graph.Nodes.First(n => n.Id == "tables/orders");
        var customers = graph.Nodes.First(n => n.Id == "tables/customers");
        Assert.True(customers.Weight > orders.Weight);
        Assert.True(customers.Rank < orders.Rank);
    }

    [Fact]
    public void Build_assigns_equal_weight_when_graph_has_no_edges()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "a.md"), """
                ---
                type: Reference
                ---
                A.
                """);

            File.WriteAllText(Path.Combine(bundlePath, "b.md"), """
                ---
                type: Reference
                ---
                B.
                """);

            var graph = GraphBuilder.Build(bundlePath);

            Assert.Equal(2, graph.Nodes.Count);
            Assert.Empty(graph.Edges);
            Assert.All(graph.Nodes, n =>
            {
                Assert.Equal(0.5, n.Weight!.Value, precision: 6);
                Assert.Equal(1, n.Rank);
            });
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Generate_writes_javascript_script_with_window_global_when_requested()
    {
        var bundlePath = FixturePath("valid");
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.js");

        try
        {
            GraphBuilder.Generate(bundlePath, graphPath, script: true);
            var content = File.ReadAllText(graphPath);

            Assert.StartsWith("/*", content);
            Assert.Contains("const data = {", content);
            Assert.Contains("window.data = data;", content);

            // Extract the JSON object assigned to data (script wrapper uses camelCase keys)
            var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
            var dataStart = normalized.IndexOf("const data = ", StringComparison.Ordinal);
            Assert.True(dataStart >= 0);
            // Find the object literal robustly (skip ws after =, take balanced-ish from first { to matching before ;)
            int objStart = normalized.IndexOf('{', dataStart);
            Assert.True(objStart >= 0);
            // find the ; after the object close for this assignment (before window.data or end)
            int windowIdx = normalized.IndexOf("window.data = data;", objStart, StringComparison.Ordinal);
            int searchEnd = windowIdx > 0 ? windowIdx : normalized.Length;
            int objEnd = normalized.LastIndexOf('}', searchEnd - 1);
            Assert.True(objEnd > objStart);
            var json = normalized.Substring(objStart, objEnd - objStart + 1);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal(GraphSchema.Url, root.GetProperty("$schema").GetString());
            Assert.Equal("0.2", root.GetProperty("version").GetString());
            Assert.Equal(2, root.GetProperty("nodes").GetArrayLength());
            Assert.Equal(1, root.GetProperty("edges").GetArrayLength());
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Graph_command_skips_output_when_bundle_has_errors()
    {
        var bundlePath = FixturePath("missing-type");
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            var checkResult = new BundleChecker(bundlePath).Check();
            Assert.NotEmpty(checkResult.Errors);

            if (checkResult.Errors.Count == 0)
            {
                GraphBuilder.Generate(bundlePath, graphPath);
            }

            Assert.False(File.Exists(graphPath));
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Graph_command_json_output_matches_check_for_broken_link()
    {
        var fixturePath = FixturePath("broken-link");
        var checkResult = new BundleChecker(fixturePath).Check();
        var checkJson = CheckRenderer.BuildJsonResult(checkResult, fixturePath);

        Assert.True(checkJson.Success);
        Assert.Equal(1, checkJson.Warnings);
        Assert.Contains(checkJson.WarningIssues, issue => issue.Message.Contains("Unresolved link"));
    }

    [Fact]
    public void Graph_command_json_skips_output_when_bundle_has_errors()
    {
        var bundlePath = FixturePath("missing-type");
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            var checkResult = new BundleChecker(bundlePath).Check();
            Assert.NotEmpty(checkResult.Errors);

            if (checkResult.Errors.Count == 0)
            {
                GraphBuilder.Generate(bundlePath, graphPath);
            }

            Assert.False(File.Exists(graphPath));
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Graph_command_generates_output_when_bundle_has_only_warnings()
    {
        var bundlePath = FixturePath("broken-link");
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            var checkResult = new BundleChecker(bundlePath).Check();
            Assert.Empty(checkResult.Errors);
            Assert.NotEmpty(checkResult.Warnings);

            if (checkResult.Errors.Count == 0)
            {
                GraphBuilder.Generate(bundlePath, graphPath);
            }

            Assert.True(File.Exists(graphPath));
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Load_round_trips_generated_graph()
    {
        var bundlePath = FixturePath("valid");
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            GraphBuilder.Generate(bundlePath, graphPath);
            var loaded = GraphBuilder.Load(graphPath);

            Assert.Equal(2, loaded.Nodes.Count);
            Assert.Single(loaded.Edges);
            Assert.False(string.IsNullOrWhiteSpace(loaded.Edges[0].Id));
            Assert.All(loaded.Nodes, n =>
            {
                Assert.NotNull(n.Weight);
                Assert.NotNull(n.Rank);
            });
            Assert.NotNull(loaded.Timestamp);
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Load_assigns_weight_when_node_weight_is_missing()
    {
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(graphPath, """
                {
                  "version": "0.1",
                  "timestamp": "2026-01-01T00:00:00Z",
                  "bundle": {
                    "name": "test",
                    "root": "/tmp"
                  },
                  "nodes": [
                    { "id": "a", "path": "a.md", "type": "Reference", "degree": 1, "in": 0, "out": 1 },
                    { "id": "b", "path": "b.md", "type": "Reference", "degree": 1, "in": 1, "out": 0 }
                  ],
                  "edges": [
                    { "source": "a", "target": "b", "label": "b" }
                  ]
                }
                """);

            var loaded = GraphBuilder.Load(graphPath);

            Assert.All(loaded.Nodes, n =>
            {
                Assert.NotNull(n.Weight);
                Assert.InRange(n.Weight!.Value, 0.0, 1.0);
                Assert.NotNull(n.Rank);
            });
            Assert.Equal(1.0, loaded.Nodes.Sum(n => n.Weight!.Value), precision: 6);
            Assert.True(loaded.Nodes.First(n => n.Id == "b").Weight > loaded.Nodes.First(n => n.Id == "a").Weight);
            Assert.Equal(1, loaded.Nodes.First(n => n.Id == "b").Rank);
            Assert.Equal(2, loaded.Nodes.First(n => n.Id == "a").Rank);
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Load_preserves_existing_node_weights_when_some_are_missing()
    {
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(graphPath, """
                {
                  "version": "0.1",
                  "timestamp": "2026-01-01T00:00:00Z",
                  "bundle": {
                    "name": "test",
                    "root": "/tmp"
                  },
                  "nodes": [
                    { "id": "a", "path": "a.md", "type": "Reference", "degree": 2, "in": 0, "out": 2, "weight": 0.9 },
                    { "id": "b", "path": "b.md", "type": "Reference", "degree": 2, "in": 1, "out": 1 },
                    { "id": "c", "path": "c.md", "type": "Reference", "degree": 1, "in": 1, "out": 0 }
                  ],
                  "edges": [
                    { "source": "a", "target": "b", "label": "b", "id": "custom_ab" },
                    { "source": "b", "target": "c", "label": "c" }
                  ]
                }
                """);

            var loaded = GraphBuilder.Load(graphPath);

            Assert.Equal(0.9, loaded.Nodes.First(n => n.Id == "a").Weight!.Value, precision: 6);
            Assert.NotNull(loaded.Nodes.First(n => n.Id == "b").Weight);
            Assert.NotNull(loaded.Nodes.First(n => n.Id == "c").Weight);
            Assert.InRange(loaded.Nodes.First(n => n.Id == "b").Weight!.Value, 0.0, 1.0);
            Assert.InRange(loaded.Nodes.First(n => n.Id == "c").Weight!.Value, 0.0, 1.0);
            Assert.Equal(1, loaded.Nodes.First(n => n.Id == "a").Rank);
            Assert.NotNull(loaded.Nodes.First(n => n.Id == "b").Rank);
            Assert.NotNull(loaded.Nodes.First(n => n.Id == "c").Rank);
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Load_assigns_short_ids_when_edge_id_is_missing()
    {
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(graphPath, """
                {
                  "version": "0.1",
                  "timestamp": "2026-01-01T00:00:00Z",
                  "bundle": {
                    "name": "test",
                    "root": "/tmp"
                  },
                  "nodes": [
                    { "id": "a", "path": "a.md", "type": "Reference", "degree": 1, "in": 0, "out": 1 },
                    { "id": "b", "path": "b.md", "type": "Reference", "degree": 1, "in": 1, "out": 0 }
                  ],
                  "edges": [
                    { "source": "a", "target": "b", "label": "b" }
                  ]
                }
                """);

            var loaded = GraphBuilder.Load(graphPath);

            Assert.Equal("a_b", Assert.Single(loaded.Edges).Id);
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Load_preserves_existing_edge_ids_when_some_are_missing()
    {
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(graphPath, """
                {
                  "version": "0.1",
                  "timestamp": "2026-01-01T00:00:00Z",
                  "bundle": {
                    "name": "test",
                    "root": "/tmp"
                  },
                  "nodes": [
                    { "id": "a", "path": "a.md", "type": "Reference", "degree": 2, "in": 0, "out": 2 },
                    { "id": "b", "path": "b.md", "type": "Reference", "degree": 2, "in": 1, "out": 1 },
                    { "id": "c", "path": "c.md", "type": "Reference", "degree": 1, "in": 1, "out": 0 }
                  ],
                  "edges": [
                    { "source": "a", "target": "b", "label": "b", "id": "custom_ab" },
                    { "source": "b", "target": "c", "label": "c" }
                  ]
                }
                """);

            var loaded = GraphBuilder.Load(graphPath);

            Assert.Equal(2, loaded.Edges.Count);
            Assert.Equal("custom_ab", loaded.Edges[0].Id);
            Assert.Equal("b_c", loaded.Edges[1].Id);
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Load_preserves_unknown_node_properties_in_extension_data()
    {
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(graphPath, """
                {
                  "version": "0.1",
                  "timestamp": "2026-01-01T00:00:00Z",
                  "bundle": {
                    "name": "test",
                    "root": "/tmp"
                  },
                  "nodes": [
                    {
                      "id": "a",
                      "path": "a.md",
                      "type": "Reference",
                      "degree": 0,
                      "in": 0,
                      "out": 0,
                      "meta": { "title": "Legacy Title", "resource": "bq://proj.ds.tbl" },
                      "highlight": true
                    }
                  ],
                  "edges": []
                }
                """);

            var loaded = GraphBuilder.Load(graphPath);
            var node = Assert.Single(loaded.Nodes);

            Assert.True(HasExtensionKey(node, "meta"));
            Assert.Equal("Legacy Title", node.ExtensionData!["meta"].GetProperty("title").GetString());
            Assert.Equal("bq://proj.ds.tbl", node.ExtensionData!["meta"].GetProperty("resource").GetString());
            Assert.True(node.ExtensionData!["highlight"].GetBoolean());
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
        }
    }

    [Fact]
    public void Load_round_trips_unknown_properties_via_extension_data()
    {
        var graphPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");
        var roundTripPath = Path.Combine(Path.GetTempPath(), $"okf-graph-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(graphPath, """
                {
                  "version": "0.1",
                  "timestamp": "2026-01-01T00:00:00Z",
                  "bundle": {
                    "name": "test",
                    "root": "/tmp",
                    "producer": "acme-agent"
                  },
                  "nodes": [
                    {
                      "id": "a",
                      "path": "a.md",
                      "type": "Reference",
                      "degree": 0,
                      "in": 0,
                      "out": 0,
                      "highlight": true
                    }
                  ],
                  "edges": [],
                  "schema_version": 2
                }
                """);

            var loaded = GraphBuilder.Load(graphPath);
            Assert.Equal("acme-agent", loaded.Bundle!.ExtensionData!["producer"].GetString());
            Assert.Equal(2, loaded.ExtensionData!["schema_version"].GetInt32());
            Assert.True(loaded.Nodes[0].ExtensionData!["highlight"].GetBoolean());

            var json = JsonSerializer.Serialize(loaded, GraphBuilder.JsonOptions);
            File.WriteAllText(roundTripPath, json);
            var reloaded = GraphBuilder.Load(roundTripPath);

            Assert.Equal("acme-agent", reloaded.Bundle!.ExtensionData!["producer"].GetString());
            Assert.Equal(2, reloaded.ExtensionData!["schema_version"].GetInt32());
            Assert.True(reloaded.Nodes[0].ExtensionData!["highlight"].GetBoolean());

            using var document = JsonDocument.Parse(json);
            Assert.Equal("acme-agent", document.RootElement.GetProperty("bundle").GetProperty("producer").GetString());
            Assert.Equal(2, document.RootElement.GetProperty("schema_version").GetInt32());
            Assert.True(document.RootElement.GetProperty("nodes")[0].GetProperty("highlight").GetBoolean());
        }
        finally
        {
            if (File.Exists(graphPath))
                File.Delete(graphPath);
            if (File.Exists(roundTripPath))
                File.Delete(roundTripPath);
        }
    }
}
