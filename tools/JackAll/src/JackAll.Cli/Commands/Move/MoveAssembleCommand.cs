using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Splices a directory of per-state fragments into a MOVE graph — the other half of
/// <see cref="MoveFragmentsCommand"/>.
/// </summary>
/// <remarks>
/// <c>jackall-cli mod build</c> does this as part of composing a whole layer. This exposes the same
/// step on its own, so a fragment set can be checked against the binary it came from without
/// building a patch: split a modified graph, assemble it back, compare. See
/// docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveAssembleCommand : CliCommand<MoveAssembleCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<base.bin>")]
        [Description("The graph to splice into, normally the retail one.")]
        public string Base { get; init; } = null!;

        [CommandArgument(1, "<fragments-dir>")]
        [Description("A directory of fragment XML files.")]
        public string Fragments { get; init; } = null!;

        [CommandOption("-o|--out <file.bin>")]
        [Description("Where to write the result.")]
        public string? Out { get; init; }

        [CommandOption("--expect <file.bin>")]
        [Description("Compare the result against this file instead of writing one.")]
        public string? Expect { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] baseBytes = CliIO.ReadInput(settings.Base);
        Dictionary<string, string> staged = [];
        foreach (string file in Directory.EnumerateFiles(settings.Fragments, "*.xml"))
        {
            staged[Path.GetFileName(file)] = File.ReadAllText(file);
        }

        if (staged.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]no .xml fragments in {settings.Fragments.EscapeMarkup()}[/]");
            return 1;
        }

        byte[] built = MoveContainerSplitter.Instance.Apply(baseBytes, staged);
        AnsiConsole.MarkupLine(
            $"spliced [green]{staged.Count}[/] fragments into "
            + $"[grey]{Path.GetFileName(settings.Base).EscapeMarkup()}[/] -> {built.Length:N0} B");

        if (settings.Expect is not null)
        {
            byte[] expected = CliIO.ReadInput(settings.Expect);
            if (built.AsSpan().SequenceEqual(expected))
            {
                AnsiConsole.MarkupLine("  [green]byte-identical to the expected graph[/]");
                return 0;
            }

            int at = 0;
            while (at < Math.Min(built.Length, expected.Length) && built[at] == expected[at])
            {
                at++;
            }

            AnsiConsole.MarkupLine(
                $"  [red]differs from the expected graph[/] at 0x{at:x} "
                + $"(built {built.Length:N0} B, expected {expected.Length:N0} B)");
            return 1;
        }

        if (settings.Out is null)
        {
            AnsiConsole.MarkupLine("  [yellow]nothing written; pass --out or --expect[/]");
            return 0;
        }

        File.WriteAllBytes(settings.Out, built);
        AnsiConsole.MarkupLine($"  wrote [grey]{settings.Out.EscapeMarkup()}[/]");
        return 0;
    }
}
