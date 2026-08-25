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
    public void FormatScopePath_uses_dot_for_local_and_tilde_for_global()
    {
        var local = SkillCommands.FormatScopePath(false);
        var global = SkillCommands.FormatScopePath(true);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(".", ".agents", "skills", "okf", "SKILL.md"), local);
        Assert.Equal(Path.Combine("~", ".agents", "skills", "okf", "SKILL.md"), global);
        Assert.StartsWith("." + Path.DirectorySeparatorChar, local);
        Assert.StartsWith("~" + Path.DirectorySeparatorChar, global);
        Assert.DoesNotContain(home, global);
    }

    [Fact]
    public void ResolveInstallDestination_prompts_when_unspecified()
    {
        var result = SkillCommands.ResolveInstallDestination(null, global: false);
        Assert.Equal(SkillCommands.DestinationKind.Prompt, result.Kind);
        Assert.Null(result.Path);
    }

    [Fact]
    public void ResolveInstallDestination_global_uses_home()
    {
        var result = SkillCommands.ResolveInstallDestination(null, global: true);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.Equal(SkillCommands.ResolveSkillPath(null), result.Path);
        Assert.True(result.Confirm);
    }

    [Fact]
    public void ResolveInstallDestination_directory_uses_that_base()
    {
        var root = CreateTempDir();
        var result = SkillCommands.ResolveInstallDestination(root, global: false);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.Equal(SkillCommands.ResolveSkillPath(root), result.Path);
        Assert.True(result.Confirm);
    }

    [Fact]
    public void ResolveInstallDestination_rejects_directory_and_global()
    {
        var result = SkillCommands.ResolveInstallDestination(".", global: true);
        Assert.Equal(SkillCommands.DestinationKind.Error, result.Kind);
        Assert.Contains("--global", result.Error);
    }

    [Fact]
    public void ResolveRemoveDestination_prompts_when_both_exist()
    {
        var local = SkillCommands.ResolveSkillPath(CreateTempDir());
        var global = SkillCommands.ResolveSkillPath(CreateTempDir());
        WriteSkill(local);
        WriteSkill(global);

        var result = SkillCommands.ResolveRemoveDestination(null, global: false, local, global);
        Assert.Equal(SkillCommands.DestinationKind.Prompt, result.Kind);
    }

    [Fact]
    public void ResolveRemoveDestination_removes_only_local_without_confirm()
    {
        var local = SkillCommands.ResolveSkillPath(CreateTempDir());
        var global = SkillCommands.ResolveSkillPath(CreateTempDir());
        WriteSkill(local);

        var result = SkillCommands.ResolveRemoveDestination(null, global: false, local, global);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.Equal(local, result.Path);
        Assert.False(result.Confirm);
    }

    [Fact]
    public void ResolveRemoveDestination_removes_only_global_without_confirm()
    {
        var local = SkillCommands.ResolveSkillPath(CreateTempDir());
        var global = SkillCommands.ResolveSkillPath(CreateTempDir());
        WriteSkill(global);

        var result = SkillCommands.ResolveRemoveDestination(null, global: false, local, global);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.Equal(global, result.Path);
        Assert.False(result.Confirm);
    }

    [Fact]
    public void ResolveRemoveDestination_neither_exists_does_not_prompt()
    {
        var local = SkillCommands.ResolveSkillPath(CreateTempDir());
        var global = SkillCommands.ResolveSkillPath(CreateTempDir());

        var result = SkillCommands.ResolveRemoveDestination(null, global: false, local, global);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.False(result.Confirm);
    }

    [Fact]
    public void ResolveRemoveDestination_same_location_does_not_prompt()
    {
        var dest = SkillCommands.ResolveSkillPath(CreateTempDir());
        WriteSkill(dest);

        var result = SkillCommands.ResolveRemoveDestination(null, global: false, dest, dest);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.Equal(dest, result.Path);
        Assert.False(result.Confirm);
    }

    [Fact]
    public void ResolveRemoveDestination_explicit_directory_confirms()
    {
        var root = CreateTempDir();
        var result = SkillCommands.ResolveRemoveDestination(root, global: false);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.Equal(SkillCommands.ResolveSkillPath(root), result.Path);
        Assert.True(result.Confirm);
    }

    [Fact]
    public void ResolveRemoveDestination_global_flag_confirms()
    {
        var result = SkillCommands.ResolveRemoveDestination(null, global: true);
        Assert.Equal(SkillCommands.DestinationKind.Target, result.Kind);
        Assert.Equal(SkillCommands.ResolveSkillPath(null), result.Path);
        Assert.True(result.Confirm);
    }

    [Fact]
    public void ResolveRemoveDestination_rejects_directory_and_global()
    {
        var result = SkillCommands.ResolveRemoveDestination(".", global: true);
        Assert.Equal(SkillCommands.DestinationKind.Error, result.Kind);
        Assert.Contains("--global", result.Error);
    }

    [Fact]
    public void RenderScopeLine_marks_selected_local_path()
    {
        var path = SkillCommands.FormatScopePath(false);
        var selected = SkillCommands.RenderScopeLine("Local", path, selected: true);
        var idle = SkillCommands.RenderScopeLine("Global", SkillCommands.FormatScopePath(true), selected: false);

        Assert.Contains("[green]●[/] Local", selected);
        Assert.Contains(path, selected);
        Assert.Contains("[grey]○ Global", idle);
        Assert.DoesNotContain("[green]", idle);
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

    static void WriteSkill(string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, "skill");
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
