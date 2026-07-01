using ConsoleAppFramework;
using okf;

var app = ConsoleApp.Create();
app.Add("check", Check);
app.Add("viz", Viz);
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

static void Viz()
{
}
