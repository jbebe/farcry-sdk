using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Writes a MOVE graph out as per-state fragments, keeping only the states that differ from vanilla.
/// </summary>
/// <remarks>
/// This is how an existing whole-file override becomes a mod layer. A graph is 1.8 MB and a weapon
/// mod usually changes one state in it, so shipping the binary means shipping 1.8 MB to say a few
/// hundred bytes - and, worse, whole-file overrides are last-wins and silent, so two mods that each
/// retarget an animation cannot coexist and neither is told.
///
/// Point <c>--base</c> at the retail graph and the output is the diff: one file per changed state,
/// staged straight into <c>mods\graphics\move\movemgr.bin\</c>. See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveFragmentsCommand : CliCommand<MoveFragmentsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.bin>")]
        [Description("The MOVE graph to split.")]
        public string Input { get; init; } = null!;

        [CommandOption("-b|--base <file.bin>")]
        [Description("The retail graph to diff against. Without it every state is written.")]
        public string? Base { get; init; }

        [CommandOption("-o|--out <dir>")]
        [Description("Where to write the fragments. Defaults to <file.bin>.fragments.")]
        public string? Out { get; init; }

        [CommandOption("--list")]
        [Description("Report what would be written without writing it.")]
        public bool List { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        MoveContainerSplitter splitter = MoveContainerSplitter.Instance;
        IContainerTree mine = splitter.Open(CliIO.ReadInput(settings.Input));
        IContainerTree? vanilla = settings.Base is null
            ? null
            : splitter.Open(CliIO.ReadInput(settings.Base));

        List<(string Id, string Xml)> changed = [];
        int added = 0;
        foreach (FcbFragmentInfo row in mine.List())
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
            $"[grey]{settings.Input.EscapeMarkup()}[/]: {mine.List().Count} states, "
            + (vanilla is null
                ? $"writing all {changed.Count}"
                : $"[green]{changed.Count} differ from vanilla[/] ({added} new)"));

        if (changed.Count == 0)
        {
            AnsiConsole.MarkupLine("  [yellow]nothing to stage - this graph matches the base[/]");
            return 0;
        }

        long bytes = changed.Sum(c => (long)c.Xml.Length);
        foreach ((string id, string xml) in changed.Take(settings.List ? int.MaxValue : 10))
        {
            AnsiConsole.MarkupLine($"    {id.EscapeMarkup()}  [grey]{xml.Length:N0} B[/]");
        }

        if (!settings.List && changed.Count > 10)
        {
            AnsiConsole.MarkupLine($"    [grey]... and {changed.Count - 10} more; pass --list[/]");
        }

        if (settings.List)
        {
            return 0;
        }

        string directory = settings.Out ?? settings.Input + ".fragments";
        Directory.CreateDirectory(directory);
        foreach ((string id, string xml) in changed)
        {
            File.WriteAllText(Path.Combine(directory, id), xml);
        }

        AnsiConsole.MarkupLine(
            $"  wrote [green]{changed.Count}[/] fragments ({bytes:N0} B) to "
            + $"[grey]{directory.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine(
            $"  [grey]stage them under mods\\graphics\\move\\{Path.GetFileName(settings.Input).EscapeMarkup()}\\[/]");
        return 0;
    }
}
