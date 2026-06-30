using okf;

namespace Tests;

public class BundleCheckerTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Valid_minimal_bundle_passes()
    {
        var issues = new BundleChecker(FixturePath("valid")).Check();
        Assert.Empty(issues);
    }

    [Fact]
    public void Missing_type_is_reported()
    {
        var issues = new BundleChecker(FixturePath("missing-type")).Check();
        Assert.Contains(issues, issue => issue.Message.Contains("'type'"));
    }

    [Fact]
    public void Broken_relative_link_is_reported()
    {
        var issues = new BundleChecker(FixturePath("broken-link")).Check();
        Assert.Contains(issues, issue => issue.Message.Contains("Broken link"));
    }

    [Fact]
    public void Broken_absolute_link_is_reported()
    {
        var issues = new BundleChecker(FixturePath("broken-absolute-link")).Check();
        Assert.Contains(issues, issue => issue.Message.Contains("Broken link") && issue.Message.Contains("/tables/missing.md"));
    }

    [Fact]
    public void Index_without_frontmatter_passes()
    {
        var issues = new BundleChecker(FixturePath("valid")).Check();
        Assert.DoesNotContain(issues, issue => issue.File.EndsWith("index.md"));
    }

    [Fact]
    public void Log_with_invalid_date_is_reported()
    {
        var issues = new BundleChecker(FixturePath("invalid-log")).Check();
        Assert.Contains(issues, issue => issue.File.EndsWith("log.md"));
    }
}

public class OKFDocumentTests
{
    [Fact]
    public void Parses_frontmatter_and_body()
    {
        const string text = """
            ---
            type: BigQuery Table
            title: Sample
            tags: [a, b]
            ---
            
            # Body
            """;

        Assert.True(OKFDocument.TryParse(text, out var document, out var error), error);
        Assert.Equal("BigQuery Table", OKFDocument.GetTypeValue(document!.Frontmatter));
        Assert.StartsWith("# Body", document.Body);
    }
}
