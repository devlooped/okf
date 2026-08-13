using Devlooped;

namespace Tests;

public class SkillCommandsTests
{
    [Fact]
    public void ResolveSkillPath_defaults_to_user_home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = Path.GetFullPath(Path.Combine(home, ".agents", "skills", "okf", "SKILL.md"));

        Assert.Equal(expected, SkillCommands.ResolveSkillPath(null));
        Assert.Equal(expected, SkillCommands.ResolveSkillPath(""));
        Assert.Equal(expected, SkillCommands.ResolveSkillPath("   "));
    }

    [Fact]
    public void ResolveSkillPath_uses_directory_when_provided()
    {
        var root = Path.Combine(Path.GetTempPath(), "okf-skill-test-" + Guid.NewGuid().ToString("N"));
        var expected = Path.GetFullPath(Path.Combine(root, ".agents", "skills", "okf", "SKILL.md"));

        Assert.Equal(expected, SkillCommands.ResolveSkillPath(root));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".agents", "skills", "okf", "SKILL.md")),
            SkillCommands.ResolveSkillPath("."));
    }

    [Fact]
    public void Bundled_skill_matches_repo_skill_file()
    {
        var repo = File.ReadAllText(FindRepoFile("skills", "okf", "SKILL.md"));
        Assert.Equal(Normalize(repo), Normalize(SkillCommands.Markdown));
    }

    [Fact]
    public void Install_writes_and_overwrites()
    {
        var dest = SkillCommands.ResolveSkillPath(CreateTempDir());

        Assert.Equal(0, SkillCommands.Install(dest));
        Assert.True(File.Exists(dest));
        Assert.Equal(Normalize(SkillCommands.Markdown), Normalize(File.ReadAllText(dest)));

        File.WriteAllText(dest, "stale");
        Assert.Equal(0, SkillCommands.Install(dest));
        Assert.Equal(Normalize(SkillCommands.Markdown), Normalize(File.ReadAllText(dest)));
    }

    [Fact]
    public void Uninstall_deletes_installed_skill()
    {
        var dest = SkillCommands.ResolveSkillPath(CreateTempDir());
        Assert.Equal(0, SkillCommands.Install(dest));
        Assert.True(File.Exists(dest));

        Assert.Equal(0, SkillCommands.Uninstall(dest));
        Assert.False(File.Exists(dest));
        Assert.False(Directory.Exists(Path.GetDirectoryName(dest)));
    }

    [Fact]
    public void Uninstall_succeeds_when_not_installed()
    {
        var dest = SkillCommands.ResolveSkillPath(CreateTempDir());
        Assert.Equal(0, SkillCommands.Uninstall(dest));
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "okf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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

        throw new FileNotFoundException("Could not locate " + Path.Combine(relative) + " from " + AppContext.BaseDirectory);
    }

    static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
