using System.ComponentModel;
using System.Text.RegularExpressions;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Legacy;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Legacy;

/// <summary>
/// Queries an analysis's changes: by address glob, by the feature that claims them, or the ones no
/// feature claims yet - listed, or grouped into the shapes rules are written against.
/// </summary>
public sealed partial class LegacyChangesCommand : CliCommand<LegacyChangesCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandOption("-w|--work <dir>")]
        [Description("The analysis work folder.")]
        public string Work { get; init; } = string.Empty;

        [CommandOption("-m|--mod <dir>")]
        [Description("The mod's database folder (legacy/<mod>), needed for --feature and --unclaimed.")]
        public string? Mod { get; init; }

        [CommandOption("--match <glob>")]
        [Description("Only changes whose address matches. Repeatable; any match counts.")]
        public string[] Match { get; init; } = [];

        [CommandOption("--feature <id>")]
        [Description("Only the changes this feature's rules claim.")]
        public string? Feature { get; init; }

        [CommandOption("--unclaimed")]
        [Description("Only the changes no feature claims.")]
        public bool Unclaimed { get; init; }

        [CommandOption("--group")]
        [Description("Count changes by address shape instead of listing them.")]
        public bool Group { get; init; }

        [CommandOption("--limit <n>")]
        [Description("Rows to print (default 60 as text, all as JSON; 0 for all).")]
        public int? Limit { get; init; }

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout.")]
        public bool Json { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Work) ? ValidationResult.Error("--work is required: the analysis folder.")
                : (Feature is not null || Unclaimed) && Mod is null ? ValidationResult.Error("--feature and --unclaimed need --mod.")
                : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        IEnumerable<LegacyChange> changes = LegacyChange.ReadAll(Path.Combine(settings.Work, LegacyAnalyzer.ChangesFile));

        if (settings.Match.Length > 0)
        {
            Regex[] globs = [.. settings.Match.Select(AddressGlob.Compile)];
            changes = changes.Where(c => globs.Any(g => g.IsMatch(c.Address)));
        }

        if (settings.Mod is { } modDir)
        {
            LegacyCatalog catalog = LegacyCatalog.Load(modDir);
            if (settings.Feature is { } id)
            {
                LegacyFeature feature = catalog.Find(id) ?? throw new KeyNotFoundException($"No feature '{id}'.");
                changes = changes.Where(c => feature.Matches(c.Address));
            }

            if (settings.Unclaimed)
            {
                changes = catalog.Attribute(changes).Where(a => a.Owners.Count == 0).Select(a => a.Change);
            }
        }

        List<LegacyChange> selected = [.. changes];
        int limit = settings.Limit switch
        {
            0 => int.MaxValue,
            { } rows => rows,
            null => settings.Json ? int.MaxValue : 60,
        };

        if (settings.Group)
        {
            var groups = selected.GroupBy(c => Shape(c.Address))
                .Select(g => new { shape = g.Key, count = g.Count(), kinds = string.Join(",", g.Select(c => c.Kind).Distinct()), sample = g.First() })
                .OrderByDescending(g => g.count)
                .ToList();
            if (settings.Json)
            {
                JsonOutput.Write(new { ok = true, total = selected.Count, groups = groups.Take(limit) });
                return 0;
            }

            foreach (var group in groups.Take(limit))
            {
                AnsiConsole.MarkupLine($"{group.count,7:N0}  {group.shape.EscapeMarkup()}  [grey]{group.kinds}  e.g. {Describe(group.sample).EscapeMarkup()}[/]");
            }

            AnsiConsole.MarkupLine($"[grey]{selected.Count:N0} change(s) in {groups.Count:N0} shape(s).[/]");
            return 0;
        }

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, total = selected.Count, changes = selected.Take(limit).Select(c => new { id = c.Id, change = c }) });
            return 0;
        }

        foreach (LegacyChange change in selected.Take(limit))
        {
            AnsiConsole.MarkupLine($"[grey]{change.Id}[/] {change.Kind,-6} {change.Address.EscapeMarkup()}  {Describe(change).EscapeMarkup()}");
        }

        AnsiConsole.MarkupLine($"[grey]{selected.Count:N0} change(s){(selected.Count > limit ? $", first {limit} shown" : string.Empty)}.[/]");
        return 0;
    }

    /// <summary>
    /// An address with what varies between siblings blurred out: list indices, line numbers, a
    /// fragment's own name under its first folder, and long digit runs such as sector numbers.
    /// </summary>
    private static string Shape(string address)
    {
        int split = address.IndexOfAny(['#', '@']);
        string unit = split < 0 ? address : address[..split];
        string rest = split < 0 ? string.Empty : address[split..];

        if (ContainerFormats.ContainerPathOf(unit.Replace('/', '\\')) is { } container)
        {
            string[] inside = unit[(container.Length + 1)..].Split('/');
            unit = container.Replace('\\', '/') + (inside.Length > 1 ? $"/{inside[0]}/**" : "/*");
        }

        unit = unit.StartsWith("_hash/", StringComparison.Ordinal) ? "_hash/*" + Path.GetExtension(unit) : LongDigits().Replace(unit, "*");
        rest = Index().Replace(rest, "[*]");
        rest = Line().Replace(rest, "@L*");
        return unit + rest;
    }

    private static string Describe(LegacyChange change)
    {
        string text = change.Kind switch
        {
            ChangeKind.Field or ChangeKind.Bytes or ChangeKind.File or ChangeKind.Loose => $"{Clip(change.Old)} -> {Clip(change.New)}",
            ChangeKind.Add or ChangeKind.New => $"+ {Clip(change.New)}",
            ChangeKind.Remove => $"- {Clip(change.Old)}",
            ChangeKind.Text => $"-{Clip(change.Old)} +{Clip(change.New)}",
            _ => string.Empty,
        };
        text = change.Hint is { } hint ? $"{text}  [{hint}]" : text;
        return change.ShadowedBy is { } winner ? $"{text}  (dead: the game reads {winner})" : text;
    }

    private static string Clip(string? text)
    {
        if (text is null)
        {
            return "(none)";
        }

        string flat = Whitespace().Replace(text, " ").Trim();
        return flat.Length > 90 ? flat[..87] + "..." : flat;
    }

    [GeneratedRegex(@"\d{3,}")]
    private static partial Regex LongDigits();

    [GeneratedRegex(@"\[\+?\d+\]")]
    private static partial Regex Index();

    [GeneratedRegex(@"@L\d+")]
    private static partial Regex Line();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
