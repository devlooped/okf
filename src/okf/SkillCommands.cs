using System.Text;
using ConsoleAppFramework;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Devlooped;

public class SkillCommands
{
    public static string Markdown => ThisAssembly.Resources.SKILL.Text;

    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Installs the bundled okf agent skill (SKILL.md) for agent tooling.</summary>
    /// <param name="directory">Optional base directory. Use '.' for the current directory. Writes to .agents/skills/okf/SKILL.md under that base. Omit together with --global to choose Local vs Global interactively.</param>
    /// <param name="yes">-y, Skip confirmation prompt.</param>
    /// <param name="global">-g, Install under the user home directory.</param>
    [Command("skill")]
    public int Skill([Argument] string? directory = null, bool yes = false, bool global = false)
    {
        var resolved = ResolveInstallDestination(directory, global);
        return resolved.Kind switch
        {
            DestinationKind.Error => Fail(resolved.Error!),
            DestinationKind.Prompt => PromptInstall(),
            _ => ConfirmThen(resolved.Path!, yes, resolved.Confirm, Install, "Install okf skill to", "green"),
        };
    }

    /// <summary>Removes a previously installed okf agent skill.</summary>
    /// <param name="directory">Optional base directory. Use '.' for the current directory. Omit together with --global to remove the only installed copy, or choose when both Local and Global exist.</param>
    /// <param name="yes">-y, Skip confirmation prompt.</param>
    /// <param name="global">-g, Remove from the user home directory.</param>
    [Command("skill remove")]
    public int Remove([Argument] string? directory = null, bool yes = false, bool global = false)
    {
        var resolved = ResolveRemoveDestination(directory, global);
        return resolved.Kind switch
        {
            DestinationKind.Error => Fail(resolved.Error!),
            DestinationKind.Prompt => PromptRemove(),
            _ => ConfirmThen(resolved.Path!, yes, resolved.Confirm, Uninstall, "Remove okf skill from", "yellow"),
        };
    }

