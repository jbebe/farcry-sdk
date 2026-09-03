using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using Spectre.Console;

namespace JackAll.Cli.Infrastructure;

/// <summary>
/// Writes a container out as the fragments a mod stages, keeping only what differs from vanilla.
/// </summary>
/// <remarks>
/// This is how an existing whole-file override becomes a mod layer, and it is the same job whatever
/// the container is - <see cref="IContainerSplitter"/> is the only thing either side speaks. What
/// differs per format is what a fragment is called and where a layer puts it.
/// </remarks>
public static class FragmentExport
{
    private const int Listed = 10;

    public static int Run(
        IContainerSplitter splitter,
        string input,
        string? basePath,
        string? outDirectory,
        bool listOnly,
        string unit,
        string stageUnder,
        CancellationToken cancellationToken)
    {
        IContainerTree mine = splitter.Open(CliIO.ReadInput(input));
        IContainerTree? vanilla = basePath is null ? null : splitter.Open(CliIO.ReadInput(basePath));

        IReadOnlyList<FcbFragmentInfo> rows = mine.List();
        List<(string Id, string Xml)> changed = [];
        int added = 0;
        foreach (FcbFragmentInfo row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string xml = mine.Extract(row.Id)!;
            string? before = vanilla?.Extract(row.Id);
            if (vanilla is not null && before == xml)
            {
                continue;
            }

            if (vanilla is not null && before is null)
            {
                added++;
            }

            changed.Add((row.Id, xml));
        }

        AnsiConsole.MarkupLine(
            $"[grey]{input.EscapeMarkup()}[/]: {rows.Count} {unit}, "
            + (vanilla is null
                ? $"writing all {changed.Count}"
                : $"[green]{changed.Count} differ from vanilla[/] ({added} new)"));

        if (changed.Count == 0)
        {
            AnsiConsole.MarkupLine("  [yellow]nothing to stage - this file matches the base[/]");
            return 0;
        }

        long bytes = changed.Sum(c => (long)c.Xml.Length);
        foreach ((string id, string xml) in changed.Take(listOnly ? int.MaxValue : Listed))
        {
            AnsiConsole.MarkupLine($"    {id.EscapeMarkup()}  [grey]{xml.Length:N0} B[/]");
        }

        if (!listOnly && changed.Count > Listed)
        {
            AnsiConsole.MarkupLine($"    [grey]... and {changed.Count - Listed} more; pass --list[/]");
        }

        if (listOnly)
        {
            return 0;
        }

        string directory = outDirectory ?? input + ".fragments";
        Directory.CreateDirectory(directory);
        foreach ((string id, string xml) in changed)
        {
            CliIO.WriteOutput(Path.Combine(directory, id), xml);
        }

        AnsiConsole.MarkupLine(
            $"  wrote [green]{changed.Count}[/] fragments ({bytes:N0} B) to "
            + $"[grey]{directory.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]stage them under {stageUnder.EscapeMarkup()}[/]");
        return 0;
    }
}
