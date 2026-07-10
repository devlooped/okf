using Devlooped;

namespace Tests;

public class ViewOutputPathsTests
{
    [Fact]
    public void Omitted_out_uses_bundle_root()
    {
        var bundle = Path.GetFullPath("/tmp/bundle");
        var p = ViewOutputPaths.Resolve(bundle, null);
        Assert.Equal(Path.Combine(bundle, "okf.json"), p.JsonPath);
        Assert.Equal(Path.Combine(bundle, "index.html"), p.HtmlPath);
    }

    [Fact]
    public void Existing_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "okf-view-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = ViewOutputPaths.Resolve(Path.GetTempPath(), dir);
            Assert.Equal(Path.Combine(dir, "okf.json"), p.JsonPath);
            Assert.Equal(Path.Combine(dir, "index.html"), p.HtmlPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Html_file_path()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "okf-view-html-" + Guid.NewGuid().ToString("N"));
        var html = Path.Combine(baseDir, "report.html");
        var p = ViewOutputPaths.Resolve(Path.GetTempPath(), html);
        Assert.Equal(Path.Combine(baseDir, "okf.json"), p.JsonPath);
        Assert.Equal(Path.GetFullPath(html), p.HtmlPath);
    }

    [Fact]
    public void Json_file_path()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "okf-view-json-" + Guid.NewGuid().ToString("N"));
        var json = Path.Combine(baseDir, "graph.json");
        var p = ViewOutputPaths.Resolve(Path.GetTempPath(), json);
        Assert.Equal(Path.GetFullPath(json), p.JsonPath);
        Assert.Equal(Path.Combine(baseDir, "index.html"), p.HtmlPath);
    }

    [Fact]
    public void Trailing_separator_is_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "okf-view-trail-" + Guid.NewGuid().ToString("N"));
        var withSep = dir + Path.DirectorySeparatorChar;
        var p = ViewOutputPaths.Resolve(Path.GetTempPath(), withSep);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir), "okf.json"), p.JsonPath);
    }

    [Fact]
    public void Extension_less_missing_path_is_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "okf-view-bare-" + Guid.NewGuid().ToString("N"), "out");
        var p = ViewOutputPaths.Resolve(Path.GetTempPath(), dir);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir), "okf.json"), p.JsonPath);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir), "index.html"), p.HtmlPath);
    }
}
