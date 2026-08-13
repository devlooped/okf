using Devlooped;

namespace Tests;

public class OkfSpecTests
{
    [Fact]
    public void Latest_spec_matches_repo_file()
    {
        var repo = File.ReadAllText(FindRepoFile("specs", "okf-0.1.md"));
        Assert.Equal(Normalize(repo), Normalize(OkfSpec.Markdown));
        Assert.Equal(OkfSpec.Markdown, OkfSpec.Get("latest"));
        Assert.Equal(OkfSpec.Markdown, OkfSpec.Get("0.1"));
        Assert.Equal(OkfSpec.Markdown, OkfSpec.Get("v0.1"));
    }

    [Fact]
    public void Write_creates_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "okf-spec-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "SPEC.md");

        try
        {
            OkfSpec.Write(path, "0.1");
            Assert.Equal(Normalize(OkfSpec.Markdown), Normalize(File.ReadAllText(path)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    static string FindRepoFile(params string[] relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine([dir, .. relative]);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException("Could not locate " + Path.Combine(relative));
    }

    static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
