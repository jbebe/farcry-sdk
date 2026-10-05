using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Legacy;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Tools.World;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Legacy;

/// <summary>
/// Lists every change a legacy mod makes to the base game, at the finest grain each format allows,
/// into a work folder that <c>legacy changes</c>, <c>check</c> and <c>pick</c> then read.
/// </summary>
public sealed class LegacyAnalyzeCommand : CliCommand<LegacyAnalyzeCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandOption("-f|--from <path>")]
        [Description("The legacy mod: a .zip, .7z or .rar, or a folder.")]
        public string From { get; init; } = string.Empty;

        [CommandOption("-w|--work <dir>")]
        [Description("The work folder to fill - extracted source, imported layer, changes.jsonl. Rewritten each run.")]
        public string Work { get; init; } = string.Empty;

        public override ValidationResult Validate()
            => base.Validate() is { Successful: false } failed ? failed
                : string.IsNullOrWhiteSpace(From) ? ValidationResult.Error("--from is required: the legacy mod.")
                : string.IsNullOrWhiteSpace(Work) ? ValidationResult.Error("--work is required: where to write the analysis.")
                : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();
        NameDatabase names = BundledAssets.LoadNames();
        FcbClassDefinitions definitions = BundledAssets.LoadFcbClasses();
        var progress = new SyncProgress(JsonOutput.Report);

        JsonOutput.Report("Mounting the game's archives to diff against…");
        using GameVfs vfs = ModPipeline.OpenOriginals(install, names, progress);
        LegacyAnalysis analysis = LegacyAnalyzer.Analyze(settings.From, settings.Work, install, vfs, names, definitions, progress);
        ModPipeline.SaveCache(vfs, install);
        int shadowed = MarkShadowed(settings.Work, install, names, definitions, progress);

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, analysis });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Analyzed[/] {analysis.Source.EscapeMarkup()}");
        if (analysis.Sha256 is { } sha)
        {
            AnsiConsole.MarkupLine($"  sha256   : {sha}");
        }

        foreach (IGrouping<string, LegacyFile> role in analysis.Files.GroupBy(f => f.Role))
        {
            AnsiConsole.MarkupLine($"  {role.Key,-12}: {role.Count():N0} file(s)");
        }

        if (analysis.Import is { } import)
        {
            AnsiConsole.MarkupLine(
                $"  archive  : {import.Imported:N0} file(s) and {import.FragmentsImported:N0} fragment(s) differ; "
                + $"{import.Skipped:N0} match the base game");
            foreach (LegacyImportNote note in import.Refused.Concat(import.WholeFile).Concat(import.Unreachable))
            {
                AnsiConsole.MarkupLine($"  [yellow]note[/]     : {note.ContainerPath.EscapeMarkup()}: {note.Reason.EscapeMarkup()}");
            }
        }

        AnsiConsole.MarkupLine(
            $"  changes  : {analysis.Changes:N0} across {analysis.Units:N0} unit(s), {analysis.WholeUnits:N0} of them only takeable whole");
        if (shadowed > 0)
        {
            AnsiConsole.MarkupLine($"  [yellow]shadowed[/] : {shadowed:N0} change(s) edit an archetype copy a later library overrides, so the game never reads them");
        }

        AnsiConsole.MarkupLine($"Next: [blue]legacy changes --work {settings.Work.EscapeMarkup()} --group[/]");
        return 0;
    }

    /// <summary>Runs the archetype lint over the imported layer and marks every change it finds dead.</summary>
    private static int MarkShadowed(
        string workDir, GameInstall install, NameDatabase names, FcbClassDefinitions definitions, SyncProgress progress)
    {
        JsonOutput.Report("Checking which edited archetypes a later library overrides…");
        var layer = new FolderModLayer(Path.Combine(workDir, LegacyAnalyzer.LayerFolder), "legacy");
        using GameVfs merged = GameVfs.Load(
            install, names, GameCache.Load(install.CacheFile), definitions, progress, includeFragments: false);
        merged.Rebuild([layer], includeFragments: false, progress: progress);

        Dictionary<string, string> winners = ArchetypeLint.Run(
                ArchetypeLint.StagedFragmentsOf([layer]),
                merged.Files.Values.Where(f => f.NameIsKnown).Select(f => f.Path),
                merged.ReadByPath, progress)
            .Where(dead => dead.FragmentId is not null)
            .GroupBy(dead => $"{dead.EditedPath}\\{dead.FragmentId}".Replace('\\', '/').ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().WinningPath.Replace('\\', '/'));
        return LegacyAnalyzer.MarkShadowed(workDir, winners);
    }
}
