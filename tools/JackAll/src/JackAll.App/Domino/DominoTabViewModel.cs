using Domino.Core.Graphs;
using Domino.Core.Lua;

namespace JackAll.App.Domino;

/// <summary>
/// One open Domino graph viewer tab. Read-only for now - there is no write path yet (Phase 4 of the
/// project plan), so this only ever parses <see cref="SourceText"/> into a <see cref="Graph"/> once at
/// open time. <see cref="ParseError"/> is set instead of throwing so a file that doesn't fit the six
/// recognized statement shapes (or isn't a `user\` graph at all) still opens, just without a graph.
/// </summary>
public sealed class DominoTabViewModel
{
    public string Title { get; }
    public string SourceText { get; }
    public ReconstructedGraph? Graph { get; }
    public IReadOnlyList<PositionedNode> Nodes { get; }
    public string? ParseError { get; }

    public DominoTabViewModel(string title, string sourceText)
    {
        Title = title;
        SourceText = sourceText;

        try
        {
            var chunk = LuaParser.Parse(sourceText);
            var userGraph = UserGraphParser.Parse(chunk);
            Graph = GraphBuilder.Build(userGraph);
            Nodes = DominoGraphLayout.Layout(Graph);
        }
        catch (Exception ex)
        {
            Graph = null;
            Nodes = [];
            ParseError = ex.Message;
        }
    }
}
