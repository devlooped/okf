using Devlooped;

namespace Tests;

public class CafArgsTests
{
    [Fact]
    public void Bare_version_is_unchanged_so_CAF_prints_tool_version()
    {
        string[] args = ["--version"];
        Assert.Same(args, CafArgs.RestrictToolVersion(args));
    }

    [Fact]
    public void Empty_and_single_non_version_are_unchanged()
    {
        Assert.Empty(CafArgs.RestrictToolVersion([]));
        string[] help = ["--help"];
        Assert.Same(help, CafArgs.RestrictToolVersion(help));
    }

    [Fact]
    public void Command_plus_version_gets_default_format_value()
    {
        Assert.Equal(["schema", "--version", "latest"], CafArgs.RestrictToolVersion(["schema", "--version"]));
        Assert.Equal(["spec", "--version", "latest"], CafArgs.RestrictToolVersion(["spec", "--version"]));
    }

    [Fact]
    public void Explicit_format_version_is_kept()
    {
        string[] args = ["schema", "--version", "0.1"];
        Assert.Same(args, CafArgs.RestrictToolVersion(args));
    }

    [Fact]
    public void Version_followed_by_another_option_inserts_latest()
    {
        Assert.Equal(
            ["schema", "--version", "latest", "-o", "out.json"],
            CafArgs.RestrictToolVersion(["schema", "--version", "-o", "out.json"]));
    }

    [Fact]
    public void Dash_v_is_not_rewritten()
    {
        string[] args = ["schema", "-v"];
        Assert.Same(args, CafArgs.RestrictToolVersion(args));
    }
}
