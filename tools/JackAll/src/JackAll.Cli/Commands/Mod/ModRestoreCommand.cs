using JackAll.Cli.Infrastructure;
using JackAll.Core;
using Spectre.Console;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Puts the pristine patch.dat/patch.fat back, undoing every build. The counterpart of a mod
/// manager's "purge" — after this the install is stock again, with the backup left in place so the
/// next build is still a pure function of the mod list.
/// </summary>
public sealed class ModRestoreCommand : CliCommand<GameCommandSettings>
{
    protected override int Run(GameCommandSettings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();

        // GameInstall.RestoreVanilla throws for this too, but its message is written for someone
        // looking at a UI that already told them a backup exists - here the caller may never have
        // built at all, and "nothing to undo" is the useful thing to say.
        if (!install.HasVanillaBackup)
        {
            throw new InvalidOperationException(
                "There is no patch.dat.vanilla backup to restore from - this install has never been built by " +
                "JackAll, so there is nothing to undo.");
        }

        Core.Mods.PluginSyncResult plugins = install.RestoreVanilla();

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                restored = true,
                patchFat = install.PatchFat,
                patchDat = install.PatchDat,
                pluginsRemoved = plugins.Removed,
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Restored[/] the original patch.dat/patch.fat in {install.DataDir.EscapeMarkup()}");
        if (plugins.Removed > 0)
        {
            AnsiConsole.MarkupLine($"Removed {plugins.Removed:N0} deployed plugin file(s) from bin\\plugins");
        }
        return 0;
    }
}
