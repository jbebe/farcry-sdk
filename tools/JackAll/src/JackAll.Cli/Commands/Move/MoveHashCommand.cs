using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>Prints the CPathID a game path hashes to, which is how MOVE names every clip.</summary>
/// <remarks>Needed the moment anyone writes a repoint map by hand: the graph stores only hashes,
/// so there is otherwise no way to check that a path you typed is the one you meant.</remarks>
public sealed class MoveHashCommand : CliCommand<MoveHashCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description(@"A game path, e.g. graphics\characters\...\clip.mab. Repeatable.")]
        public string[] Paths { get; init; } = [];
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        foreach (string path in settings.Paths)
        {
            AnsiConsole.MarkupLine($"{NameHash.Compute(path):X8}  {path.EscapeMarkup()}");
        }

        return 0;
    }
}
