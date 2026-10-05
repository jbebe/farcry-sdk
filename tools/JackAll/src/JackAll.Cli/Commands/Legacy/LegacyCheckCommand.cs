using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Legacy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Legacy;

/// <summary>
/// Holds a mod's database to its analysis: every change claimed by exactly one feature, every
/// published claim covered, every page well-formed. Exits 2 while anything is left.
/// </summary>
public sealed class LegacyCheckCommand : CliCommand<LegacyCheckCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandOption("-m|--mod <dir>")]
        [Description("The mod's database folder (legacy/<mod>).")]
        public string Mod { get; init; } = string.Empty;

        [CommandOption("-w|--work <dir>")]
        [Description("The analysis work folder.")]
        public string Work { get; init; } = string.Empty;

        [CommandOption("--features")]
        [Description("Also list how many changes each feature claims.")]
        public bool Features { get; init; }

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout.")]
        public bool Json { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Mod) || string.IsNullOrWhiteSpace(Work)
                ? ValidationResult.Error("--mod and --work are both required.")
                : ValidationResult.Success();
    }

    private const int Shown = 25;

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        LegacyCatalog catalog = LegacyCatalog.Load(settings.Mod);
        List<LegacyChange> changes = LegacyChange.ReadAll(Path.Combine(settings.Work, LegacyAnalyzer.ChangesFile));
        LegacyCheck check = catalog.Check(changes, LegacyAnalyzer.ReadAnalysis(settings.Work).Sha256);
        int exit = check.Clean ? 0 : 2;

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                clean = check.Clean,
                changes = check.Changes,
                unclaimed = check.Unclaimed.Count,
                contested = check.Contested.Select(c => new { address = c.Change.Address, c.Features }),
                check.UnclaimedClaims,
                check.Problems,
                perFeature = check.PerFeature,
            });
            return exit;
        }

        AnsiConsole.MarkupLine(
            $"{catalog.Mod.Name.EscapeMarkup()}: {catalog.Features.Count} page(s), {check.Changes:N0} change(s), "
            + $"{check.Unclaimed.Count:N0} unclaimed, {check.Contested.Count:N0} contested, "
            + $"{check.UnclaimedClaims.Count} published claim(s) uncovered, {check.Problems.Count} problem(s).");

        if (settings.Features)
        {
            foreach (LegacyFeature feature in catalog.Features)
            {
                AnsiConsole.MarkupLine($"  {check.PerFeature[feature.Id],7:N0}  {feature.Id.EscapeMarkup()} [grey]({feature.Kind}{(feature.Status is { } s ? ", " + s : string.Empty)})[/]");
            }
        }

        foreach (string problem in check.Problems)
        {
            AnsiConsole.MarkupLine($"[red]problem[/]   {problem.EscapeMarkup()}");
        }

        foreach (string claim in check.UnclaimedClaims)
        {
            AnsiConsole.MarkupLine($"[yellow]uncovered[/] {claim.EscapeMarkup()}");
        }

        foreach ((LegacyChange change, IReadOnlyList<string> owners) in check.Contested.Take(Shown))
        {
            AnsiConsole.MarkupLine($"[yellow]contested[/] {change.Address.EscapeMarkup()} [grey]by {string.Join(", ", owners).EscapeMarkup()}[/]");
        }

        foreach (LegacyChange change in check.Unclaimed.Take(Shown))
        {
            AnsiConsole.MarkupLine($"[yellow]unclaimed[/] {change.Address.EscapeMarkup()}");
        }

        if (check.Unclaimed.Count > Shown)
        {
            AnsiConsole.MarkupLine($"[grey]... see them all with: legacy changes --work <dir> --mod <dir> --unclaimed --group[/]");
        }

        if (check.Clean)
        {
            AnsiConsole.MarkupLine("[green]Clean[/]: every change and every published claim is accounted for.");
        }

        return exit;
    }
}
