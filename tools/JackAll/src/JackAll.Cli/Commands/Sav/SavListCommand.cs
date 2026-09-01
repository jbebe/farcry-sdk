using JackAll.Cli.Infrastructure;
using JackAll.Tools.Sav;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Sav;

/// <summary>Lists the player's saves. A save that won't parse is counted rather than reported.</summary>
public sealed class SavListCommand : CliCommand<SavListCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandOption("--folder <path>")]
        [Description("Where to look (default: the game's own Saved Games folder).")]
        public string? Folder { get; init; }

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout instead of human-readable output.")]
        public bool Json { get; init; }
    }

    private sealed record SaveListing(
        string FileName, string World, string Player, uint PersistedObjects, DateTime Modified);

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string folder = settings.Folder ?? SaveGameLocator.SavedGamesFolder;
        var saves = new List<SaveListing>();
        int unreadable = 0;

        foreach (string path in SaveGameLocator.EnumerateSaveFiles(folder))
        {
            try
            {
                SaveGameInfo info = SaveGameDocument.Read(path);
                saves.Add(new SaveListing(
                    Path.GetFileName(path), info.WorldName, info.PlayerName,
                    info.PersistedObjectCount, File.GetLastWriteTime(path)));
            }
            catch
            {
                unreadable++;
            }
        }
        saves.Sort((a, b) => b.Modified.CompareTo(a.Modified));

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, folder, saves, unreadable });
            return 0;
        }

        if (saves.Count == 0)
        {
            AnsiConsole.MarkupLine($"No saves found in {folder.EscapeMarkup()}");
            return 0;
        }

        var table = new Table().AddColumns("File", "World", "Player", "Persisted objects", "Modified");
        foreach (SaveListing save in saves)
        {
            table.AddRow(
                save.FileName.EscapeMarkup(), save.World.EscapeMarkup(), save.Player.EscapeMarkup(),
                $"{save.PersistedObjects:N0}", $"{save.Modified:yyyy-MM-dd HH:mm}");
        }
        AnsiConsole.Write(table);

        if (unreadable > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{unreadable}[/] file(s) couldn't be read.");
        }
        return 0;
    }
}
