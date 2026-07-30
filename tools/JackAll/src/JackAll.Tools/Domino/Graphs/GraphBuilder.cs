using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Graphs;

/// <summary>
/// Rebuilds a <see cref="ReconstructedGraph"/> (boxes, typed pins, connections) from a
/// <see cref="UserGraphParser"/>-classified file.
///
/// A `user\` file's `export:` functions aren't graph nodes themselves - they're the flattened
/// continuation code BlackBox generated for "whatever runs after this pin fires." The real nodes are
/// box instances: one per `self[N]`/`self.box_X` (<see cref="BoxInstanceKind.Persistent"/>, built once
/// up front from every `CreateBoxStmt` since these are referenced by ID from anywhere in the file), and
/// one per distinct configure-and-fire occurrence of `Boxes[PathID(...)]`
/// (<see cref="BoxInstanceKind.Pooled"/> - the runtime slot is reused, but each occurrence is where a
/// human placed a separate box in the original visual editor).
///
/// Edges are resolved by walking every statement the target handler function runs (in order) and
/// collecting each thing it fires: a box's control-in pin (a real edge target), a redirect through a
/// `self._type.otherHandler(self)` call (expanded inline, recursively, then the handler's remaining
/// statements are still considered - a subroutine call is not a dead end for what follows it), or the
/// graph's own exposed pin (`self:PinName()`, relevant when this graph is itself used as a sub-box). A
/// real function in the corpus frequently fires more than one of these in sequence (confirmed empirically
/// - roughly 30% of functions do), so one `WireControlOutStmt` can fan out into several
/// <see cref="GraphEdge"/>s sharing the same source pin; a handler that fires nothing at all resolves to
/// a single <see cref="EdgeTarget.DeadEnd"/> edge instead.
/// </summary>
public static class GraphBuilder
{
    private sealed class NodeBuilder
    {
        public required string Id;
        public required BoxRef Ref;
        public required string NodeTypePath;
        public required BoxInstanceKind Kind;
        public required string OwnerFunction;
        public readonly Dictionary<string, ExpressionSyntax> Params = new();
    }

    private enum EntryEventKind { Node, Redirect, GraphExit }

    private sealed record EntryEvent(EntryEventKind Kind, string? NodeId, string? Pin, string? RedirectTo, string? GraphExitPin);

    private sealed record Terminal(EdgeTarget Kind, string? NodeId, string? Pin, string? GraphExitPin);

    private sealed record PendingEdge(string SourceNodeId, string SourcePin, int? Index, string? TargetHandler);

