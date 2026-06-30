using ConsoleAppFramework;
using okf;

var app = ConsoleApp.Create();
app.Add("check", Check);
app.Add("viz", Viz);
app.Run(args);

static int Check([Argument] string path = ".")
{
    var checker = new BundleChecker(path);
    var issues = checker.Check();

    foreach (var issue in issues)
    {
        Console.WriteLine(issue);
    }

    if (issues.Count == 0)
    {
        Console.WriteLine($"OK: {Path.GetFullPath(path)}");
        return 0;
    }

    var errors = issues.Count(issue => issue.Severity == IssueSeverity.Error);
    Console.Error.WriteLine($"{errors} error(s), {issues.Count - errors} warning(s).");
    return errors > 0 ? 1 : 0;
}

static void Viz()
{
}
