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

    private sealed record RuleTotal(string Severity, int Count);

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

        // Only the findings and the counts outlive each graph; the graphs themselves hold their whole
        // syntax trees.
        var findings = new List<(string File, IReadOnlyList<DominoFinding> Findings)>();
        var twins = new List<TwinValidation>();
        var identity = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string twinPath = DominoDebugTwin.TwinPathFor(file);
            string? twin = File.Exists(twinPath) ? File.ReadAllText(twinPath) : null;
            DominoCheckResult result = DominoCheck.Run(File.ReadAllText(file), twin, catalog);

            findings.Add((file, result.Findings));
            if (result.Graph?.Twin is { } validation)
            {
                twins.Add(validation);
            }
            foreach (GraphNode node in result.Graph?.Nodes ?? [])
            {
                identity[node.IdSource.ToString()] = identity.GetValueOrDefault(node.IdSource.ToString()) + 1;
            }
        }

        var rules = new SortedDictionary<string, RuleTotal>(StringComparer.Ordinal);
        foreach (var byRule in findings.SelectMany(r => r.Findings).GroupBy(f => f.Rule))
        {
            rules[byRule.Key] = new RuleTotal(byRule.First().Severity.ToString(), byRule.Count());
        }
        var twinTotals = new
        {
            graphs = twins.Count,
            clean = twins.Count(t => t.IsClean),
            tracedFires = twins.Sum(t => t.TracedFires),
            matched = twins.Sum(t => t.Matched),
            namedFromTwin = twins.Sum(t => t.NamedFromTwin),
        };
        int errors = findings.Sum(r => r.Findings.Count(f => f.Severity == LintSeverity.Error));

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                graphs = files.Count,
                errors,
                rules,
                twin = twinTotals,
                identity,
                findings = settings.Summary ? null : findings
                    .Where(r => r.Findings.Count > 0)
                    .Select(r => new
                    {
                        path = System.IO.Path.GetRelativePath(settings.Path, r.File),
                        findings = r.Findings.Select(DominoFindingOutput.Json),
                    }),
            });
            return errors > 0 ? 1 : 0;
        }

        if (!settings.Summary)
        {
            foreach ((string file, IReadOnlyList<DominoFinding> graphFindings) in findings)
            {
                var shown = graphFindings.Where(f => f.Severity != LintSeverity.Info).ToList();
                if (shown.Count > 0)
                {
                    DominoFindingOutput.Print(file, shown);
                }
            }
        }

        var table = new Table().AddColumns("Rule", "Severity", "Findings");
        foreach (var rule in rules)
        {
            table.AddRow(rule.Key.EscapeMarkup(), rule.Value.Severity, rule.Value.Count.ToString("N0"));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"{files.Count:N0} graph(s); twin: {twinTotals.clean:N0}/{twinTotals.graphs:N0} clean, " +
            $"{twinTotals.matched:N0}/{twinTotals.tracedFires:N0} traced fires matched ({twinTotals.namedFromTwin:N0} named by the twin)");
        AnsiConsole.MarkupLine("box IDs: " + string.Join(", ", identity.Select(i => $"{i.Key} {i.Value:N0}")));
        AnsiConsole.MarkupLine(errors > 0 ? $"[red]{errors:N0} error(s)[/]" : "[green]No errors[/]");
        return errors > 0 ? 1 : 0;
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