    internal static int Install(string dest)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, Markdown, Utf8NoBom);
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleApp.LogError($"Failed to install skill: {ex.Message}");
            return 1;
        }
    }

    internal static int Uninstall(string dest)
    {
        try
        {
            if (File.Exists(dest))
                File.Delete(dest);

            var skillDir = Path.GetDirectoryName(dest);
            if (skillDir is not null &&
                Directory.Exists(skillDir) &&
                !Directory.EnumerateFileSystemEntries(skillDir).Any())
            {
                Directory.Delete(skillDir);
            }

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleApp.LogError($"Failed to remove skill: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Resolves the install path for the okf skill under the given base directory
    /// (or the user home directory when <paramref name="directory"/> is omitted).
    /// </summary>
    internal static string ResolveSkillPath(string? directory)
    {
        var root = string.IsNullOrWhiteSpace(directory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(directory);

        return Path.GetFullPath(Path.Combine(root, ".agents", "skills", "okf", "SKILL.md"));
    }

    /// <summary>
    /// Display path for the Local (<c>.</c>) or Global (<c>~</c>) scope. Uses
    /// <c>~</c> even on Windows (never expands the user profile).
    /// </summary>
    internal static string FormatScopePath(bool global)
        => Path.Combine(global ? "~" : ".", ".agents", "skills", "okf", "SKILL.md");

    internal static DestinationResolution ResolveInstallDestination(string? directory, bool global)
    {
        if (HasDirectory(directory) && global)
            return DestinationResolution.Fail("Specify a directory or --global, not both.");

        if (global)
            return DestinationResolution.Target(ResolveSkillPath(null), confirm: true);

        if (HasDirectory(directory))
            return DestinationResolution.Target(ResolveSkillPath(directory), confirm: true);

        return DestinationResolution.Prompt();
    }

    internal static DestinationResolution ResolveRemoveDestination(string? directory, bool global)
        => ResolveRemoveDestination(directory, global, ResolveSkillPath("."), ResolveSkillPath(null));

    internal static DestinationResolution ResolveRemoveDestination(
        string? directory,
        bool global,
        string localDest,
        string globalDest)
    {
        if (HasDirectory(directory) && global)
            return DestinationResolution.Fail("Specify a directory or --global, not both.");

        if (global)
            return DestinationResolution.Target(ResolveSkillPath(null), confirm: true);

        if (HasDirectory(directory))
            return DestinationResolution.Target(ResolveSkillPath(directory), confirm: true);

        if (SamePath(localDest, globalDest))
            return DestinationResolution.Target(localDest, confirm: false);

        var localExists = File.Exists(localDest);
        var globalExists = File.Exists(globalDest);

        if (localExists && globalExists)
            return DestinationResolution.Prompt();

        if (localExists)
            return DestinationResolution.Target(localDest, confirm: false);

        if (globalExists)
            return DestinationResolution.Target(globalDest, confirm: false);

        return DestinationResolution.Target(localDest, confirm: false);
    }

    internal enum DestinationKind
    {
        Target,
        Prompt,
        Error,
    }

    internal readonly record struct DestinationResolution(
        DestinationKind Kind,
        string? Path,
        string? Error,
        bool Confirm)
    {
        public static DestinationResolution Target(string path, bool confirm)
            => new(DestinationKind.Target, path, null, confirm);

        public static DestinationResolution Prompt()
            => new(DestinationKind.Prompt, null, null, false);

        public static DestinationResolution Fail(string message)
            => new(DestinationKind.Error, null, message, false);
    }

    internal enum SkillScope
    {
        Local,
        Global,
    }

    internal static string RenderScopeLine(string label, string path, bool selected)
    {
        var escaped = Markup.Escape(path);
        return selected
            ? $"[green]●[/] {label} [grey]({escaped})[/]"
            : $"[grey]○ {label} ({escaped})[/]";
    }

    static int PromptInstall()
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            return Fail("Specify a directory or pass --global when not running interactively.");
        }

        var choice = PromptScope("Installation scope",
        [
            (SkillScope.Local, FormatScopePath(false)),
            (SkillScope.Global, FormatScopePath(true)),
        ]);

        if (choice is null)
            return 0;

        return Install(DestFor(choice.Value));
    }

    static int PromptRemove()
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            return Fail("Both local and global okf skills are installed. Specify a directory or pass --global.");
        }

        var choice = PromptScope("Removal scope",
        [
            (SkillScope.Local, FormatScopePath(false)),
            (SkillScope.Global, FormatScopePath(true)),
        ]);

        if (choice is null)
            return 0;

        return Uninstall(DestFor(choice.Value));
    }

    static SkillScope? PromptScope(string title, IReadOnlyList<(SkillScope Scope, string DisplayPath)> options)
    {
        var index = 0;
        SkillScope? result = null;

        AnsiConsole.Live(RenderScopePrompt(title, options, index, showFooter: true))
            .AutoClear(false)
            .Start(ctx =>
            {
                while (true)
                {
                    var key = AnsiConsole.Console.Input.ReadKey(intercept: true);
                    if (key is null)
                        return;

                    switch (key.Value.Key)
                    {
                        case ConsoleKey.UpArrow:
                            index = (index + options.Count - 1) % options.Count;
                            break;
                        case ConsoleKey.DownArrow:
                            index = (index + 1) % options.Count;
                            break;
                        case ConsoleKey.Enter:
                            result = options[index].Scope;
                            ctx.UpdateTarget(RenderScopePrompt(title, options, index, showFooter: false));
                            return;
                        case ConsoleKey.Escape:
                            ctx.UpdateTarget(Text.Empty);
                            return;
                        case ConsoleKey.C when key.Value.Modifiers.HasFlag(ConsoleModifiers.Control):
                            ctx.UpdateTarget(Text.Empty);
                            return;
                        default:
                            continue;
                    }

                    ctx.UpdateTarget(RenderScopePrompt(title, options, index, showFooter: true));
                }
            });

        return result;
    }

    static IRenderable RenderScopePrompt(
        string title,
        IReadOnlyList<(SkillScope Scope, string DisplayPath)> options,
        int selected,
        bool showFooter)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[grey]{Markup.Escape(title)}[/]"),
        };

        for (var i = 0; i < options.Count; i++)
        {
            var label = options[i].Scope == SkillScope.Local ? "Local" : "Global";
            rows.Add(new Markup(RenderScopeLine(label, options[i].DisplayPath, i == selected)));
        }

        if (showFooter)
            rows.Add(new Markup("[grey]↑/↓ to navigate • Enter: confirm[/]"));

        return new Rows(rows);
    }

    static int ConfirmThen(string dest, bool yes, bool confirm, Func<string, int> action, string verb, string color)
    {
        if (confirm && !yes &&
            !AnsiConsole.Confirm($"{verb} [{color}]{Markup.Escape(dest)}[/]?", defaultValue: true))
        {
            return 0;
        }

        return action(dest);
    }

    static string DestFor(SkillScope scope)
        => scope == SkillScope.Global ? ResolveSkillPath(null) : ResolveSkillPath(".");

    static bool HasDirectory(string? directory) => !string.IsNullOrWhiteSpace(directory);

    static bool SamePath(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    static int Fail(string message)
    {
        ConsoleApp.LogError(message);
        return 1;
    }
}
