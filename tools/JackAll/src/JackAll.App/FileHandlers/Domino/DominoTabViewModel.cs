using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>
/// One open Domino graph viewer tab. Read-only: there is no write path, so this parses
/// <see cref="SourceText"/> once at open time and never mutates it. <see cref="ParseError"/> is set
/// instead of throwing so a file that doesn't fit the recognized statement shapes (or isn't a `user\`
/// graph at all) still opens, just without a graph.
///
/// Two things beyond the graph itself get pulled in through <see cref="Services"/>, both
/// optional and both silently skipped when unavailable: the node type scripts each box refers to (for
/// pin signatures and the lint) and the `*.debug.lua` twin (for the editor's original box and pin names,
/// and to check the reconstruction against).
/// </summary>
public sealed class DominoTabViewModel
{
    public string Title { get; }
    public string SourceText { get; }
    public ReconstructedGraph? Graph { get; }
    public DominoGraphViewModel? Canvas { get; }
    public DominoDebugTwin? Twin { get; }
    public string? ParseError { get; }

    /// <summary>What the lint found wrong with the graph.</summary>
    public IReadOnlyList<DominoFinding> Findings { get; } = [];

    /// <summary>Null when the tab was opened without the rest of JackAll behind it.</summary>
    public DominoServices? Services { get; }

    /// <param name="gamePath">The graph's own game-relative path, used to find its debug twin. Null
    /// when the file didn't come from the VFS.</param>
    public DominoTabViewModel(string title, string sourceText, string? gamePath = null, DominoServices? services = null)
    {
        Title = title;
        SourceText = sourceText;
        Services = services;
        // A twin has no twin of its own.
        string? twinSource = gamePath is null || services is null || DominoDebugTwin.IsTwinPath(gamePath)
            ? null
            : services.ReadText(DominoDebugTwin.TwinPathFor(gamePath));

        try
        {
            DominoCheckResult check = DominoCheck.Run(sourceText, twinSource,
                services is null ? null : new DominoNodeCatalog(services.ReadText));
            Findings = check.Findings;
            Twin = check.Twin;
            Graph = check.Graph;
            if (Graph is null)
            {
                ParseError = Findings[0].Message;
                return;
            }
            Canvas = new DominoGraphViewModel(Graph, SugiyamaLayout.Order(Graph), Twin, Findings);
        }
        catch (Exception ex)
        {
            Graph = null;
            Canvas = null;
            ParseError = ex.Message;
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
            string twin = Graph.Twin switch
            {
                null => "no debug twin",
                { IsClean: true } t => $"twin: all {t.Matched} fires match",
                { } t => $"twin: {t.Problems.Count} disagreements",
            };
            string ambiguous = Canvas.AmbiguousDataWireCount > 0 ? $", {Canvas.AmbiguousDataWireCount} ambiguous" : "";
            string chips = Canvas.ChipCount > 0 ? $" (+{Canvas.ChipCount} via variable chips)" : "";
            int errors = Findings.Count(f => f.Severity == LintSeverity.Error);
            int warnings = Findings.Count(f => f.Severity == LintSeverity.Warning);
            string problems = errors + warnings == 0 ? "no problems" : $"{errors} errors, {warnings} warnings";

            return $"{boxes} boxes · {Canvas.ControlWireCount} control wires · {Canvas.DataWireCount} data wires{ambiguous}{chips} · "
                 + $"{Canvas.UnwiredPinCount} unwired, {Canvas.DeadEndPinCount} dead-end pins · {twin} · {problems}";
        }
    }
}
