using System.Text.Json;
using okf;

namespace Tests;

public class CheckRendererTests
{
    static string FixturePath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));

    [Fact]
    public void Json_output_for_valid_bundle_reports_success()
    {
        var issues = new BundleChecker(FixturePath("valid")).Check();
        var result = CheckRenderer.BuildJsonResult(issues, FixturePath("valid"));

        Assert.True(result.Success);
        Assert.Equal(0, result.Errors);
        Assert.Empty(result.Issues);
        Assert.All(result.Rules, rule => Assert.True(rule.Passed));
    }

    [Fact]
    public void Json_output_for_invalid_bundle_includes_issues()
    {
        var fixturePath = FixturePath("missing-type");
        var issues = new BundleChecker(fixturePath).Check();
        var result = CheckRenderer.BuildJsonResult(issues, fixturePath);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("'type'"));
        Assert.Contains(result.Rules, rule => rule.Rule == CheckRule.ConceptType && !rule.Passed);
    }

    [Fact]
    public void RenderJson_writes_valid_json_to_stdout()
    {
        var issues = new BundleChecker(FixturePath("valid")).Check();
        using var writer = new StringWriter();

        CheckRenderer.RenderJson(issues, FixturePath("valid"), writer);

        var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("errors").GetInt32());
    }
}
