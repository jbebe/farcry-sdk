using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Vfs;
using JackAll.Tools.SoundLabels;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Labels every retail `.spk` by where its sounds are used and writes the list the App reads
/// (<c>assets/fc2.soundlabels.tsv</c>).
/// </summary>
public sealed class SpkLabelCommand : CliCommand<SpkLabelCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandOption("-o|--out <file>")]
        [Description("Where to write the label list (default: fc2.soundlabels.tsv in the current folder).")]
        public string Output { get; init; } = "fc2.soundlabels.tsv";
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        using GameVfs vfs = settings.OpenVfs([]);
        IReadOnlyList<SoundLabel> labels = SoundLabeler.FromGame(
            vfs, BundledAssets.LoadFcbClasses(), new SyncProgress(JsonOutput.Report));
        var table = new SoundLabelTable(labels);
        File.WriteAllLines(settings.Output, table.Write());

        int banks = vfs.Files.Values.Count(f => !f.IsSynthetic && f.Type.Extension == "spk");
        var byCategory = labels.GroupBy(l => l.Category).OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var bySource = labels.GroupBy(l => l.Source).ToDictionary(g => g.Key, g => g.Count());

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, path = Path.GetFullPath(settings.Output), banks, labelled = labels.Count, byCategory, bySource });
            return 0;
        }

        AnsiConsole.MarkupLine($"Labelled [bold]{labels.Count:N0}[/] of {banks:N0} .spk files → {Path.GetFullPath(settings.Output).EscapeMarkup()}");
        AnsiConsole.MarkupLine("  " + string.Join("  ", bySource.Select(kv => $"{kv.Key} {kv.Value:N0}")).EscapeMarkup());
        foreach ((string category, int count) in byCategory)
        {
            AnsiConsole.MarkupLine($"  {count,6:N0}  {category.EscapeMarkup()}");
        }
        return 0;
    }
}
