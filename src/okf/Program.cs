using System.Diagnostics;
using ConsoleAppFramework;
using okf;

var app = ConsoleApp.Create();
app.Add("check", Check);
app.Add("viz", Visualize);
app.Add("graph", Graph);
app.Run(args);

static int Check([Argument] string path = ".", bool json = false)
{
    var checker = new BundleChecker(path);
    var issues = checker.Check();

    if (json)
    {
        CheckRenderer.RenderJson(issues, path, Console.Out);
    }
    else
    {
        CheckRenderer.Render(issues, path);

        if (issues.Count > 0)
        {
            Console.Error.WriteLine($"{issues.Count} error(s).");
        }
    }

    return issues.Count > 0 ? 1 : 0;
}

static int Visualize(
    [Argument] string path = ".",
    string? @out = null,
    string? name = null,
    bool open = false)
{
    var bundleRoot = Path.GetFullPath(path);

    if (!Directory.Exists(bundleRoot))
    {
        Console.Error.WriteLine($"Bundle directory not found: {bundleRoot}");
        return 1;
    }

    var outPath = @out ?? Path.Combine(bundleRoot, "viz.html");
    var displayName = name ?? new DirectoryInfo(bundleRoot).Name;

    try
    {
        var stats = BundleVisualizer.Generate(bundleRoot, outPath, displayName);
        Console.WriteLine($"Wrote {outPath} ({stats.Concepts} concepts, {stats.Edges} edges, {stats.Bytes} bytes).");

        if (open)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = outPath,
                UseShellExecute = true,
            });
        }

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int Graph(
    [Argument] string path = ".",
    string? @out = null,
    string? name = null,
    bool body = false,
    bool verbose = false)
{
    var bundleRoot = Path.GetFullPath(path);

    if (!Directory.Exists(bundleRoot))
    {
        Console.Error.WriteLine($"Bundle directory not found: {bundleRoot}");
        return 1;
    }

    var outPath = @out ?? Path.Combine(bundleRoot, "okf.json");
    var displayName = name ?? new DirectoryInfo(bundleRoot).Name;

    try
    {
        var (concepts, edges) = GraphBuilder.Generate(bundleRoot, outPath, displayName, body);
        Console.WriteLine($"Wrote {outPath} ({concepts} concepts, {edges} edges).");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}
