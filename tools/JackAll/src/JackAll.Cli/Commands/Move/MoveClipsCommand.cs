using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Naming;
using JackAll.Core.Format.Move;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Lists the animation clips an EquippedWeapon index plays, or a census of every index.
/// </summary>
/// <remarks>
/// A clip several weapons play is called out, because repointing one of those breaks the others -
/// and folder names do not tell you which is which. The Dart Rifle's own folder holds a draw clip
/// the MGL-140 also plays, and it borrows the AK-47's jam cycle from the AK's folder.
/// See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveClipsCommand : CliCommand<MoveClipsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.bin>")]
        [Description("The MOVE graph to read.")]
        public string Input { get; init; } = null!;

        [CommandOption("-w|--weapon <N>")]
        [Description("An EquippedWeapon index. Omit for a census of every index.")]
        public int? Weapon { get; init; }

        [CommandOption("--shared-only")]
        [Description("List only the clips another weapon also plays.")]
        public bool SharedOnly { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        MoveFile file = MoveCodec.Load(CliIO.ReadInput(settings.Input));
        NameDatabase names = BundledAssets.LoadNames();

        if (settings.Weapon is not { } weapon)
        {
            Census(file);
            return 0;
        }

        IReadOnlyList<MoveClip> clips = MoveWeapons.ClipsFor(file, weapon);
        if (clips.Count == 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]EquippedWeapon == {weapon} scopes nothing in this graph.[/]");
            return 0;
        }

        int shared = clips.Count(c => !c.IsExclusive);
        AnsiConsole.MarkupLine(
            $"[grey]{settings.Input.EscapeMarkup()}[/]: weapon {weapon} plays {clips.Count} clips "
            + $"({clips.Count - shared} exclusive, {shared} shared)");

        foreach (MoveClip clip in clips)
        {
            if (settings.SharedOnly && clip.IsExclusive)
            {
                continue;
            }

            string path = names.TryResolve(clip.Hash, out string resolved) ? resolved : "<unresolved>";
            string note = clip.IsExclusive
                ? string.Empty
                : $"  [red]shared with {string.Join(", ", clip.PlayedBy.Where(w => w != weapon))}[/]";
            AnsiConsole.MarkupLine(
                $"  {clip.Hash:X8}  {clip.References}x  {path.EscapeMarkup()}{note}");
        }

        if (shared > 0 && !settings.SharedOnly)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]{shared} clips are played by another weapon too - repointing those "
                + "changes how that weapon animates.[/]");
        }

        return 0;
    }

    private static void Census(MoveFile file)
    {
        IReadOnlyDictionary<int, IReadOnlySet<uint>> byWeapon = MoveWeapons.ClipsByWeapon(file);
        AnsiConsole.MarkupLine(
            $"[grey]{byWeapon.Count} EquippedWeapon indices scope clips in this graph[/]");
        foreach ((int weapon, IReadOnlySet<uint> clips) in byWeapon.OrderBy(p => p.Key))
        {
            AnsiConsole.MarkupLine($"  {weapon,4}  {clips.Count,4} clips");
        }
    }
}
