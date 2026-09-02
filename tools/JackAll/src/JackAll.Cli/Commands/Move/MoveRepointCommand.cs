using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Naming;
using JackAll.Core.Format.Move;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Retargets the clips one weapon plays, from a map of old to new game paths.
/// </summary>
/// <remarks>
/// Only reference sites the weapon governs are rewritten, so a clip it shares with another weapon
/// keeps playing for that weapon. A mapped clip also reachable from a site no weapon governs makes
/// the result incomplete - the weapon still plays the original through that path - and the command
/// exits non-zero so a build cannot ship past it. See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveRepointCommand : CliCommand<MoveRepointCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.bin>")]
        [Description("The MOVE graph to retarget.")]
        public string Input { get; init; } = null!;

        [CommandArgument(1, "<out.bin>")]
        [Description("Where to write the retargeted graph.")]
        public string Output { get; init; } = null!;

        [CommandOption("-w|--weapon <N>")]
        [Description("The EquippedWeapon index whose branches to retarget.")]
        public int Weapon { get; init; }

        [CommandOption("-m|--map <pairs.tsv>")]
        [Description("Tab-separated old and new game paths, one pair per line. "
                     + "Paths are hashed, so the new target need not exist yet.")]
        public string Map { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        MoveFile file = MoveCodec.Load(CliIO.ReadInput(settings.Input));
        CliIO.GuardNotOverwritingInput(settings.Input, settings.Output);

        Dictionary<uint, uint> map = [];
        int line = 0;
        foreach (string row in File.ReadLines(settings.Map))
        {
            line++;
            if (row.Length == 0 || row.StartsWith('#'))
            {
                continue;
            }

            string[] parts = row.Split('\t');
            if (parts.Length < 2)
            {
                AnsiConsole.MarkupLine($"[red]![/] {settings.Map.EscapeMarkup()}:{line} is not a pair");
                return 1;
            }

            map[NameHash.Compute(parts[0].Trim())] = NameHash.Compute(parts[1].Trim());
        }

        MoveRepointResult result = MoveRepoint.Apply(file, settings.Weapon, map);
        NameDatabase names = BundledAssets.LoadNames();

        AnsiConsole.MarkupLine(
            $"[grey]{settings.Input.EscapeMarkup()}[/]: {map.Count} mapped clips, weapon {settings.Weapon}");
        AnsiConsole.MarkupLine($"  {result.Rewritten,5}  references rewritten");
        AnsiConsole.MarkupLine($"  {result.OtherWeapon,5}  left alone, another weapon governs");
        AnsiConsole.MarkupLine(
            $"  {result.Ungoverned,5}  left alone, [yellow]no weapon governs[/]");

        foreach (uint hash in result.Unreferenced)
        {
            string path = names.TryResolve(hash, out string resolved) ? resolved : "<unresolved>";
            AnsiConsole.MarkupLine($"  [yellow]![/] mapped but never referenced: {path.EscapeMarkup()}");
        }

        CliIO.WriteOutput(settings.Output, MoveCodec.Save(file));
        CliIO.ReportWrote(settings.Output);

        if (result.IsComplete)
        {
            return result.Unreferenced.Count == 0 ? 0 : 1;
        }

        AnsiConsole.MarkupLine(
            $"[red]![/] incomplete: {result.Ungoverned} references sit under no weapon, so weapon "
            + $"{settings.Weapon} still reaches the original clips through them. Retargeting those "
            + "would change every weapon that shares them - clone the states instead.");
        return 1;
    }
}
