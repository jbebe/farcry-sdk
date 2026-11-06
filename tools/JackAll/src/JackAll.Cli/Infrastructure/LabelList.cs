using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Core.Format;
using JackAll.Core.Vfs;
using JackAll.Tools.Labels;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Infrastructure;

/// <summary>Options shared by the commands that write a label list the App reads.</summary>
public class LabelCommandSettings : GameCommandSettings
{
    [CommandOption("-o|--out <file>")]
    [Description("Where to write the label list (default: the command's fc2.*labels.tsv in the current folder).")]
    public string? Output { get; init; }

    [CommandOption("--hand <file>")]
    [Description("A label list written by hand (same columns); its rows replace found labels and are kept on later runs.")]
    public string? Hand { get; init; }
}

/// <summary>Writes a label list with every hand label kept, then reports what it holds.</summary>
public static class LabelList
{
    /// <param name="covers">Which game files the list is for; a hand label for anything else is reported.</param>
    /// <param name="noun">What those files are called in the summary, e.g. ".spk files".</param>
    public static int Write(LabelCommandSettings settings, string defaultOutput, GameVfs vfs,
        IReadOnlyList<FileLabel> found, Func<VfsFile, bool> covers, string noun)
    {
        string output = settings.Output ?? defaultOutput;
        FileLabelTable table = FileLabelTable.MergeHand(found, output, settings.Hand);
        CliIO.WriteOutput(output, string.Join(Environment.NewLine, table.Write()) + Environment.NewLine);

        IReadOnlyList<FileLabel> labels = [.. table.Labels];
        foreach (FileLabel stray in labels.Where(l => l.Source == FileLabel.FromHand
            && (!vfs.Files.TryGetValue(NameHash.Compute(l.Path), out VfsFile? file) || !covers(file))))
        {
            AnsiConsole.MarkupLine($"[yellow]Hand label for a file the game does not have:[/] {stray.Path.EscapeMarkup()}");
        }

        int files = vfs.Files.Values.Count(f => !f.IsSynthetic && covers(f));
        var byCategory = labels.GroupBy(l => l.Category).OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var bySource = labels.GroupBy(l => l.Source).ToDictionary(g => g.Key, g => g.Count());

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, path = Path.GetFullPath(output), files, labelled = labels.Count, byCategory, bySource });
            return 0;
        }

        AnsiConsole.MarkupLine($"Labelled [bold]{labels.Count:N0}[/] of {files:N0} {noun.EscapeMarkup()} → {Path.GetFullPath(output).EscapeMarkup()}");
        AnsiConsole.MarkupLine("  " + string.Join("  ", bySource.Select(kv => $"{kv.Key} {kv.Value:N0}")).EscapeMarkup());
        foreach ((string category, int count) in byCategory)
        {
            AnsiConsole.MarkupLine($"  {count,6:N0}  {category.EscapeMarkup()}");
        }
        return 0;
    }
}
