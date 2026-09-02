using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Naming;
using JackAll.Core.Format.Move;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Reports clip references a MOVE graph makes that no known path hashes to.
/// </summary>
/// <remarks>
/// A mistyped path in a repoint map produces a graph that parses, round-trips and plays nothing,
/// because the reference is only a CRC32 and nothing checks it. This is the check that catches it.
/// A name the dictionary does not know is not proof the clip is missing, so unresolved references
/// are reported as unknown rather than as errors.
/// </remarks>
public sealed class MoveValidateCommand : CliCommand<MoveValidateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.bin>")]
        [Description("The MOVE graph to check.")]
        public string Input { get; init; } = null!;

        [CommandOption("-l|--list")]
        [Description("List every unresolved reference rather than only counting them.")]
        public bool List { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        MoveFile file = MoveCodec.Load(CliIO.ReadInput(settings.Input));
        NameDatabase names = BundledAssets.LoadNames();
        IReadOnlyDictionary<uint, int> clips = MoveWeapons.AllClipReferences(file);

        List<KeyValuePair<uint, int>> unresolved =
            [.. clips.Where(c => !names.TryResolve(c.Key, out _)).OrderByDescending(c => c.Value)];

        AnsiConsole.MarkupLine(
            $"[grey]{settings.Input.EscapeMarkup()}[/]: {clips.Count} distinct clips, "
            + $"{clips.Values.Sum()} references");

        if (unresolved.Count == 0)
        {
            AnsiConsole.MarkupLine("  [green]every clip reference resolves to a known path[/]");
            return 0;
        }

        // Unknown is not the same as dangling: a mod's own clip at an invented path hashes to
        // something no dictionary has ever seen, and the engine loads it from patch.dat anyway.
        AnsiConsole.MarkupLine(
            $"  [yellow]{unresolved.Count} clips hash to no path the dictionary knows[/] "
            + "[grey](expected for mod-added clips; not proof the clip is missing)[/]");
        foreach ((uint hash, int count) in settings.List ? unresolved : unresolved.Take(10))
        {
            AnsiConsole.MarkupLine($"    {hash:X8}  {count}x");
        }

        if (!settings.List && unresolved.Count > 10)
        {
            AnsiConsole.MarkupLine($"    [grey]... and {unresolved.Count - 10} more; pass --list[/]");
        }

        return 0;
    }
}
