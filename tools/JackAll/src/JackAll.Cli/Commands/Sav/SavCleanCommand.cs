using JackAll.Cli.Infrastructure;
using JackAll.Tools.Sav;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Sav;

/// <summary>Writes a copy of a save with its persisted entities dropped. The source is never modified.</summary>
public sealed class SavCleanCommand : CliCommand<SavCleanCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandArgument(0, "<file.sav>")]
        [Description("The save to copy and clean.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.sav>")]
        [Description("Where to write the cleaned save (default: a new game-style name beside the input).")]
        public string? Out { get; init; }

        [CommandOption("--dry-run")]
        [Description("Report what would be removed and write nothing.")]
        public bool DryRun { get; init; }

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout instead of human-readable output.")]
        public bool Json { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        SaveGameInfo info = SaveGameDocument.Read(settings.Input);

        if (settings.DryRun)
        {
            PurgeReport preview = SaveGameCleaner.PurgePersistedEntities(SaveGameDocument.ReadFcbRoot(info));
            Report(settings, info, preview, destPath: null);
            return 0;
        }

        (string destPath, PurgeReport report) = SaveGameCleaner.PurgeToNewSave(info, settings.Out);
        Report(settings, info, report, destPath);
        return 0;
    }

    private static void Report(Settings settings, SaveGameInfo info, PurgeReport report, string? destPath)
    {
        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                source = Path.GetFullPath(info.FilePath),
                output = destPath is null ? null : Path.GetFullPath(destPath),
                dryRun = settings.DryRun,
                world = info.WorldName,
                player = info.PlayerName,
                report.DatabasesEmptied,
                report.RecordsRemoved,
                report.ObjectsRemoved,
            });
            return;
        }

        AnsiConsole.MarkupLine(
            $"[green]{info.PlayerName.EscapeMarkup()}[/] in {info.WorldName.EscapeMarkup()} — " +
            $"{report.RecordsRemoved:N0} persisted record(s), {report.ObjectsRemoved:N0} object(s), " +
            $"across {report.DatabasesEmptied} database(s)");

        if (destPath is null)
        {
            AnsiConsole.MarkupLine("[yellow]Dry run[/] — nothing written.");
            return;
        }

        CliIO.ReportWrote(destPath);
        AnsiConsole.MarkupLine(
            "The world resets in the copy: cleared outposts repopulate and dropped items are gone. " +
            "Mission progress, buddies, tapes and diamonds carry over.");
    }
}
