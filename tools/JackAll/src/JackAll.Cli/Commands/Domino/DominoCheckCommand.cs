using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Domino;

/// <summary>
/// Checks Domino user graphs - one file, or every graph under a folder - for anything that would break in
/// game or that JackAll's reading of them gets wrong. Each graph's `.debug.lua` twin, when it sits next to
/// it, is what the reconstruction is proved against.
/// </summary>
public sealed class DominoCheckCommand : CliCommand<DominoCheckCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("A user graph .lua, or a folder to check every graph under.")]
        public string Path { get; init; } = null!;

        [CommandOption("-r|--root <dir>")]
        [Description("The folder holding domino\\, for reading node types (default: found above <path>).")]
        public string? Root { get; init; }

        [CommandOption("-s|--summary")]
        [Description("Print only the totals, not each graph's findings.")]
        public bool Summary { get; init; }

        [CommandOption("--json")]
        [Description("Write one JSON object to stdout.")]
        public bool Json { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        List<string> files = File.Exists(settings.Path)
            ? [settings.Path]
            : [.. Directory.EnumerateFiles(settings.Path, "*.lua", SearchOption.AllDirectories)
                .Where(f => !DominoDebugTwin.IsTwinPath(f))
                .Order(StringComparer.OrdinalIgnoreCase)];

        string? root = settings.Root ?? DominoRootAbove(settings.Path);
        var catalog = root is null
            ? null
            : new DominoNodeCatalog(vfsPath => System.IO.Path.Combine(root, vfsPath) is var full && File.Exists(full) ? File.ReadAllText(full) : null);

        var results = new List<(string File, DominoCheckResult Result)>();
        foreach (string file in files)
        {
            string twinPath = DominoDebugTwin.TwinPathFor(file);
            string? twin = File.Exists(twinPath) ? File.ReadAllText(twinPath) : null;
            results.Add((file, DominoCheck.Run(File.ReadAllText(file), twin, catalog)));
        }

        var totals = Totals(results);
        int errors = results.Sum(r => r.Result.Findings.Count(f => f.Severity == LintSeverity.Error));

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                graphs = results.Count,
                errors,
                totals.Rules,
                totals.Twin,
                totals.Identity,
                findings = settings.Summary ? null : results
                    .Where(r => r.Result.Findings.Count > 0)
                    .Select(r => new
                    {
                        path = System.IO.Path.GetRelativePath(settings.Path, r.File),
                        findings = r.Result.Findings.Select(f => new { severity = f.Severity.ToString(), f.Rule, f.Message, f.Function, f.Position }),
                    }),
            });
            return errors > 0 ? 1 : 0;
        }

        if (!settings.Summary)
        {
            foreach ((string file, DominoCheckResult result) in results)
            {
                var shown = result.Findings.Where(f => f.Severity != LintSeverity.Info).ToList();
                if (shown.Count == 0)
                {
                    continue;
                }
                AnsiConsole.MarkupLine($"[blue]{file.EscapeMarkup()}[/]");
                foreach (DominoFinding f in shown)
                {
                    string colour = f.Severity == LintSeverity.Error ? "red" : "yellow";
                    string where = f.Function is null ? "" : $"{f.Function}: ";
                    AnsiConsole.MarkupLine($"  [{colour}]{f.Rule}[/] {where.EscapeMarkup()}{f.Message.EscapeMarkup()}");
                }
            }
        }

        var table = new Table().AddColumns("Rule", "Severity", "Findings");
        foreach (var rule in totals.Rules)
        {
            table.AddRow(rule.Key.EscapeMarkup(), rule.Value.Severity, rule.Value.Count.ToString("N0"));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"{results.Count:N0} graph(s); twin: {totals.Twin.Clean:N0}/{totals.Twin.Graphs:N0} clean, " +
            $"{totals.Twin.Matched:N0}/{totals.Twin.TracedFires:N0} traced fires matched ({totals.Twin.NamedFromTwin:N0} named by the twin)");
        AnsiConsole.MarkupLine("box IDs: " + string.Join(", ", totals.Identity.Select(i => $"{i.Key} {i.Value:N0}")));
        AnsiConsole.MarkupLine(errors > 0 ? $"[red]{errors:N0} error(s)[/]" : "[green]No errors[/]");
        return errors > 0 ? 1 : 0;
    }

    private sealed record TwinTotals(int Graphs, int Clean, int TracedFires, int Matched, int NamedFromTwin);

    private sealed record RuleTotal(string Severity, int Count);

    private static (SortedDictionary<string, RuleTotal> Rules, TwinTotals Twin, SortedDictionary<string, int> Identity)
        Totals(List<(string File, DominoCheckResult Result)> results)
    {
        var rules = new SortedDictionary<string, RuleTotal>(StringComparer.Ordinal);
        foreach (var byRule in results.SelectMany(r => r.Result.Findings).GroupBy(f => f.Rule))
        {
            rules[byRule.Key] = new RuleTotal(byRule.Max(f => f.Severity).ToString(), byRule.Count());
        }

        var twins = results.Select(r => r.Result.Graph?.Twin).OfType<TwinValidation>().ToList();
        var twin = new TwinTotals(twins.Count, twins.Count(t => t.IsClean), twins.Sum(t => t.TracedFires),
            twins.Sum(t => t.Matched), twins.Sum(t => t.NamedFromTwin));

        var identity = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (GraphNode node in results.SelectMany(r => r.Result.Graph?.Nodes ?? []))
        {
            identity[node.IdSource.ToString()] = identity.GetValueOrDefault(node.IdSource.ToString()) + 1;
        }
        return (rules, twin, identity);
    }

    /// <summary>The folder above <paramref name="path"/> that holds `domino\`, where node types are read from.</summary>
    private static string? DominoRootAbove(string path)
    {
        for (DirectoryInfo? dir = new(System.IO.Path.GetFullPath(path)); dir is not null; dir = dir.Parent)
        {
            if (string.Equals(dir.Name, "domino", StringComparison.OrdinalIgnoreCase))
            {
                return dir.Parent?.FullName;
            }
        }
        return null;
    }
}
