using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Compiles the vanilla patch archive plus the given mod layers into the game's live
/// patch.dat/patch.fat — the headless form of JackAll.App's Build button.
/// </summary>
/// <remarks>
/// <c>--layer</c> order is the whole interface: it maps one-for-one onto
/// <see cref="PatchBuilder.Build"/>'s layer list, where later wins. Nothing here reorders,
/// deduplicates or second-guesses it, because the caller — a mod manager with a load-order UI — is
/// the only thing that knows what the user actually asked for.
///
/// The build itself is a pure function of (vanilla backup, layers, order): it always regenerates
/// from <c>patch.dat.vanilla</c>, never from what is currently on disk. Running this twice with the
/// same arguments produces the same bytes, and dropping a <c>--layer</c> genuinely removes that mod.
/// </remarks>
public sealed class ModBuildCommand : CliCommand<ModBuildCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandOption("-l|--layer <path>")]
        [Description("A mod folder or .zip to apply. Repeatable, and order matters - later layers win.")]
        public string[] Layers { get; init; } = [];

        [CommandOption("--force")]
        [Description("Build even though the current patch.dat looks modded and no vanilla backup exists yet.")]
        public bool Force { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();
        GuardVanillaBackup(install, settings.Force);

        List<IModLayer> layers = [.. settings.Layers.Select(ModPipeline.OpenLayer)];
        BuildResult result = ModPipeline.Build(install, layers, new SyncProgress(JsonOutput.Report));

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                patchFat = install.PatchFat,
                patchDat = install.PatchDat,
                result.TotalEntries,
                result.VanillaEntries,
                result.OverriddenEntries,
                result.AddedEntries,
                result.OutputBytes,
                layers = layers.Select((layer, index) => new
                {
                    index,
                    path = settings.Layers[index],
                    layer.Name,
                    wholeFileOverrides = layer.Hashes.Count,
                    fragmentOverrides = layer.FragmentOverrides.Sum(kv => kv.Value.Count),
                }),
                conflicts = result.Conflicts.Select(c => new
                {
                    container = c.Container,
                    fragmentId = c.FragmentId,
                    isNewEntry = c.IsNewEntry,
                    winningLayer = c.WinningLayer,
                    earlierLayers = c.EarlierLayers,
                }),
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"[green]Built[/] {install.PatchDat.EscapeMarkup()} - {result.TotalEntries:N0} entries "
            + $"({result.OverriddenEntries:N0} overridden, {result.AddedEntries:N0} added, "
            + $"{result.OutputBytes / 1024.0 / 1024.0:N1} MB)");

        foreach (FragmentConflict conflict in result.Conflicts)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Warning:[/] '{conflict.WinningLayer.EscapeMarkup()}' overrode "
                + $"'{string.Join(", ", conflict.EarlierLayers).EscapeMarkup()}' inside "
                + $"'{conflict.DisplayPath.EscapeMarkup()}' by load order - their edits genuinely "
                + "conflicted, so only the higher-priority mod's change survived. Verify this in-game "
                + "or hand-resolve it in JackAll.App.");
        }
        return 0;
    }

    /// <summary>
    /// Refuses to snapshot a patch archive that already carries somebody's mod as this install's
    /// "vanilla" (see <see cref="GameInstall.BackupWouldCaptureMods"/>). A headless run has nobody
    /// to ask the way JackAll.App's confirmation dialog does, so it refuses and lets the caller
    /// decide with <c>--force</c>.
    /// </summary>
    private static void GuardVanillaBackup(GameInstall install, bool force)
    {
        if (force || !install.BackupWouldCaptureMods)
        {
            return;
        }

        throw new InvalidOperationException(
            "The current patch.dat already looks like it contains mods, and there is no patch.dat.vanilla " +
            "backup yet - building now would bake that mod in as this install's base game. Restore the " +
            "original patch.dat/patch.fat (verify the game files in Steam) and try again, or pass --force " +
            "if you are certain the current patch is stock.");
    }
}
