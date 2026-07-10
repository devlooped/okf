using System.Text.Json;
using Devlooped;

namespace Tests;

public class IndexNavBuilderTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Nav_is_null_when_includeNav_false()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-basic"), includeNav: false);
        Assert.Null(graph.Nav);
    }

    [Fact]
    public void Nav_root_is_dir_with_children_when_includeNav_true()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-basic"), includeNav: true);
        Assert.NotNull(graph.Nav);
        Assert.Equal("dir", graph.Nav!.Kind);
        Assert.Equal("", graph.Nav.Id);
        Assert.False(graph.Nav.Synthetic == true);
        Assert.NotNull(graph.Nav.Body);
        Assert.Contains("# Bundle", graph.Nav.Body);
        Assert.NotNull(graph.Nav.Children);
        Assert.True(graph.Nav.Children!.Count >= 3);
    }

    [Fact]
    public void Multi_section_index_preserves_groups()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-basic"), includeNav: true);
        var alpha = FindDir(graph.Nav!, "alpha");
        Assert.NotNull(alpha);
        Assert.NotNull(alpha!.Children);
        Assert.Equal(2, alpha.Children!.Count);
        Assert.All(alpha.Children, c => Assert.Equal("group", c.Kind));
        Assert.Equal("Core", alpha.Children[0].Label);
        Assert.Equal("Extra", alpha.Children[1].Label);
        Assert.Equal("alpha/one", alpha.Children[0].Children![0].Id);
        Assert.Equal("alpha/two", alpha.Children[1].Children![0].Id);
    }

    [Fact]
    public void Single_section_index_is_flattened()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-basic"), includeNav: true);
        // root has single "# Bundle" section → flattened to dir children
        Assert.DoesNotContain(graph.Nav!.Children!, c => c.Kind == "group");
        Assert.Contains(graph.Nav.Children!, c => c.Kind == "dir" && c.Id == "alpha");
    }

    [Fact]
    public void Missing_index_is_synthetic()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-basic"), includeNav: true);
        var beta = FindDir(graph.Nav!, "beta");
        Assert.NotNull(beta);
        Assert.True(beta!.Synthetic == true);
        Assert.NotNull(beta.Body);
        Assert.Contains("#", beta.Body);
        Assert.Contains(beta.Children!, c => c.Kind == "concept" && c.Id == "beta/only");
    }

    [Fact]
    public void Orphan_concept_appears_under_orphans_group()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-basic"), includeNav: true);
        var gamma = FindDir(graph.Nav!, "gamma");
        Assert.NotNull(gamma);
        var orphans = gamma!.Children!.Single(c => c.Kind == "orphans");
        Assert.Equal("Other", orphans.Label);
        Assert.Contains(orphans.Children!, c => c.Id == "gamma/b");
        Assert.Contains(gamma.Children!, c => c.Kind == "concept" && c.Id == "gamma/a");
    }

    [Fact]
    public void Out_of_dir_links_omitted_from_tree_but_kept_in_body()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-outdir"), includeNav: true);
        var local = FindDir(graph.Nav!, "local");
        Assert.NotNull(local);
        Assert.Contains("Cross-References", local!.Body!);
        Assert.Contains("/other.md", local.Body!);
        // Tree: only Here concept (flattened), no link to other or root index
        Assert.DoesNotContain(local.Children ?? [], c => c.Id == "other");
        Assert.Contains(local.Children!, c => c.Kind == "concept" && c.Id == "local/here");
        Assert.DoesNotContain(local.Children ?? [], c => c.Kind == "group" && c.Label == "Cross-References");
    }

    [Fact]
    public void Compat_mode_uses_H2_section_groups()
    {
        var graph = GraphBuilder.Build(FixturePath("nav-compat"), includeNav: true);
        Assert.NotNull(graph.Nav);
        Assert.Equal("My Bundle Title", graph.Nav!.Label);
        Assert.NotNull(graph.Nav.Children);
        Assert.Equal(2, graph.Nav.Children!.Count);
        Assert.All(graph.Nav.Children, c => Assert.Equal("group", c.Kind));
        Assert.Equal("First", graph.Nav.Children[0].Label);
        Assert.Equal("Second", graph.Nav.Children[1].Label);
        Assert.Equal("alpha", graph.Nav.Children[0].Children![0].Id);
        Assert.Equal("Alpha", graph.Nav.Children[0].Children![0].Label); // index link text wins
    }

    [Fact]
    public void Root_frontmatter_stripped_from_nav_body()
    {
        // nav-basic root has no frontmatter; use valid fixture which may have tables only
        var bundle = Path.Combine(Path.GetTempPath(), "okf-nav-fm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundle);
        try
        {
            File.WriteAllText(Path.Combine(bundle, "index.md"), """
                ---
                okf_version: "0.1"
                ---

                # Root

                * [Doc](doc.md) - A doc.
                """);
            File.WriteAllText(Path.Combine(bundle, "doc.md"), """
                ---
                type: Reference
                title: Doc
                ---

                Body.
                """);

            var graph = GraphBuilder.Build(bundle, includeNav: true);
            Assert.DoesNotContain("okf_version", graph.Nav!.Body);
            Assert.Contains("# Root", graph.Nav.Body);
        }
        finally
        {
            Directory.Delete(bundle, recursive: true);
        }
    }

    [Fact]
    public void Duplicate_listings_preserved()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "okf-nav-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundle);
        try
        {
            File.WriteAllText(Path.Combine(bundle, "index.md"), """
                # Root

                * [Doc](doc.md) - First.
                * [Doc again](doc.md) - Second.
                """);
            File.WriteAllText(Path.Combine(bundle, "doc.md"), """
                ---
                type: Reference
                title: Doc
                ---

                Body.
                """);

            var graph = GraphBuilder.Build(bundle, includeNav: true);
            var concepts = graph.Nav!.Children!.Where(c => c.Kind == "concept").ToList();
            Assert.Equal(2, concepts.Count);
            Assert.Equal("Doc", concepts[0].Label);
            Assert.Equal("Doc again", concepts[1].Label);
        }
        finally
        {
            Directory.Delete(bundle, recursive: true);
        }
    }

    [Fact]
    public void Load_round_trips_nav()
    {
        var bundle = FixturePath("nav-basic");
        var outPath = Path.Combine(Path.GetTempPath(), "okf-nav-rt-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            GraphBuilder.Generate(bundle, outPath, includeNav: true);
            var loaded = GraphBuilder.Load(outPath);
            Assert.NotNull(loaded.Nav);
            Assert.Equal("dir", loaded.Nav!.Kind);
            Assert.Equal("", loaded.Nav.Id);
            Assert.NotNull(loaded.Nav.Children);
            Assert.Contains(loaded.Nav.Children!, c => c.Id == "alpha");
        }
        finally
        {
            if (File.Exists(outPath))
                File.Delete(outPath);
        }
    }

    [Fact]
    public void Valid_fixture_tables_dir_is_synthetic()
    {
        var graph = GraphBuilder.Build(FixturePath("valid"), includeNav: true);
        Assert.NotNull(graph.Nav);
        // root index lists nested concept; local membership drops it → tables may be orphan/subdir
        var tables = FindDir(graph.Nav!, "tables");
        Assert.NotNull(tables);
        Assert.True(tables!.Synthetic == true);
        Assert.Contains(tables.Children!, c => c.Id == "tables/orders");
        Assert.Contains(tables.Children!, c => c.Id == "tables/customers");
    }

    static GraphBuilder.NavNode? FindDir(GraphBuilder.NavNode root, string id)
    {
        if (root.Kind == "dir" && root.Id == id)
            return root;
        if (root.Children is null)
            return null;
        foreach (var child in root.Children)
        {
            var found = FindDir(child, id);
            if (found is not null)
                return found;
        }
        return null;
    }
}
