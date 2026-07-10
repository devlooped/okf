using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConsoleAppFramework;
using Devlooped;

var runArgs = new List<string>(args);

if (runArgs.IndexOf("--debug") is var debugIdx and not -1)
{
    Debugger.Launch();
    runArgs.RemoveAt(debugIdx);
}

var app = ConsoleApp.Create();
app.Add("check", Check);
app.Add("viz", Visualize);
app.Add("graph", Graph);
app.Run([.. runArgs]);

/// <summary>Validate an OKF bundle directory for structural and content issues.</summary>
/// <param name="path">Path to the bundle directory. [Default: .]</param>
/// <param name="json">Output validation issues as JSON instead of human-readable text. [Default: false]</param>
static int Check([Argument] string path = ".", bool json = false)
{
    var result = new BundleChecker(path).Check();

    if (json)
    {
        CheckRenderer.RenderJson(result, path, Console.Out);
    }
    else
    {
        ReportCheckResult(result, path);
    }

    return result.Errors.Count > 0 ? 1 : 0;
}

static void ReportCheckResult(BundleCheckResult result, string bundleRoot, bool quiet = false)
{
    CheckRenderer.Render(result, bundleRoot, quiet);

    if (result.Errors.Count > 0)
    {
        Console.Error.WriteLine($"{result.Errors.Count} error(s).");
    }

    if (result.Warnings.Count > 0)
    {
        Console.Error.WriteLine($"{result.Warnings.Count} warning(s).");
    }
}

/// <summary>Generate an interactive HTML visualization from a bundle or graph file.</summary>
/// <param name="path">Path to a bundle directory or .json graph file. [Default: .]</param>
/// <param name="out">-o, Output path for the generated HTML file. [Default: viz.html]</param>
/// <param name="name">Display name shown in the visualization title. [Default: directory or file name]</param>
/// <param name="open">Open the generated HTML in the default browser after writing. [Default: false]</param>
static int Visualize(
    [Argument] string path = ".",
    string? @out = null,
    string? name = null,
    bool open = false)
{
    var fullPath = Path.GetFullPath(path);
    GraphBuilder.KnowledgeGraph? graph = null;
    string displayName;
    string outputBaseDir;

    try
    {
        if (File.Exists(fullPath) &&
            fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            graph = GraphBuilder.Load(fullPath);
            displayName = name ?? Path.GetFileNameWithoutExtension(fullPath);
            outputBaseDir = Path.GetDirectoryName(fullPath) ?? ".";
        }
        else if (Directory.Exists(fullPath))
        {
            var checkResult = new BundleChecker(fullPath).Check();
            ReportCheckResult(checkResult, fullPath);

            if (checkResult.Errors.Count > 0)
            {
                return 1;
            }

            graph = GraphBuilder.Build(fullPath, includeBody: true);
            displayName = name ?? new DirectoryInfo(fullPath).Name;
            outputBaseDir = fullPath;
        }
        else
        {
            Console.Error.WriteLine($"Path is not a directory or .json graph file: {fullPath}");
            return 1;
        }

        var outPath = @out ?? Path.Combine(outputBaseDir, "viz.html");

        var stats = BundleVisualizer.Generate(graph, outPath, displayName);
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

/// <summary>Generate an OKF graph file for the bundle.</summary>
/// <param name="path">Path to the bundle directory. [Default: .]</param>
/// <param name="out">-o, Output path for the generated graph file. [Default: okf.json, or okf.js with --js]</param>
/// <param name="body">-b, Include body content in the graph. [Default: false]</param>
/// <param name="nav">Include index-driven navigation tree (nav). [Default: false]</param>
/// <param name="js">Emit a plain JS script that sets window.data (loadable via &lt;script src&gt; on file:// too). [Default: false]</param>
/// <param name="quiet">-q, Only render errors and warnings. [Default: false]</param>
/// <param name="json">Output validation issues as JSON instead of human-readable text. [Default: false]</param>
/// <param name="properties">-p, Properties in format Key=Value. Can be repeated.</param>
static int Graph(
    [Argument, DefaultValue(".")] string path = ".",
    [HideDefaultValue] string? @out = null,
    bool body = false,
    bool nav = false,
    bool js = false,
    bool quiet = false,
    bool json = false,
    params string[]? properties)
{
    var bundleRoot = Path.GetFullPath(path);

    if (!Directory.Exists(bundleRoot))
    {
        Console.Error.WriteLine($"Bundle directory not found: {bundleRoot}");
        return 1;
    }

    var outPath = @out ?? Path.Combine(bundleRoot, js ? "okf.js" : "okf.json");
    var bundleProperties = Converters.ParseKeyValue(properties);
    var checkResult = new BundleChecker(bundleRoot).Check();

    if (json)
    {
        CheckRenderer.RenderJson(checkResult, bundleRoot, Console.Out);
    }
    else
    {
        ReportCheckResult(checkResult, bundleRoot, quiet);
    }

    if (checkResult.Errors.Count > 0)
    {
        return 1;
    }

    try
    {
        var (nodes, edges) = GraphBuilder.Generate(
            bundleRoot,
            outPath,
            body,
            bundleProperties,
            js,
            nav);

        if (!json)
        {
            var navNote = nav ? ", + nav" : "";
            Console.WriteLine($"Wrote {outPath} ({nodes} nodes, {edges} edges{navNote}).");
        }

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}
