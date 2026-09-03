using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Move;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Recovers the names behind a MOVE graph's hashes from its authoring twin, and writes the table
/// JackAll labels fragments with.
/// </summary>
/// <remarks>
/// A loadable graph carries no names, and the twin that does is a format no engine reads and that is
/// decoded to about 2% of its length. This needs neither: it hashes every string the twin holds and
/// keeps the ones the loadable graph actually keys on, so a match <em>is</em> the proof. Measured at
/// 100% of <c>movemgr.bin</c>'s 1,700 state names.
///
/// Pass the <c>*named.bin</c> twins; the loadable graph beside each is found by dropping "named".
/// See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveNamesCommand : CliCommand<MoveNamesCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<named.bin...>")]
        [Description("The authoring twins to harvest, e.g. movemgrnamed.bin dlc1named.bin.")]
        public string[] Twins { get; init; } = [];

        [CommandOption("-o|--out <file.tsv>")]
        [Description("Where to write the table. Prints a summary only when omitted.")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        MoveNames all = MoveNames.Empty;
        foreach (string twin in settings.Twins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LoadableBeside(twin) is not { } graph)
            {
                AnsiConsole.MarkupLine(
                    $"[red]no loadable graph beside {Path.GetFileName(twin).EscapeMarkup()}[/] "
                    + "[grey](expected the same name without \"named\")[/]");
                return 1;
            }

            MoveFile file = MoveCodec.Load(File.ReadAllBytes(graph));
            HashSet<uint> wanted = MoveNames.HashesIn(file);
            MoveNames found = MoveNames.Harvest(File.ReadAllBytes(twin), wanted);

            AnsiConsole.MarkupLine(
                $"[grey]{Path.GetFileName(graph).EscapeMarkup()}[/]: {wanted.Count} hashes, "
                + $"[green]{found.Count} named[/] ({100.0 * found.Count / Math.Max(1, wanted.Count):F1}%)");
            all = all.MergedWith(found);
        }

        AnsiConsole.MarkupLine($"  {all.Count} names in total");
        foreach ((uint hash, string name) in all.All.Take(5))
        {
            AnsiConsole.MarkupLine($"    {hash:X8}  [grey]{name.EscapeMarkup()}[/]");
        }

        if (settings.Out is null)
        {
            return 0;
        }

        File.WriteAllText(settings.Out, all.ToTsv());
        AnsiConsole.MarkupLine($"  wrote [grey]{settings.Out.EscapeMarkup()}[/]");
        return 0;
    }

    /// <summary>The loadable graph beside a twin: the same name with "named" dropped.</summary>
    private static string? LoadableBeside(string twin)
    {
        string name = Path.GetFileNameWithoutExtension(twin);
        if (!name.EndsWith("named", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string candidate = Path.Combine(
            Path.GetDirectoryName(twin) ?? ".",
            name[..^"named".Length] + Path.GetExtension(twin));
        return File.Exists(candidate) ? candidate : null;
    }
}
