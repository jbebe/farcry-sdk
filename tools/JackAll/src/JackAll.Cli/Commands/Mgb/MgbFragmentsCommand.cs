using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Mgb;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mgb;

/// <summary>
/// Writes a .mgb package out as fragments, keeping only the areas and lists that differ from vanilla.
/// </summary>
/// <remarks>
/// A whole package in a mod is last-wins against every other mod that edits it, and the HUD is the
/// package every HUD mod edits. Fragments merge instead. The input may be the XML the package is built
/// from, so the names it declares label the fragments rather than their hashes.
/// </remarks>
public sealed class MgbFragmentsCommand : CliCommand<MgbFragmentsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("The edited .mgb, or the .xml it is built from.")]
        public string Input { get; init; } = null!;

        [CommandOption("-b|--base <file.mgb>")]
        [Description("The retail package to diff against. Without it every fragment is written.")]
        public string? Base { get; init; }

        [CommandOption("-o|--out <dir>")]
        [Description("Where to write the fragments. Defaults to <file>.fragments.")]
        public string? Out { get; init; }

        [CommandOption("--list")]
        [Description("Report what would be written without writing it.")]
        public bool List { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        var names = new MgbNameLookup();
        byte[] bytes = MgbInput.Read(settings.Input, names);

        var splitter = new MgbContainerSplitter(names.Names);
        IContainerTree mine = splitter.Open(bytes);
        IContainerTree? vanilla = settings.Base is null ? null : splitter.Open(CliIO.ReadInput(settings.Base));

        if (vanilla is not null && !FragmentDiff.IsExpressible(mine, vanilla))
        {
            AnsiConsole.MarkupLine(
                "[red]This package removes an area or list, or changes something outside them - the "
                + "header, the type table or the fonts - which no fragment carries. Ship the whole package.[/]");
            return 1;
        }

        return FragmentExport.Run(mine, vanilla, settings.Input, settings.Out, settings.List,
            $@"a folder named {Path.GetFileNameWithoutExtension(settings.Base ?? settings.Input)}.mgb\ at the package's own path");
    }
}
