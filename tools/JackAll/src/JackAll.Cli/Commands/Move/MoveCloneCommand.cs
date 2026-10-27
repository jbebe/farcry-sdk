using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format.Move;
using JackAll.Core.Format.Move.Rules;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Gives a new EquippedWeapon index a copy of every branch a donor index has, the Animations tab's
/// copy of a weapon's set. The copies play the donor's clips until they are repointed.
/// </summary>
public sealed class MoveCloneCommand : CliCommand<MoveCloneCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.bin>")]
        [Description("The MOVE graph to add to.")]
        public string Input { get; init; } = null!;

        [CommandArgument(1, "<out.bin>")]
        [Description("Where to write the graph with the copies.")]
        public string Output { get; init; } = null!;

        [CommandOption("-f|--from <N>")]
        [Description("The index whose branches are copied.")]
        public int From { get; init; }

        [CommandOption("-t|--to <N>")]
        [Description("The index the copies are pinned to.")]
        public int To { get; init; }

        [CommandOption("-p|--package <NAME>")]
        [Description("Move the copies' clips into this animation package, registered in the package list.")]
        public string? Package { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] graph = CliIO.ReadInput(settings.Input);
        CliIO.GuardNotOverwritingInput(settings.Input, settings.Output);
        MoveNames names = BundledAssets.LoadMoveNames();
        MoveEditSession session = new(graph, null, names, null);

        MoveCloneResult result = session.CloneWeapon(settings.From, settings.To, settings.Package);

        AnsiConsole.MarkupLine(
            $"[grey]{settings.Input.EscapeMarkup()}[/]: {result.Branches} branches of index {settings.From} "
            + $"copied to {settings.To}, in {result.States.Count} states");
        foreach (uint state in result.States)
        {
            AnsiConsole.MarkupLine($"  {state:X8}  {(names.Of(state) ?? "").EscapeMarkup()}");
        }

        foreach (string skipped in result.Skipped)
        {
            AnsiConsole.MarkupLine($"  [yellow]![/] {skipped.EscapeMarkup()}");
        }

        CliIO.WriteOutput(settings.Output, MoveCodec.Save(session.File));
        CliIO.ReportWrote(settings.Output);
        return result.Skipped.Count == 0 ? 0 : 1;
    }
}
