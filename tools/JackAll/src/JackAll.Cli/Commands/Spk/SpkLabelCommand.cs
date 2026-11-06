using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Vfs;
using JackAll.Tools.SoundLabels;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Labels every retail `.spk` by where its sounds are used and writes the list the App reads
/// (<c>assets/fc2.soundlabels.tsv</c>). Hand labels win over found ones: those already in the output
/// are kept, and <c>--hand</c> adds more.
/// </summary>
public sealed class SpkLabelCommand : CliCommand<SpkLabelCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandOption("-o|--out <file>")]
        [Description("Where to write the label list (default: fc2.soundlabels.tsv in the current folder).")]
        public string Output { get; init; } = "fc2.soundlabels.tsv";

        [CommandOption("--hand <file>")]
        [Description("A label list written by hand (same columns); its rows replace found labels and are kept on later runs.")]
        public string? Hand { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        using GameVfs vfs = settings.OpenVfs([]);
        IReadOnlyList<SoundLabel> found = SoundLabeler.FromGame(
            vfs, BundledAssets.LoadFcbClasses(), new SyncProgress(JsonOutput.Report));
        SoundLabel[] hand =
        [
            .. SoundLabelTable.Load(settings.Output).Labels.Where(l => l.Source == SoundLabeler.FromHand),
            .. settings.Hand is null ? [] : SoundLabelTable.Load(settings.Hand).Labels.Select(l => l with { Source = SoundLabeler.FromHand }),
        ];
        var table = new SoundLabelTable([.. found, .. hand]);
        File.WriteAllLines(settings.Output, table.Write());

        IReadOnlyList<SoundLabel> labels = [.. table.Labels];
        string[] strays = [.. hand.Select(l => l.Path).Distinct()
            .Where(p => !vfs.Files.ContainsKey(NameHash.Compute(p)) || !p.EndsWith(".spk", StringComparison.OrdinalIgnoreCase))];
        foreach (string stray in strays)
        {
            AnsiConsole.MarkupLine($"[yellow]Hand label for a file the game does not have:[/] {stray.EscapeMarkup()}");
        }

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
