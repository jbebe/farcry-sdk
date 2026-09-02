using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;

using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.DepLoad;

/// <summary>Checks that a `depload.dat` holds together and reads back to itself.</summary>
/// <remarks>Worth running on anything hand-edited or merged: a parents array in the wrong order still
/// loads, and shows up as animations misbehaving in game rather than as an error. See
/// docs/docs/file-formats/depload.md.</remarks>
public sealed class DepLoadValidateCommand : CliCommand<DepLoadValidateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file_depload.dat>")]
        [Description("The dependency index to check.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] original = CliIO.ReadInput(settings.Input);
        DepLoadFile file = DepLoadDocument.Decode(original);

        IReadOnlyList<string> problems = DepLoadValidate.Problems(file);
        foreach (string problem in problems)
        {
            AnsiConsole.MarkupLine($"[red]![/] {problem.EscapeMarkup()}");
        }

        byte[] rebuilt = DepLoadDocument.Encode(file);
        bool rebuilds = rebuilt.AsSpan().SequenceEqual(original);
        if (!rebuilds)
        {
            AnsiConsole.MarkupLine(
                "[yellow]![/] This file does not re-encode to itself, so it was not written by the "
                + "game's own exporter. It should still load; the layout just differs.");
        }

        int children = file.Parents.Sum(p => p.Children.Count);
        AnsiConsole.MarkupLine($"{file.Parents.Count} parents, {children} children.");

        if (problems.Count > 0)
        {
            return 1;
        }

        AnsiConsole.MarkupLine(rebuilds ? "[green]OK[/]" : "[green]OK[/] [dim](re-encodes differently)[/]");
        return 0;
    }
}