    public static ReconstructedGraph Build(UserGraph graph)
    {
        var nodesByRef = new Dictionary<BoxRef, NodeBuilder>();
        var allNodes = new List<NodeBuilder>();
        var registeredDeps = new List<string>();
        var loadedResources = new List<(string, string)>();

        // Pass 0: register every persistent box instance up front, so any function can reference one by
        // BoxRef regardless of which function it happens to be manipulated from.
        foreach (var fn in graph.Functions)
        {
            foreach (var stmt in fn.Body)
            {
                if (stmt is CreateBoxStmt create)
                {
                    var nb = new NodeBuilder
                    {
                        Id = MakePersistentId(create.Box),
                        Ref = create.Box,
                        NodeTypePath = create.Path,
                        Kind = BoxInstanceKind.Persistent,
                        OwnerFunction = fn.Name,
                    };
                    nodesByRef[create.Box] = nb;
                    allNodes.Add(nb);
                }
            }
        }

        // Pass 1: per function, build pooled-box occurrence nodes, collect this function's outgoing
        // wiring as pending edges, and record what "firing into this function" resolves to.
        var pendingEdges = new List<PendingEdge>();
        var entryByFunction = new Dictionary<string, List<EntryEvent>>();

        foreach (var fn in graph.Functions)
        {
            var openPooled = new Dictionary<BoxRef, NodeBuilder>();
            int occurrenceSeq = 0;
            var events = new List<EntryEvent>();

            NodeBuilder? ResolveBoxNode(BoxRef boxRef)
            {
                if (nodesByRef.TryGetValue(boxRef, out var existing))
                {
                    return existing;
                }
                if (boxRef is not PooledBoxRef pooled)
                {
                    return null; // an instance ref that was never CreateBox'd - not expected in the real corpus
                }
                if (openPooled.TryGetValue(boxRef, out var open))
                {
                    return open;
                }
                var nb = new NodeBuilder
                {
                    Id = $"o:{fn.Name}#{occurrenceSeq++}",
                    Ref = boxRef,
                    NodeTypePath = pooled.Path,
                    Kind = BoxInstanceKind.Pooled,
                    OwnerFunction = fn.Name,
                };
                openPooled[boxRef] = nb;
                allNodes.Add(nb);
                return nb;
            }

            foreach (var stmt in fn.Body)
            {
                switch (stmt)
                {
                    case RegisterBoxStmt r:
                        registeredDeps.Add(r.Path);
                        break;

                    case LoadResourceStmt l:
                        loadedResources.Add((l.ResourceName, l.ResourceType));
                        break;

                    case SetParamStmt p:
                        if (ResolveBoxNode(p.Box) is { } paramNode)
                        {
                            paramNode.Params[p.ParamName] = p.Value;
                        }
                        break;

                    case SetGraphBackrefStmt g:
                        ResolveBoxNode(g.Box); // touch/open the occurrence only
                        break;

                    case WireControlOutStmt w:
                        if (ResolveBoxNode(w.Box) is { } wireNode)
                        {
                            pendingEdges.Add(new PendingEdge(wireNode.Id, w.PinName, w.Index, w.TargetHandler));
                        }
                        break;

                    case FireControlInStmt f:
                        if (ResolveBoxNode(f.Box) is { } fireNode)
                        {
                            events.Add(new EntryEvent(EntryEventKind.Node, fireNode.Id, f.PinName, null, null));
                            if (f.Box is PooledBoxRef)
                            {
                                // Fired and done - a later configure of the same path in this function
                                // is a fresh occurrence, not a continuation of this one.
                                openPooled.Remove(f.Box);
                            }
                        }
                        break;

                    case CallOwnHandlerStmt own:
                        events.Add(new EntryEvent(EntryEventKind.Redirect, null, null, own.HandlerName, null));
                        break;

                    case FireOwnPinStmt pin:
                        events.Add(new EntryEvent(EntryEventKind.GraphExit, null, null, null, pin.PinName));
                        break;

                    // CreateBoxStmt: handled in pass 0.
                    // RebindSelfToGraphStmt, ReadDataStmt, SetGraphFieldStmt, TraceConnectionStmt,
                    // OtherStmt: no node/edge effect.
                }
            }

            entryByFunction[fn.Name] = events;
        }

        // Pass 2: resolve every pending edge. A target handler can fan out into any number of terminals
        // (fire box A, then box B, then...) - each becomes its own edge sharing the source pin; a
        // handler that resolves to zero terminals is a single DeadEnd edge instead.
        var edges = new List<GraphEdge>();
        foreach (var pending in pendingEdges)
        {
            if (pending.TargetHandler is null)
            {
                edges.Add(new GraphEdge(pending.SourceNodeId, pending.SourcePin, pending.Index, EdgeTarget.Unwired, null, null, null));
                continue;
            }

            var terminals = ResolveTerminals(pending.TargetHandler, entryByFunction, []);
            if (terminals.Count == 0)
            {
                edges.Add(new GraphEdge(pending.SourceNodeId, pending.SourcePin, pending.Index, EdgeTarget.DeadEnd, null, null, null));
                continue;
            }
            foreach (var terminal in terminals)
            {
                edges.Add(new GraphEdge(pending.SourceNodeId, pending.SourcePin, pending.Index, terminal.Kind, terminal.NodeId, terminal.Pin, terminal.GraphExitPin));
            }
        }

        var finalNodes = allNodes
            .Select(nb => new GraphNode(nb.Id, nb.Ref, nb.NodeTypePath, nb.Kind, nb.OwnerFunction, nb.Params))
            .ToList();

        return new ReconstructedGraph(finalNodes, edges, registeredDeps, loadedResources);
    }

    /// <summary>Expands a handler function name into every terminal (box fire / graph exit) it and
    /// anything it redirects through eventually reach, in order. A cyclic redirect chain contributes
    /// nothing further once revisited, rather than recursing forever.</summary>
    private static List<Terminal> ResolveTerminals(string handlerName, Dictionary<string, List<EntryEvent>> table, HashSet<string> visiting)
    {
        if (!table.TryGetValue(handlerName, out var events))
        {
            // Not one of this file's own export: functions - e.g. a named exposed pin with no body of
            // its own. Treat conservatively as reaching the graph's own boundary under that name.
            return [new Terminal(EdgeTarget.GraphExit, null, null, handlerName)];
        }
        if (!visiting.Add(handlerName))
        {
            return []; // cyclic redirect guard - this path contributes no further terminals
        }

        var results = new List<Terminal>();
        foreach (var ev in events)
        {
            switch (ev.Kind)
            {
                case EntryEventKind.Node:
                    results.Add(new Terminal(EdgeTarget.Node, ev.NodeId, ev.Pin, null));
                    break;
                case EntryEventKind.GraphExit:
                    results.Add(new Terminal(EdgeTarget.GraphExit, null, null, ev.GraphExitPin));
                    break;
                case EntryEventKind.Redirect:
                    results.AddRange(ResolveTerminals(ev.RedirectTo!, table, visiting));
                    break;
            }
        }

        visiting.Remove(handlerName);
        return results;
    }

    private static string MakePersistentId(BoxRef box) => box switch
    {
        InstanceBoxRef i => $"p:{i.Slot}",
        NamedInstanceBoxRef n => $"p:{n.FieldName}",
        PooledBoxRef p => $"p:{p.Path}",
        _ => throw new NotSupportedException(box.GetType().Name),
    };
}
