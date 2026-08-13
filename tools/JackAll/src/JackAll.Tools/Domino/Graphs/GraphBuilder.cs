using JackAll.Tools.Domino.Nodes;
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

    /// <summary>Mutable state the build passes accumulate into. <see cref="StatementOrder"/> is the
    /// current statement's position in whole-file order, stamped on every data event so
    /// <see cref="DataFlowResolver"/> can sequence produces against consumes across functions.</summary>
    private sealed class BuildState
    {
        public readonly Dictionary<BoxRef, NodeBuilder> NodesByRef = new();
        public readonly List<NodeBuilder> AllNodes = new();
        public readonly List<string> RegisteredDeps = new();
        public readonly List<(string, string)> LoadedResources = new();
        public readonly List<DataEvent> DataEvents = new();
        public readonly List<PendingEdge> PendingEdges = new();
        public int StatementOrder;
    }

    /// <param name="catalog">Resolves each box's node type to its pin interface. Optional: without one
    /// the graph still reconstructs, the nodes just carry no <see cref="GraphNode.Signature"/>.</param>
    /// <param name="twin">The graph's parsed `*.debug.lua`, when available - supplies each persistent
    /// box's original editor name.</param>
    public static ReconstructedGraph Build(UserGraph graph, DominoNodeCatalog? catalog = null, DominoDebugTwin? twin = null)
    {
        var state = new BuildState();
        RegisterPersistentBoxes(graph, state);

        var entryByFunction = new Dictionary<string, List<EntryEvent>>();
        foreach (var fn in graph.Functions)
        {
            entryByFunction[fn.Name] = new FunctionWalker(state, fn).Walk();
        }

        var edges = ResolveControlEdges(state.PendingEdges, entryByFunction);
        return Assemble(state, edges, catalog, twin);
    }

    /// <summary>Registers every persistent box instance up front, so any function can reference one by
    /// BoxRef regardless of which function it happens to be manipulated from.</summary>
    private static void RegisterPersistentBoxes(UserGraph graph, BuildState state)
    {
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
                    state.NodesByRef[create.Box] = nb;
                    state.AllNodes.Add(nb);
                }
            }
        }
    }

    /// <summary>Walks one function's statements in order: builds pooled-box occurrence nodes, collects
    /// the function's outgoing wiring as pending edges, and returns what "firing into this function"
    /// resolves to.</summary>
    private sealed class FunctionWalker(BuildState state, UserGraphFunction fn)
    {
        private readonly Dictionary<BoxRef, NodeBuilder> _openPooled = new();
        private int _occurrenceSeq;

        public List<EntryEvent> Walk()
        {
            var events = new List<EntryEvent>();
            foreach (var stmt in fn.Body)
            {
                state.StatementOrder++;
                switch (stmt)
                {
                    case RegisterBoxStmt r:
                        state.RegisteredDeps.Add(r.Path);
                        break;

                    case LoadResourceStmt l:
                        state.LoadedResources.Add((l.ResourceName, l.ResourceType));
                        break;

                    case SetParamStmt p:
                        if (ResolveBoxNode(p.Box) is { } paramNode)
                        {
                            paramNode.Params[p.ParamName] = p.Value;
                            RecordParamDataEvent(paramNode, p);
                        }
                        break;

                    // `self.Var = self[N].Pin;` - a box's data-out feeding a graph variable, which is
                    // how nearly all data reaches its consumer (see DataFlowResolver).
                    case ReadDataStmt read when DominoNodeCatalog.GraphFieldName(read.Target) is { } variable:
                        if (ResolveBoxNode(read.Box) is { } producerNode)
                        {
                            state.DataEvents.Add(new DataEvent(
                                DataEventKind.Produce, producerNode.Id, read.PinName, variable,
                                null, null, fn.Name, state.StatementOrder)
                            {
                                NodeTypePath = producerNode.NodeTypePath,
                            });
                        }
                        break;

                    case SetGraphBackrefStmt g:
                        // Touch/open the occurrence only.
                        ResolveBoxNode(g.Box);
                        break;

                    case WireControlOutStmt w:
                        if (ResolveBoxNode(w.Box) is { } wireNode)
                        {
                            state.PendingEdges.Add(new PendingEdge(wireNode.Id, w.PinName, w.Index, w.TargetHandler));
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
                                _openPooled.Remove(f.Box);
                            }
                        }
                        break;

                    case CallOwnHandlerStmt own:
                        events.Add(new EntryEvent(EntryEventKind.Redirect, null, null, own.HandlerName, null));
                        break;

                    case FireOwnPinStmt pin:
                        events.Add(new EntryEvent(EntryEventKind.GraphExit, null, null, null, pin.PinName));
                        break;

                    // CreateBoxStmt: handled by RegisterPersistentBoxes.
                    // RebindSelfToGraphStmt, ReadDataStmt, SetGraphFieldStmt, TraceConnectionStmt,
                    // OtherStmt: no node/edge effect.
                }
            }

            return events;
        }

        private NodeBuilder? ResolveBoxNode(BoxRef boxRef)
        {
            if (state.NodesByRef.TryGetValue(boxRef, out var existing))
            {
                return existing;
            }
            if (boxRef is not PooledBoxRef pooled)
            {
                // An instance ref that was never CreateBox'd - not expected in the real corpus.
                return null;
            }
            if (_openPooled.TryGetValue(boxRef, out var open))
            {
                return open;
            }
            var nb = new NodeBuilder
            {
                Id = $"o:{fn.Name}#{_occurrenceSeq++}",
                Ref = boxRef,
                NodeTypePath = pooled.Path,
                Kind = BoxInstanceKind.Pooled,
                OwnerFunction = fn.Name,
            };
            _openPooled[boxRef] = nb;
            state.AllNodes.Add(nb);
            return nb;
        }

        // A `Box.Param = value;` is a data connection when the value reads something rather than
        // stating a literal: usually a graph variable (`self.BuddyPawn`), occasionally another box's
        // data-out pin directly. Literals aren't edges - they're just the box's settings.
        private void RecordParamDataEvent(NodeBuilder consumer, SetParamStmt p)
        {
            var (variable, direct) = DataFlowResolver.ClassifyParamValue(p.Value);
            if (variable is not null)
            {
                state.DataEvents.Add(new DataEvent(
                    DataEventKind.Consume, consumer.Id, p.ParamName, variable,
                    null, null, fn.Name, state.StatementOrder));
            }
            else if (direct is { } source && state.NodesByRef.TryGetValue(source.Box, out NodeBuilder? sourceNode))
            {
                // Looked up rather than resolved, so reading a pooled box's pin here can't silently
                // open a fresh occurrence of it as a side effect.
                state.DataEvents.Add(new DataEvent(
                    DataEventKind.DirectConsume, consumer.Id, p.ParamName, null,
                    sourceNode.Id, source.Pin, fn.Name, state.StatementOrder));
            }
        }
    }

    /// <summary>Resolves every pending edge. A target handler can fan out into any number of terminals
    /// (fire box A, then box B, then...) - each becomes its own edge sharing the source pin; a handler
    /// that resolves to zero terminals is a single DeadEnd edge instead.</summary>
    private static List<GraphEdge> ResolveControlEdges(List<PendingEdge> pendingEdges, Dictionary<string, List<EntryEvent>> entryByFunction)
    {
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

        return edges;
    }

    private static ReconstructedGraph Assemble(BuildState state, List<GraphEdge> edges, DominoNodeCatalog? catalog, DominoDebugTwin? twin)
    {
        IReadOnlyDictionary<long, string>? twinNames = twin?.BoxNamesById;

        var nodes = state.AllNodes
            .Select(nb => new GraphNode(nb.Id, nb.Ref, nb.NodeTypePath, nb.Kind, nb.OwnerFunction, nb.Params)
            {
                Signature = catalog?.Resolve(nb.NodeTypePath),
                OriginalName = OriginalNameFor(nb.Ref, twinNames),
            })
            .ToList();

        // Data attribution leans on control flow to tell which of several writers of a graph variable
        // actually reached a given consumer, so the resolver needs the just-resolved control edges.
        var controlAdjacency = edges
            .Where(e => e.Target == EdgeTarget.Node && e.TargetNodeId is not null)
            .Select(e => (e.SourceNodeId, e.TargetNodeId!))
            .Distinct()
            .ToList();

        return new ReconstructedGraph(
            nodes, edges, DataFlowResolver.Resolve(state.DataEvents, controlAdjacency), state.RegisteredDeps, state.LoadedResources);
    }

    /// <summary>A persistent box's `self[N]` slot is its original editor box ID, which is what the debug
    /// twin's `box_&lt;Display&gt;_&lt;N&gt;` names are keyed on. The named form already carries that name
    /// verbatim. Pooled occurrences get nothing: the twin numbers them with editor IDs the release file
    /// discarded when it collapsed them onto one shared slot.</summary>
    private static string? OriginalNameFor(BoxRef box, IReadOnlyDictionary<long, string>? twinNames) => box switch
    {
        NamedInstanceBoxRef named => named.FieldName,
        InstanceBoxRef instance when twinNames?.TryGetValue(instance.Slot, out string? name) == true => name,
        _ => null,
    };

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
