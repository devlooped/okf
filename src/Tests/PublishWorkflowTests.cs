namespace Tests;

public class PublishWorkflowTests
{
    static readonly (string Os, string Rid)[] ExpectedMatrix =
    [
        ("ubuntu-latest", "linux-x64"),
        ("ubuntu-24.04-arm", "linux-arm64"),
        ("ubuntu-latest", "linux-musl-x64"),
        ("ubuntu-24.04-arm", "linux-musl-arm64"),
        ("windows-latest", "win-x64"),
        ("windows-11-arm", "win-arm64"),
        ("macos-15-intel", "osx-x64"),
        ("macos-latest", "osx-arm64"),
    ];

    [Fact]
    public void Release_workflow_packs_rid_natives_any_fallback_then_pointer()
    {
        var yml = File.ReadAllText(FindRepoFile(".github", "workflows", "publish.yml"));

        Assert.Contains("name: native-aot-${{ matrix.rid }}", yml);
        Assert.Contains("dotnet pack src/okf/okf.csproj", yml);
        Assert.Contains("-r ${{ matrix.rid }}", yml);
        Assert.Contains("-r any", yml);
        Assert.Contains("name: package-any", yml);
        Assert.Contains("matrix.musl != true", yml);
        Assert.Contains(".github/scripts/pack-musl.sh", yml);
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0-alpine3.23-aot", yml);
        Assert.Contains("linux-musl-x64", yml);
        Assert.Contains("linux-musl-arm64", yml);

        var runtimeNuget = yml.IndexOf("foreach ($package in $runtimePackages)", StringComparison.Ordinal);
        var pointerNuget = yml.IndexOf("foreach ($package in $pointerPackages)", StringComparison.Ordinal);
        Assert.True(runtimeNuget >= 0 && pointerNuget > runtimeNuget, "RID/any nupkgs must be nuget-pushed before the pointer.");

        var runtimeSleet = yml.IndexOf("sleet push $runtimePath", StringComparison.Ordinal);
        var pointerSleet = yml.IndexOf("sleet push $pointerPath", StringComparison.Ordinal);
        Assert.True(runtimeSleet >= 0 && pointerSleet > runtimeSleet, "RID/any nupkgs must be sleet-pushed before the pointer.");

        foreach (var (os, rid) in ExpectedMatrix)
        {
            Assert.Contains($"os: {os}", yml);
            Assert.Contains($"rid: {rid}", yml);
        }
    }

    [Fact]
    public void Ci_packs_only_the_any_fallback_pair()
    {
        var yml = File.ReadAllText(FindRepoFile(".github", "workflows", "build.yml"));

        Assert.Contains("-p:RuntimeIdentifiers=any", yml);
        Assert.Contains("-r any", yml);
        Assert.DoesNotContain("GeneratePackageOnBuild", yml);
        Assert.DoesNotContain("linux-musl", yml);
        Assert.DoesNotContain("-r win-x64", yml);
    }

    [Fact]
    public void Project_declares_the_ndx_rid_set_and_disables_aot_for_any()
    {
        var project = File.ReadAllText(FindRepoFile("src", "okf", "okf.csproj"));

        Assert.Contains("<PublishAot>true</PublishAot>", project);
        Assert.Contains("<PublishAot>false</PublishAot>", project);
        Assert.Contains("RuntimeIdentifier)' == 'any'", project);
        Assert.DoesNotContain("NuGetizer", project);

        foreach (var rid in new[]
        {
            "win-x64", "win-arm64", "linux-x64", "linux-arm64",
            "linux-musl-x64", "linux-musl-arm64", "osx-x64", "osx-arm64", "any",
        })
        {
            Assert.Contains(rid, project);
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

        throw new FileNotFoundException("Could not locate " + Path.Combine(relative) + " from " + AppContext.BaseDirectory);
    }
}
