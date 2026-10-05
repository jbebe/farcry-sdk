using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Legacy;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Legacy;

/// <summary>
/// Builds a layer holding only the picked features of an analyzed legacy mod, with their bundles
/// expanded and their requirements pulled in. What a layer cannot carry is listed for porting.
/// </summary>
public sealed class LegacyPickCommand : CliCommand<LegacyPickCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandOption("-m|--mod <dir>")]
        [Description("The mod's database folder (legacy/<mod>).")]
        public string Mod { get; init; } = string.Empty;

        [CommandOption("-w|--work <dir>")]
        [Description("The analysis work folder.")]
        public string Work { get; init; } = string.Empty;

        [CommandOption("--feature <id>")]
        [Description("A feature or bundle to take. Repeatable.")]
        public string[] Feature { get; init; } = [];

        [CommandOption("-o|--out <dir>")]
        [Description("The layer folder to write. Must not exist yet.")]
        public string Out { get; init; } = string.Empty;

        public override ValidationResult Validate()
            => base.Validate() is { Successful: false } failed ? failed
                : string.IsNullOrWhiteSpace(Mod) || string.IsNullOrWhiteSpace(Work) || string.IsNullOrWhiteSpace(Out)
                    ? ValidationResult.Error("--mod, --work and --out are all required.")
                : Feature.Length == 0 ? ValidationResult.Error("Name at least one --feature.")
                : Directory.Exists(Out) ? ValidationResult.Error($"'{Out}' exists already; a pick writes a fresh layer.")
                : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        LegacyCatalog catalog = LegacyCatalog.Load(settings.Mod);
        List<LegacyChange> changes = LegacyChange.ReadAll(Path.Combine(settings.Work, LegacyAnalyzer.ChangesFile));
        LegacyCheck check = catalog.Check(changes, LegacyAnalyzer.ReadAnalysis(settings.Work).Sha256);
        List<LegacyFeature> features = catalog.Expand(settings.Feature);

        var picked = new HashSet<string>(
            changes.Where(c => features.Any(f => f.Matches(c.Address))).Select(c => c.Address), StringComparer.Ordinal);

        GameInstall install = settings.OpenInstall();
        NameDatabase names = BundledAssets.LoadNames();
        FcbClassDefinitions definitions = BundledAssets.LoadFcbClasses();
        using GameVfs vfs = ModPipeline.OpenOriginals(install, names, new SyncProgress(JsonOutput.Report));
        var units = new LegacyUnits(Path.Combine(settings.Work, LegacyAnalyzer.LayerFolder), vfs, names, definitions);
        LegacyPick pick = LegacyPicker.Pick(units, changes, picked, settings.Out);
        ModPipeline.SaveCache(vfs, install);

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                features = features.Select(f => f.Id),
                changes = picked.Count,
                pick.Copied,
                pick.Merged,
                pick.TakenWhole,
                outside = pick.Outside,
                databaseClean = check.Clean,
            });
            return 0;
        }

        if (!check.Clean)
        {
            AnsiConsole.MarkupLine("[yellow]The database does not check clean[/] - run legacy check; a contested change goes to every feature claiming it.");
        }

        AnsiConsole.MarkupLine($"[green]Picked[/] {string.Join(", ", features.Select(f => f.Id)).EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  {picked.Count:N0} change(s): {pick.Copied:N0} unit(s) copied, {pick.Merged:N0} rebuilt from the base game, into {settings.Out.EscapeMarkup()}");
        foreach (string unit in pick.TakenWhole)
        {
            AnsiConsole.MarkupLine($"  [yellow]whole[/]   {unit.EscapeMarkup()} [grey]carries changes no picked feature claims[/]");
        }

        foreach (LegacyChange change in pick.Outside)
        {
            string what = change.Kind == ChangeKind.Bytes ? $"{change.Old} -> {change.New}" : change.Hint ?? string.Empty;
            AnsiConsole.MarkupLine($"  [yellow]by hand[/] {change.Address.EscapeMarkup()} [grey]{what.EscapeMarkup()}[/]");
        }

        return 0;
    }
}
