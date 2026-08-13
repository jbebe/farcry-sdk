using JackAll.Cli.Infrastructure;
using JackAll.Core;
using Spectre.Console;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Reports whether a folder is a usable Far Cry 2 install and what state its patch archive is in.
/// </summary>
/// <remarks>
/// This is the command a mod manager runs first, and the only one that reports an unusable install
/// as *data* rather than as a failure: "that folder isn't Far Cry 2" is a normal answer to ask for,
/// and a caller needs the reason string to show the user, not an exit code.
///
/// <c>looksModded</c> with no <c>hasVanillaBackup</c> is the dangerous combination and the reason
/// this command exists — see <see cref="ModBuildCommand"/>.
/// </remarks>
public sealed class ModStatusCommand : CliCommand<GameCommandSettings>
{
    protected override int Run(GameCommandSettings settings, CancellationToken cancellationToken)
    {
        GameInstall? install = GameInstall.TryOpen(settings.Game, out string error);

        if (install is null)
        {
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    ok = true,
                    gamePath = Path.GetFullPath(settings.Game),
                    valid = false,
                    error,
                });
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Not a Far Cry 2 install:[/] {error.EscapeMarkup()}");
            }
            return 0;
        }

        bool hasBackup = install.HasVanillaBackup;
        bool looksModded = install.LooksModded();
        int patchEntries = install.TryCountPatchEntries();

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                gamePath = install.RootPath,
                valid = true,
                dataDir = install.DataDir,
                patchFat = install.PatchFat,
                patchDat = install.PatchDat,
                hasVanillaBackup = hasBackup,
                looksModded,
                patchEntries,
                // The one state a caller must refuse to build from without an explicit override.
                needsVanillaConfirmation = !hasBackup && looksModded,
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Far Cry 2[/] at {install.RootPath.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  patch.fat entries : {patchEntries:N0}");
        AnsiConsole.MarkupLine($"  vanilla backup    : {(hasBackup ? "[green]present[/]" : "[yellow]not created yet[/]")}");
        if (!hasBackup && looksModded)
        {
            AnsiConsole.MarkupLine(
                "  [yellow]The current patch.dat already looks modded.[/] Restore it (verify the game files) " +
                "before building, or the next build will treat someone else's mod as the base game.");
        }
        return 0;
    }
}
