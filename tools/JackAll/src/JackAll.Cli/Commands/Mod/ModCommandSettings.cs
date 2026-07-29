using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Shared options for every <c>mod</c> command that acts on an installed game.
/// </summary>
/// <remarks>
/// <c>--game</c> is an explicit required option rather than something discovered: the CLI is driven
/// by a mod manager that already knows where the game is (JackAll.App has its own <c>config.ini</c>
/// for the same job), and silently guessing the wrong install is the one mistake here that damages
/// something.
/// </remarks>
public class GameCommandSettings : CommandSettings, IJsonOutputSettings
{
    [CommandOption("-g|--game <dir>")]
    [Description("The Far Cry 2 install folder - the one containing bin\\FarCry2.exe and Data_Win32\\.")]
    public string Game { get; init; } = string.Empty;

    [CommandOption("--json")]
    [Description("Emit one JSON object on stdout instead of human-readable output; progress goes to stderr.")]
    public bool Json { get; init; }

    public override ValidationResult Validate()
        => string.IsNullOrWhiteSpace(Game)
            ? ValidationResult.Error("--game is required: point it at the Far Cry 2 install folder.")
            : ValidationResult.Success();

    /// <summary>Opens and validates the install, turning <see cref="GameInstall.TryOpen"/>'s reason
    /// into the one-line message <see cref="CliCommand{TSettings}"/>'s guard reports.</summary>
    public GameInstall OpenInstall()
        => GameInstall.TryOpen(Game, out string error) ?? throw new InvalidOperationException(error);
}
