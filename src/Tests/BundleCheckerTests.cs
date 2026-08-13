using Devlooped;

namespace Tests;

public class BundleCheckerTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Valid_minimal_bundle_passes()
    {
        var result = new BundleChecker(FixturePath("valid")).Check();
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Missing_type_is_reported_as_error()
    {
        var result = new BundleChecker(FixturePath("missing-type")).Check();
        Assert.Contains(result.Errors, issue => issue.Message.Contains("'type'"));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Broken_relative_link_is_reported_as_warning()
    {
        var result = new BundleChecker(FixturePath("broken-link")).Check();
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("Unresolved link"));
    }

    [Fact]
    public void Broken_absolute_link_is_reported_as_warning()
    {
        var result = new BundleChecker(FixturePath("broken-absolute-link")).Check();
        Assert.Empty(result.Errors);
        Assert.Contains(
            result.Warnings,
            issue => issue.Message.Contains("Unresolved link") && issue.Message.Contains("/tables/missing.md"));
    }

    [Fact]
    public void Index_without_frontmatter_passes()
    {
        var result = new BundleChecker(FixturePath("valid")).Check();
        Assert.DoesNotContain(result.Errors, issue => issue.File.EndsWith("index.md"));
        Assert.DoesNotContain(result.Warnings, issue => issue.File.EndsWith("index.md"));
    }

    [Fact]
    public void Log_with_invalid_date_is_reported_as_error()
    {
        var result = new BundleChecker(FixturePath("invalid-log")).Check();
        Assert.Contains(result.Errors, issue => issue.File.EndsWith("log.md"));
    }

    [Fact]
    public void Index_free_prose_is_reported_as_warning()
    {
        var result = new BundleChecker(FixturePath("index-prose")).Check();
        Assert.Empty(result.Errors);
        Assert.Contains(
            result.Warnings,
            issue => issue.Rule == CheckRule.IndexProse
                && issue.File.EndsWith("index.md")
                && issue.Message.Contains("free prose"));
    }

    [Fact]
    public void Compliant_index_with_h2_sections_has_no_prose_warning()
    {
        var result = new BundleChecker(FixturePath("nav-compat")).Check();
        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, issue => issue.Rule == CheckRule.IndexProse);
    }

    [Fact]
    public void V02_family_warnings_are_soft()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "bad.md"), """
                ---
                type: Metric
                generated: { at: 2026-06-20T22:53:05Z }
                verified: just-a-string
                status: published
                stale_after: next-tuesday
                sources:
                  - id: orphan
                    title: Missing resource
                ---
                A claim.[^missing]
                """);

            var result = new BundleChecker(bundlePath).Check();
            Assert.Empty(result.Errors);
            Assert.Contains(result.Warnings, w => w.Rule == CheckRule.GeneratedBy);
            Assert.Contains(result.Warnings, w => w.Rule == CheckRule.VerifiedShape);
            Assert.Contains(result.Warnings, w => w.Rule == CheckRule.StatusValue);
            Assert.Contains(result.Warnings, w => w.Rule == CheckRule.StaleAfter);
            Assert.Contains(result.Warnings, w => w.Rule == CheckRule.SourcesResource);
            Assert.Contains(result.Warnings, w => w.Rule == CheckRule.SourceFootnotes);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Attested_computation_without_runtime_is_not_a_finding()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"okf-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);

        try
        {
            File.WriteAllText(Path.Combine(bundlePath, "revenue.md"), """
                ---
                type: Attested Computation
                title: Revenue
                ---
                SELECT 1
                """);

            var result = new BundleChecker(bundlePath).Check();
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, recursive: true);
        }
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
        Assert.StartsWith("# Body", document.Body.TrimStart('\r', '\n'));
    }

    [Fact]
    public void TryParse_includes_yaml_snippet_for_invalid_frontmatter()
    {
        const string text = """
            ---
            type: Metric
            tags: [unclosed list
            ---
            
            Body
            """;

        Assert.False(OKFDocument.TryParse(text, out _, out var error, out var snippet));
        Assert.Equal("While parsing a flow sequence, did not find expected ',' or ']'.", error);
        Assert.NotNull(snippet);
        Assert.Single(snippet!.Lines);
        Assert.Equal(2, snippet.Lines[0].LineNumber);
        Assert.Equal("tags: [unclosed list", snippet.Lines[0].Text);
        Assert.Equal(7, snippet.Lines[0].StartColumn);
        Assert.Equal(20, snippet.Lines[0].EndColumn);

        var location = IssueLocation.FromYamlSnippet(text, snippet);
        Assert.NotNull(location);
        Assert.Equal("(3:7-3:20)", location!.FormatSuffix());
        Assert.Equal("(5:9)", new IssueLocation(5, 9).FormatSuffix());
        Assert.Equal("(14)", new IssueLocation(14).FormatSuffix());
    }
}