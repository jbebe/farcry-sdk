using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using Spectre.Console;

namespace JackAll.Cli.Infrastructure;

/// <summary>The body every <c>&lt;format&gt; fragments</c> verb shares: write the units of one
/// container that differ from its base, as files a mod stages.</summary>
internal static class FragmentExport
{
    /// <param name="stageUnder">Where the written fragments go in a mod, for the closing hint.</param>
    public static int Run(
        IContainerTree mine, IContainerTree? vanilla, string input, string? outDir, bool list, string stageUnder)
    {
        IReadOnlyList<FcbFragmentInfo> rows = mine.List();
        IReadOnlyList<FragmentChange> changed = FragmentDiff.Changed(mine, vanilla, rows.Select(r => r.Id));
        int added = changed.Count(c => c.Added);

        AnsiConsole.MarkupLine(
            $"[grey]{input.EscapeMarkup()}[/]: {rows.Count} units, "
            + (vanilla is null
                ? $"writing all {changed.Count}"
                : $"[green]{changed.Count} differ from vanilla[/] ({added} new)"));

        if (changed.Count == 0)
        {
            AnsiConsole.MarkupLine("  [yellow]nothing to stage - this file matches the base[/]");
            return 0;
        }

        long bytes = changed.Sum(c => (long)c.Xml.Length);
        foreach (FragmentChange change in changed.Take(list ? int.MaxValue : 10))
        {
            AnsiConsole.MarkupLine($"    {change.Id.EscapeMarkup()}  [grey]{change.Xml.Length:N0} B[/]");
        }

        if (!list && changed.Count > 10)
        {
            AnsiConsole.MarkupLine($"    [grey]... and {changed.Count - 10} more; pass --list[/]");
        }

        if (list)
        {
            return 0;
        }

        string directory = outDir ?? input + ".fragments";
        foreach (FragmentChange change in changed)
        {
            CliIO.WriteOutput(Path.Combine(directory, change.Id), change.Xml);
        }

        AnsiConsole.MarkupLine(
            $"  wrote [green]{changed.Count}[/] fragments ({bytes:N0} B) to "
            + $"[grey]{directory.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]stage them under {stageUnder.EscapeMarkup()}[/]");
        return 0;
    }
}
