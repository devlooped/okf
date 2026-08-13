namespace Devlooped;

/// <summary>
/// ConsoleAppFramework shows the tool version when the remaining args are
/// exactly <c>--version</c>. Keep that only for a bare invocation.
/// </summary>
static class CafArgs
{
    /// <summary>
    /// When a command (or any other arg) is present, give a valueless
    /// <c>--version</c> its default format value so CAF does not steal it
    /// as the tool-version flag.
    /// </summary>
    public static string[] RestrictToolVersion(string[] args)
    {
        if (args.Length <= 1)
            return args;

        List<string>? rewritten = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not "--version")
            {
                continue;
            }

            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith('-');
            if (hasValue)
            {
                continue;
            }

            rewritten ??= [.. args];
            rewritten.Insert(i + 1, "latest");
            i++;
        }

        return rewritten is null ? args : [.. rewritten];
    }
}
