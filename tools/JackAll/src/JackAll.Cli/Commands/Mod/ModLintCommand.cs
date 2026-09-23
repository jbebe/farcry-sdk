using System.ComponentModel;
using System.Text;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Reports archetype edits that will not take effect: a layer edits an entity library, and a library
/// loading after it declares the same archetype, so the game reads that copy instead. Also runs
/// <c>domino check</c> on every Domino user graph a layer ships.
/// </summary>
/// <remarks>
/// This is the failure the entity library's own override rule makes easy and invisible - the file
/// really did change, so nothing looks wrong until someone reports the mod doing nothing. See
/// docs/docs/engine-internals/entity-instancing.md.
/// </remarks>
public sealed class ModLintCommand : CliCommand<ModLintCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandOption("-l|--layer <dir>")]
        [Description("A mod layer to check, lowest priority first. Repeatable.")]
        public string[] Layers { get; init; } = [];
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = GameInstall.TryOpen(settings.Game, out string error)
            ?? throw new InvalidOperationException(error);

        List<IModLayer> layers = [.. settings.Layers.Select(ModPipeline.OpenLayer)];
        var progress = new SyncProgress(JsonOutput.Report);
        NameDatabase names = BundledAssets.LoadNames();

        // Resolved through the merged filesystem with the layers applied, so a layer that adds an
        // archetype to a later library is itself taken into account. Fragment *rows* are skipped -
        // decoding every .fcb container in the game would dwarf the lint itself, and a layer's own
        // fragment overrides are spliced from the layer, not from those rows.
        using GameVfs vfs = GameVfs.Load(
            install, names, GameCache.Load(install.CacheFile), BundledAssets.LoadFcbClasses(), progress,
            includeFragments: false);
        if (layers.Count > 0)
        {
            vfs.Rebuild(layers, includeFragments: false, progress: progress);
        }

        IReadOnlyList<DeadEdit> dead = ArchetypeLint.Run(
            ArchetypeLint.StagedFragmentsOf(layers),
            vfs.Files.Values.Where(f => f.NameIsKnown).Select(f => f.Path),
            vfs.ReadByPath, progress);

        return Report(settings, layers, dead, LintDominoGraphs(layers, vfs));
    }

    private sealed record GraphFindings(string Layer, string Path, IReadOnlyList<DominoFinding> Findings);

    /// <summary>Checks every Domino user graph a layer ships, reporting only what it adds over the retail
    /// graph it replaces.</summary>
    private static List<GraphFindings> LintDominoGraphs(List<IModLayer> layers, GameVfs vfs)
    {
        var catalog = new DominoNodeCatalog(path => Text(vfs.ReadByPath(path)));
        var results = new List<GraphFindings>();
        foreach (IModLayer layer in layers)
        {
            foreach (uint hash in layer.Hashes)
            {
                if (layer.PathOf(hash) is not { } path
                    || !path.StartsWith(@"domino\user\", StringComparison.OrdinalIgnoreCase)
                    || !path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                    || DominoDebugTwin.IsTwinPath(path))
                {
                    continue;
                }

                string? twin = Text(vfs.ReadByPath(DominoDebugTwin.TwinPathFor(path)));
                DominoCheckResult edited = DominoCheck.Run(Text(layer.Read(hash))!, twin, catalog);
                DominoCheckResult? original = Text(vfs.ReadOriginal(hash)) is { } retail
                    ? DominoCheck.Run(retail, twin, catalog)
                    : null;
                var added = DominoCheck.NewSince(edited, original).Where(f => f.Severity != LintSeverity.Info).ToList();
                if (added.Count > 0)
                {
                    results.Add(new GraphFindings(layer.Name, path, added));
                }
            }
        }
        return results;

        static string? Text(byte[]? bytes) => bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private int Report(Settings settings, List<IModLayer> layers, IReadOnlyList<DeadEdit> dead, List<GraphFindings> domino)
    {
        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                layers = layers.Select(l => l.Name),
                deadEdits = dead.Select(d => new
                {
                    layer = d.Source,
                    archetype = d.Archetype,
                    edited = d.EditedPath,
                    d.FragmentId,
                    winner = d.WinningPath,
                }),
                domino = domino.Select(g => new
                {
                    layer = g.Layer,
                    path = g.Path,
                    findings = g.Findings.Select(f => new { severity = f.Severity.ToString(), f.Rule, f.Message, f.Function }),
                }),
            });
            return 0;
        }

        foreach (GraphFindings graph in domino)
        {
            AnsiConsole.MarkupLine($"[blue]{graph.Layer.EscapeMarkup()}[/] {graph.Path.EscapeMarkup()}");
            foreach (DominoFinding f in graph.Findings)
            {
                string colour = f.Severity == LintSeverity.Error ? "red" : "yellow";
                string where = f.Function is null ? "" : $"{f.Function}: ";
                AnsiConsole.MarkupLine($"  [{colour}]{f.Rule}[/] {where.EscapeMarkup()}{f.Message.EscapeMarkup()}");
            }
        }

        if (dead.Count == 0)
        {
            AnsiConsole.MarkupLine(
                layers.Count == 0
                    ? "[yellow]No layers given[/] - pass --layer to check one."
                    : "[green]No dead archetype edits[/] - every edited archetype is the copy the game reads.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]{dead.Count:N0} archetype edit(s) will not take effect[/]");
        foreach (IGrouping<string, DeadEdit> byLayer in dead.GroupBy(d => d.Source))
        {
            AnsiConsole.MarkupLine($"  [blue]{byLayer.Key.EscapeMarkup()}[/]");
            foreach (DeadEdit edit in byLayer.OrderBy(d => d.Archetype, StringComparer.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"    {edit.Archetype.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"      edited in : {edit.EditedPath.EscapeMarkup()} ({edit.FragmentId.EscapeMarkup()})");
                AnsiConsole.MarkupLine($"      [green]game reads[/] : {edit.WinningPath.EscapeMarkup()}");
            }
        }
        AnsiConsole.MarkupLine("Move the edit into the winning library, or drop the archetype from the later one.");
        return 0;
    }
}
