using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino;

/// <summary>
/// Checks a reconstructed user graph for what would break or silently misbehave in game. The
/// reconstruction's own findings come first; the rules here need node signatures, so the graph should be
/// built with a <see cref="DominoNodeCatalog"/>. An error-level rule has no hits anywhere in the retail
/// scripts - see docs/docs/engine-internals/domino-scripts.md for each rule's retail count.
/// </summary>
public static class DominoLint
{
    public static IReadOnlyList<DominoFinding> Run(ReconstructedGraph graph)
    {
        var findings = new List<DominoFinding>(graph.Findings);
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var registered = graph.RegisteredDependencies.ToHashSet(StringComparer.OrdinalIgnoreCase);

        void Add(LintSeverity severity, string rule, string message, GraphNode? node = null) =>
            findings.Add(new DominoFinding(severity, rule, message)
            {
                NodeId = node?.Id,
                Position = node?.SourcePositions.FirstOrDefault(),
            });

        foreach (GraphNode node in graph.Nodes)
        {
            if (!registered.Contains(node.NodeTypePath))
            {
                Add(LintSeverity.Error, "unregistered-box", $"{node.NodeTypePath} is used but not registered in Create", node);
            }

            NodeSignature? signature = node.Signature;
            if (signature is null)
            {
                continue;
            }
            if (node.Kind == BoxInstanceKind.Pooled && signature.Origin == SignatureOrigin.Declared && !signature.Stateless)
            {
                Add(LintSeverity.Error, "stateful-pooled", $"{signature.Title} keeps state but runs on a shared pooled slot", node);
            }
            if (signature.Origin != SignatureOrigin.Declared)
            {
                continue;
            }

            var dataIns = signature.DataIns.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
            foreach ((string param, ExpressionSyntax value) in node.Params)
            {
                if (!dataIns.TryGetValue(param, out string? type))
                {
                    Add(LintSeverity.Warning, "undeclared-param", $"{param} is not one of {signature.Title}'s data-ins", node);
                }
                else if (!LiteralFits(value, type))
                {
                    Add(LintSeverity.Warning, "literal-type", $"{param} is {type} but is set to {value}", node);
                }
            }
        }

        foreach (GraphEdge edge in graph.Edges)
        {
            GraphNode? source = edge.SourceNodeId is null ? null : nodes[edge.SourceNodeId];
            if (source?.Signature is { } sourceSignature && !sourceSignature.ControlOuts.Any(p => p.Name == edge.SourcePin))
            {
                Add(LintSeverity.Warning, "undeclared-out", $"{edge.SourcePin} is not a control-out of {sourceSignature.Title}, so it never fires", source);
            }
            if (source is not null && edge.Index is { } outSlot && !SlotInRange(source, edge.SourcePin, outSlot))
            {
                Add(LintSeverity.Error, "dynamic-index-range", $"{edge.SourcePin}[{outSlot}] is beyond the slots _DynamicAnchors gives it", source);
            }

            if (edge.TargetNodeId is null || nodes[edge.TargetNodeId] is not { } target)
            {
                continue;
            }
            if (target.Signature is { } targetSignature && !targetSignature.ControlIns.Any(p => p.Name == edge.TargetPin))
            {
                Add(LintSeverity.Error, "undeclared-in", $"{edge.TargetPin} is not a control-in of {targetSignature.Title}; firing it calls nil", target);
            }
            if (edge.TargetIndex is { } inSlot && !SlotInRange(target, edge.TargetPin!, inSlot))
            {
                Add(LintSeverity.Error, "dynamic-index-range", $"{edge.TargetPin}({inSlot}) is beyond the slots _DynamicAnchors gives it", target);
            }
        }

        StaleSlots(graph, Add);
        UnusedSlots(graph, Add);

        var fired = graph.Edges.Where(e => e.Target == EdgeTarget.GraphExit).Select(e => e.GraphExitPin).ToHashSet(StringComparer.Ordinal);
        foreach (GraphFunction anchor in graph.OutAnchors.Where(a => !fired.Contains(a.Name)))
        {
            Add(LintSeverity.Info, "unfired-out-anchor", $"control-out {anchor.Name} is never fired");
        }

        if (graph.Twin is { IsClean: false } twin)
        {
            Add(LintSeverity.Warning, "stale-twin", $"the debug twin no longer matches: {twin.Problems[0]}");
        }
        return findings;
    }

    private static bool SlotInRange(GraphNode node, string pin, int slot) =>
        node.DynamicSlots.TryGetValue(pin, out int count) && slot >= 0 && slot < count;

    /// <summary>A pooled slot keeps whatever the last box of its type set, anywhere in the game, so a pooled
    /// box has to set every data-in itself - BlackBox writes `nil` for the ones left empty. A sub-graph's
    /// data-ins are only inferred, so there the check is against what other boxes of the type set.</summary>
    private static void StaleSlots(ReconstructedGraph graph, Action<LintSeverity, string, string, GraphNode?> add)
    {
        foreach (var sameType in graph.Nodes.Where(n => n.Kind == BoxInstanceKind.Pooled).GroupBy(n => n.NodeTypePath))
        {
            var everySet = sameType.SelectMany(n => n.Params.Keys).ToHashSet(StringComparer.Ordinal);
            foreach (GraphNode node in sameType)
            {
                IEnumerable<string> expected = node.Signature?.Origin == SignatureOrigin.Declared
                    ? node.Signature.DataIns.Select(p => p.Name)
                    : everySet;
                var missing = expected.Where(p => !node.Params.ContainsKey(p)).Order().ToList();
                if (missing.Count > 0)
                {
                    add(LintSeverity.Warning, "stale-slot",
                        $"{string.Join(", ", missing)} is not set, so it keeps whatever the shared slot last held", node);
                }
            }
        }
    }

    private static void UnusedSlots(ReconstructedGraph graph, Action<LintSeverity, string, string, GraphNode?> add)
    {
        var firedSlots = graph.Edges
            .Where(e => e.TargetNodeId is not null && e.TargetIndex is not null)
            .ToLookup(e => (e.TargetNodeId!, e.TargetPin!), e => e.TargetIndex!.Value);
        foreach (GraphNode node in graph.Nodes)
        {
            foreach ((string pin, int count) in node.DynamicSlots)
            {
                if (node.Signature?.ControlIns.Any(p => p.Name == pin && p.Dynamic) != true)
                {
                    continue;
                }
                var unused = Enumerable.Range(0, count).Except(firedSlots[(node.Id, pin)]).ToList();
                if (unused.Count > 0)
                {
                    add(LintSeverity.Warning, "unused-slot", $"{pin} slot {string.Join(", ", unused)} is never fired", node);
                }
            }
        }
    }

    /// <summary>Whether a literal is a value the data-in's type takes; anything computed is not judged.</summary>
    private static bool LiteralFits(ExpressionSyntax value, string type)
    {
        if (value is not LiteralExpressionSyntax literal || literal.Kind() == SyntaxKind.NilLiteralExpression)
        {
            return true;
        }
        SyntaxKind kind = literal.Kind();
        return type switch
        {
            "Core|bool" => kind is SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression
                           || literal.Token.Text is "0" or "1",
            "Core|int" => kind == SyntaxKind.NumericalLiteralExpression && long.TryParse(literal.Token.Text, out _),
            "Core|float" => kind == SyntaxKind.NumericalLiteralExpression,
            "Core|string" or "Nomad|entity" => kind == SyntaxKind.StringLiteralExpression,
            _ => true,
        };
    }
}
