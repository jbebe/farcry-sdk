using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Mods;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Writes a MOVE graph out as fragments, keeping only the units that differ from vanilla.
/// </summary>
/// <remarks>
/// This is how an existing whole-file override becomes a mod layer. A graph is 1.8 MB and a weapon
/// mod changes a few dozen clip references in it, so shipping the binary means shipping 1.8 MB to say
/// a few hundred bytes - and, worse, whole-file overrides are last-wins and silent, so two mods that
/// each retarget an animation cannot coexist and neither is told.
///
/// Point <c>--base</c> at the retail graph and the output is the diff, staged straight into
/// <c>mods\graphics\move\movemgr.bin\</c>. For the VSS Vintorez that is 17 files and 265 KB, all of
/// them branches of the one weapon it replaces. See docs/docs/file-formats/move.md.
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
        => FragmentExport.Run(
            new MoveContainerSplitter(BundledAssets.LoadMoveNames()),
            settings.Input, settings.Base, settings.Out, settings.List,
            unit: "units",
            stageUnder: $"mods\\graphics\\move\\{Path.GetFileName(settings.Input)}\\",
            cancellationToken);
}
