using System.ComponentModel;
using Domino.Core;
using Domino.Core.Graphs;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Domino.Cli.Commands;

/// <summary>
/// Batch-reconstructs every `user\*.lua` mission graph with <see cref="GraphBuilder"/> and reports
/// aggregate node/edge stats - the Phase 2 checkpoint, mirroring <see cref="ClassifyGraphsCommand"/> for
/// the underlying statement classifier.
/// </summary>
public sealed class BuildGraphsCommand : Command<BuildGraphsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<dir>")]
        [Description("Directory to recursively scan for .lua files (e.g. the extracted domino\\user corpus).")]
        public string Dir { get; init; } = null!;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(settings.Dir, "*.lua", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int filesOk = 0;
        long totalNodes = 0, persistentNodes = 0, pooledNodes = 0, subGraphNodes = 0, totalEdges = 0;
        var edgeCounts = new Dictionary<EdgeTarget, long>();
        var failures = new List<(string File, string Message)>();

        foreach (var file in files)
        {
            try
            {
                var root = DominoLuaSource.Parse(File.ReadAllText(file));
                var userGraph = UserGraphParser.Parse(root);
                var graph = GraphBuilder.Build(userGraph);
                filesOk++;

                totalNodes += graph.Nodes.Count;
                persistentNodes += graph.Nodes.Count(n => n.Kind == BoxInstanceKind.Persistent);
                pooledNodes += graph.Nodes.Count(n => n.Kind == BoxInstanceKind.Pooled);
                subGraphNodes += graph.Nodes.Count(n => n.IsSubGraph);
                totalEdges += graph.Edges.Count;
                foreach (var edge in graph.Edges)
                {
                    edgeCounts[edge.Target] = edgeCounts.GetValueOrDefault(edge.Target) + 1;
                }
            }
            catch (Exception ex)
            {
                failures.Add((file, ex.Message));
            }
        }

        AnsiConsole.MarkupLine($"Built [green]{filesOk}[/]/{files.Count} graphs.");
        AnsiConsole.MarkupLine($"{totalNodes} nodes ({persistentNodes} persistent, {pooledNodes} pooled, {subGraphNodes} sub-graph), {totalEdges} edges.");
        foreach (var (target, count) in edgeCounts.OrderByDescending(kv => kv.Value))
        {
            AnsiConsole.MarkupLine($"  {count,7}  {target} ({100.0 * count / totalEdges:F2}%)");
        }
        foreach (var (file, message) in failures)
        {
            AnsiConsole.MarkupLine($"[red]FAIL[/] {file.EscapeMarkup()}: {message.EscapeMarkup()}");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
