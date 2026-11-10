using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Tools.Fixtures;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fixtures;

/// <summary>Options shared by the <c>fixtures</c> commands: the fixtures folder and the Steam install.</summary>
public class FixturesSettings : CommandSettings
{
    [CommandArgument(0, "<folder>")]
    [Description("The fixtures folder holding manifest.tsv: src\\JackAll.Tests\\Fixtures.")]
    public string Folder { get; init; } = null!;

    [CommandOption("-g|--game <dir>")]
    [Description("A Steam install of Far Cry 2 1.03, the one containing bin\\FarCry2.exe and Data_Win32\\.")]
    public string Game { get; init; } = string.Empty;

    public override ValidationResult Validate()
        => string.IsNullOrWhiteSpace(Game)
            ? ValidationResult.Error("--game is required: point it at a Steam install of Far Cry 2.")
            : ValidationResult.Success();

    public GameInstall OpenInstall()
        => GameInstall.TryOpen(Game, out string error) ?? throw new InvalidOperationException(error);
}

public sealed class FixturesRestoreSettings : FixturesSettings
{
    [CommandOption("--mods <dir>")]
    [Description("The folder holding the Nexus downloads the manifest names.")]
    public string Mods { get; init; } = string.Empty;

    public override ValidationResult Validate()
        => string.IsNullOrWhiteSpace(Mods)
            ? ValidationResult.Error("--mods is required: the folder holding the Nexus downloads the manifest names.")
            : base.Validate();
}

internal static class FixturesOutput
{
    /// <summary>Prints the report; the exit code fails when a fixture is left unaccounted for.</summary>
    public static int Print(FixtureReport report, string written)
    {
        foreach (string problem in report.Problems)
        {
            AnsiConsole.MarkupLine($"[red]Missing:[/] {problem.EscapeMarkup()}");
        }
        AnsiConsole.MarkupLine($"[green]{written}[/] {report.Written:N0} fixture(s), {report.Unchanged:N0} unchanged"
            + (report.Problems.Count > 0 ? $", [red]{report.Problems.Count:N0} missing[/]" : ""));
        return report.Problems.Count > 0 ? 1 : 0;
    }
}

/// <summary>Restores the test fixtures the manifest lists from a Steam install and the Nexus downloads.</summary>
public sealed class FixturesRestoreCommand : CliCommand<FixturesRestoreSettings>
{
    protected override int Run(FixturesRestoreSettings settings, CancellationToken cancellationToken)
        => FixturesOutput.Print(FixtureManifest.Restore(settings.Folder, settings.OpenInstall(), settings.Mods), "Restored");
}

/// <summary>Writes the manifest's line for each new or changed fixture file in the folder.</summary>
public sealed class FixturesRecordCommand : CliCommand<FixturesSettings>
{
    protected override int Run(FixturesSettings settings, CancellationToken cancellationToken)
        => FixturesOutput.Print(FixtureManifest.Record(settings.Folder, settings.OpenInstall()), "Recorded");
}
