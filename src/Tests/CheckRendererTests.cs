using System.Text.Json;
using Devlooped;

namespace Tests;

public class CheckRendererTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Json_output_for_valid_bundle_reports_success()
    {
        var result = new BundleChecker(FixturePath("valid")).Check();
        var json = CheckRenderer.BuildJsonResult(result, FixturePath("valid"));

        Assert.True(json.Success);
        Assert.Equal(0, json.Errors);
        Assert.Equal(0, json.Warnings);
        Assert.Empty(json.Issues);
        Assert.Empty(json.WarningIssues);
        Assert.All(json.Rules, rule => Assert.True(rule.Passed));
        Assert.All(json.WarningRules, rule => Assert.True(rule.Passed));
    }

    [Fact]
    public void Json_output_for_invalid_bundle_includes_issues()
    {
        var fixturePath = FixturePath("missing-type");
        var result = new BundleChecker(fixturePath).Check();
        var json = CheckRenderer.BuildJsonResult(result, fixturePath);

        Assert.False(json.Success);
        Assert.Contains(json.Issues, issue => issue.Message.Contains("'type'"));
        Assert.Contains(json.Rules, rule => rule.Rule == CheckRule.ConceptType && !rule.Passed);
    }

    [Fact]
    public void Json_output_for_broken_link_includes_warnings()
    {
        var fixturePath = FixturePath("broken-link");
        var result = new BundleChecker(fixturePath).Check();
        var json = CheckRenderer.BuildJsonResult(result, fixturePath);

        Assert.True(json.Success);
        Assert.Equal(0, json.Errors);
        Assert.Equal(1, json.Warnings);
        Assert.Contains(json.WarningIssues, issue => issue.Message.Contains("Unresolved link"));
        Assert.Contains(json.WarningRules, rule => rule.Rule == CheckRule.InternalLinks && !rule.Passed);
    }

    [Fact]
    public void Quiet_render_for_valid_bundle_produces_no_output()
    {
        var result = new BundleChecker(FixturePath("valid")).Check();
        using var writer = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);
            CheckRenderer.Render(result, FixturePath("valid"), quiet: true);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void Quiet_render_for_broken_link_shows_only_warnings()
    {
        var fixturePath = FixturePath("broken-link");
        var result = new BundleChecker(fixturePath).Check();
        using var writer = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);
            CheckRenderer.Render(result, fixturePath, quiet: true);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("Unresolved internal links", output);
        Assert.Contains("Unresolved link", output);
        Assert.DoesNotContain("Concept files declare a type", output);
        Assert.DoesNotContain("✓", output);
    }

    [Fact]
    public void RenderJson_writes_valid_json_to_stdout()
    {
        var result = new BundleChecker(FixturePath("valid")).Check();
        using var writer = new StringWriter();

        CheckRenderer.RenderJson(result, FixturePath("valid"), writer);

        var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("errors").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("warnings").GetInt32());
        Assert.Equal("bundleExists", document.RootElement.GetProperty("rules")[0].GetProperty("rule").GetString());
    }
}