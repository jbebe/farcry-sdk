using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.App.Domino;

/// <summary>
/// One open Domino graph viewer tab. Read-only: there is no write path, so this parses
/// <see cref="SourceText"/> once at open time and never mutates it. <see cref="ParseError"/> is set
/// instead of throwing so a file that doesn't fit the recognized statement shapes (or isn't a `user\`
/// graph at all) still opens, just without a graph.
///
/// Two things beyond the graph itself get pulled in through <paramref name="readByPath"/>, both
/// optional and both silently skipped when unavailable: the node type scripts each box refers to (for
/// pin signatures) and the `*.debug.lua` twin (for the editor's original box and pin names).
/// </summary>
public sealed class DominoTabViewModel
{
    public string Title { get; }
    public string SourceText { get; }
    public ReconstructedGraph? Graph { get; }
    public DominoGraphViewModel? Canvas { get; }
    public DominoDebugTwin? Twin { get; }
    public string? ParseError { get; }

    /// <param name="gamePath">The graph's own game-relative path, used to find its debug twin. Null
    /// when the file didn't come from the VFS.</param>
    /// <param name="readByPath">Reads any game-relative path's bytes as text, or returns null.</param>
    public DominoTabViewModel(string title, string sourceText, string? gamePath = null, Func<string, string?>? readByPath = null)
    {
        Title = title;
        SourceText = sourceText;

        try
        {
            UserGraph userGraph = UserGraphParser.Parse(DominoLuaSource.Parse(sourceText));

            DominoNodeCatalog? catalog = readByPath is null ? null : new DominoNodeCatalog(readByPath);
            Twin = LoadTwin(gamePath, readByPath);

            Graph = GraphBuilder.Build(userGraph, catalog, Twin);
            Canvas = new DominoGraphViewModel(Graph, SugiyamaLayout.Layout(Graph), Twin);
        }
        catch (Exception ex)
        {
            Graph = null;
            Canvas = null;
            ParseError = ex.Message;
        }
    }

    /// <summary>Loads the graph's `*.debug.lua` sibling. Absent, unreadable or unparseable is normal -
    /// a debug twin is a bonus, never a requirement - so every failure here is swallowed.</summary>
    private static DominoDebugTwin? LoadTwin(string? gamePath, Func<string, string?>? readByPath)
    {
        if (gamePath is null || readByPath is null || DominoDebugTwin.IsTwinPath(gamePath))
        {
            return null;
        }

        try
        {
            string? twinSource = readByPath(DominoDebugTwin.TwinPathFor(gamePath));
            return twinSource is null ? null : DominoDebugTwin.FromGraph(UserGraphParser.Parse(DominoLuaSource.Parse(twinSource)));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The one-line summary shown above the canvas.</summary>
    public string StatusText
    {
        get
        {
            if (ParseError is not null)
            {
                return $"Couldn't build a graph from this file: {ParseError} — the source is still shown on the right.";
            }
            if (Graph is null || Canvas is null || Canvas.Nodes.Count == 0)
            {
                return @"No reconstructable box graph here (a system\ node body, or an empty user\ graph).";
            }

            int boxes = Graph.Nodes.Count;
            string twin = Twin is null ? "no debug twin" : $"{Twin.Connections.Count} traced connections";
            string ambiguous = Canvas.AmbiguousDataWireCount > 0 ? $", {Canvas.AmbiguousDataWireCount} ambiguous" : "";
            string chips = Canvas.ChipCount > 0 ? $" (+{Canvas.ChipCount} via variable chips)" : "";

            return $"{boxes} boxes · {Canvas.ControlWireCount} control wires · {Canvas.DataWireCount} data wires{ambiguous}{chips} · "
                 + $"{Canvas.UnwiredPinCount} unwired, {Canvas.DeadEndPinCount} dead-end pins · {twin}";
        }
    }
}
