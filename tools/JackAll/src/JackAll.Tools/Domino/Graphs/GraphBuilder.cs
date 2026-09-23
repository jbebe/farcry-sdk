using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Graphs;

/// <summary>
/// Rebuilds a <see cref="ReconstructedGraph"/> (boxes, typed pins, connections) from a
/// <see cref="UserGraphParser"/>-classified file.
///
/// A `user\` file's `export:` functions aren't graph nodes themselves - they're the flattened
/// continuation code BlackBox generated for "whatever runs after this pin fires". The nodes are the
/// editor's boxes. A persistent box (`self[N]`) is one node. A pooled box (`Boxes[PathID(...)]`) is a
/// runtime slot every box of that type shares, configured and fired where it is used; which editor box
/// each use was is read off the generated names, which all carry the box's editor ID: a control-out wired
/// to `f_N_Pin` is box N's, `en_N` configures box N, and `f_N_Pin` and `ex_N` read box N's outputs. A use
/// nothing names takes its ID from the debug twin's trace, when there is one.
///
/// `en_N` and `ex_N` are walked in place, where they are called, so what they configure and read belongs
/// to the box and the handler that called them.
/// </summary>
public static class GraphBuilder
{
    /// <param name="catalog">Resolves each box's node type to its pin interface. Optional: without one
    /// the graph still reconstructs, the nodes just carry no <see cref="GraphNode.Signature"/>.</param>
    /// <param name="twin">The graph's parsed `*.debug.lua`, when available - names every box, and is what
    /// the reconstruction is checked against.</param>
    public static ReconstructedGraph Build(UserGraph graph, DominoNodeCatalog? catalog = null, DominoDebugTwin? twin = null) =>
        new Builder(graph, twin).Build(catalog);

    private sealed class NodeState
    {
        public required string Id;
        public required BoxRef Ref;
        public required string Path;
        public required BoxInstanceKind Kind;
        public long? EditorId;
        public BoxIdSource IdSource;
        public readonly Dictionary<string, ExpressionSyntax> Params = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> DynamicSlots = new(StringComparer.Ordinal);
        public readonly List<int> Positions = [];
    }

    /// <summary>A pooled box's configuration since its slot was last fired, held until the fire says
    /// which box it was.</summary>
    private sealed class Episode
    {
        public long? Candidate;
        public BoxIdSource CandidateSource = BoxIdSource.None;
        public readonly List<Action<NodeState>> Apply = [];
    }

    private enum TerminalKind { Node, Exit }

    private sealed record Terminal(TerminalKind Kind, string? NodeId, string Pin, int? Index, string? FiredIn, int? FireOrdinal);

    private sealed record Wire(string SourceNodeId, string Pin, int? Index, string? Handler, int? Position);

    private sealed class Builder
    {
        private readonly UserGraph _graph;
        private readonly Dictionary<string, UserGraphFunction> _functions = new(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, GraphFunction> _roles;
        private readonly HashSet<string> _twinBoxNames;
        private readonly DominoDebugTwin? _twin;
        private readonly TwinAlignment? _alignment;

        private readonly Dictionary<string, NodeState> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<BoxRef, string> _persistentIds = new();
        private readonly Dictionary<long, string> _pathOf = new();
        private readonly List<DominoFinding> _findings = [];
        private readonly List<string> _registered = [];
        private readonly List<(string, string)> _resources = [];
        private readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);
        private readonly List<DataEvent> _dataEvents = [];
        private readonly List<Wire> _wires = [];
        private readonly Dictionary<string, List<Terminal>> _terminals = new(StringComparer.Ordinal);
        private readonly HashSet<string> _calledHelpers = new(StringComparer.Ordinal);
        private int _order;

        public Builder(UserGraph graph, DominoDebugTwin? twin)
        {
            _graph = graph;
            foreach (UserGraphFunction fn in graph.Functions)
            {
                _functions.TryAdd(fn.Name, fn);
            }
            _roles = GraphFunctions.Classify(graph);
            _twinBoxNames = GraphFunctions.TwinBoxNamesOf(graph);
            _twin = twin;
            _alignment = twin is null ? null : new TwinAlignment(graph, twin);
        }

        public ReconstructedGraph Build(DominoNodeCatalog? catalog)
        {
            RegisterPersistentBoxes();
            CollectPooledPaths();

            foreach (UserGraphFunction fn in _graph.Functions)
            {
                if (_roles[fn.Name].Role is FunctionRole.Handler or FunctionRole.Entry or FunctionRole.Lifecycle)
                {
                    _terminals[fn.Name] = new Walker(this, fn).Walk();
                }
            }

            foreach (GraphFunction helper in _roles.Values)
            {
                if (helper.Role is FunctionRole.Prologue or FunctionRole.Epilogue && !_calledHelpers.Contains(helper.Name))
                {
                    Report(LintSeverity.Info, "uncalled-helper", $"{helper.Name} is never called", helper.Name, null);
                }
            }
            if (_terminals.TryGetValue("Init", out var initFires) && initFires.Count > 0)
            {
                Report(LintSeverity.Warning, "lifecycle-fire", "Init fires a control pin", "Init", null);
            }

            List<GraphEdge> edges = ResolveEdges();
            return Assemble(edges, catalog);
        }

        // ------------------------------------------------------------ identity

        private void RegisterPersistentBoxes()
        {
            foreach (UserGraphFunction fn in _graph.Functions)
            {
                foreach (CreateBoxStmt create in fn.Body.OfType<CreateBoxStmt>())
                {
                    long? id = create.Box switch
                    {
                        InstanceBoxRef i => i.Slot,
                        NamedInstanceBoxRef n when DominoDebugTwin.TryParseBoxId(n.FieldName, out long parsed) => parsed,
                        _ => null,
                    };
                    string nodeId = id is { } editorId ? $"p:{editorId}" : $"p:{Label(create.Box)}";
                    if (_nodes.ContainsKey(nodeId))
                    {
                        Report(LintSeverity.Error, "identity-conflict", $"box {nodeId} is created twice", fn.Name, create.Syntax);
                        continue;
                    }
                    _nodes[nodeId] = new NodeState
                    {
                        Id = nodeId,
                        Ref = create.Box,
                        Path = create.Path,
                        Kind = BoxInstanceKind.Persistent,
                        EditorId = id,
                        IdSource = BoxIdSource.Slot,
                    };
                    _persistentIds[create.Box] = nodeId;
                    if (id is { } known)
                    {
                        _pathOf[known] = create.Path;
                    }
                }
            }
        }

        /// <summary>Which node type each pooled editor box is, from the wires and helpers that name it -
        /// what lets a continuation's read find the box it continues.</summary>
        private void CollectPooledPaths()
        {
            foreach (UserGraphFunction fn in _graph.Functions)
            {
                GraphFunction role = _roles[fn.Name];
                foreach (UserGraphStmt stmt in fn.Body)
                {
                    if (stmt is WireControlOutStmt { Box: PooledBoxRef wired, TargetHandler: { } handler }
                        && HandlerBox(handler) is { } owner)
                    {
                        NotePooledPath(owner, wired.Path, fn.Name, stmt);
                    }
                    if (role.Role is FunctionRole.Prologue or FunctionRole.Epilogue && PooledRefOf(stmt) is { } touched)
                    {
                        NotePooledPath(role.BoxId!.Value, touched.Path, fn.Name, stmt);
                    }
                }
            }
        }

        private void NotePooledPath(long id, string path, string function, UserGraphStmt stmt)
        {
            if (!_pathOf.TryAdd(id, path) && _pathOf[id] != path)
            {
                Report(LintSeverity.Error, "identity-conflict",
                    $"box {id} is named as both {_pathOf[id]} and {path}", function, stmt.Syntax);
            }
        }

        private long? HandlerBox(string handler)
        {
            GraphFunction role = _roles.TryGetValue(handler, out GraphFunction? known)
                ? known
                : GraphFunctions.Classify(handler, isOutAnchor: false, _twinBoxNames);
            return role.Role == FunctionRole.Handler ? role.BoxId : null;
        }

        private static PooledBoxRef? PooledRefOf(UserGraphStmt stmt) => stmt switch
        {
            SetParamStmt { Box: PooledBoxRef p } => p,
            WireControlOutStmt { Box: PooledBoxRef p } => p,
            SetGraphBackrefStmt { Box: PooledBoxRef p } => p,
            SetDynamicAnchorsStmt { Box: PooledBoxRef p } => p,
            ReadDataStmt { Box: PooledBoxRef p } => p,
            FireControlInStmt { Box: PooledBoxRef p } => p,
            _ => null,
        };

        private NodeState PooledNode(long id, string path, BoxIdSource source, string function, StatementSyntax? at)
        {
            string nodeId = $"q:{id}";
            if (_nodes.TryGetValue(nodeId, out NodeState? existing))
            {
                if (existing.Path != path)
                {
                    Report(LintSeverity.Error, "identity-conflict",
                        $"box {id} is used as both {existing.Path} and {path}", function, at);
                }
                if (source < existing.IdSource)
                {
                    existing.IdSource = source;
                }
                return existing;
            }
            if (_nodes.ContainsKey($"p:{id}"))
            {
                Report(LintSeverity.Error, "identity-conflict",
                    $"pooled box {id} shares its ID with a persistent box", function, at);
            }

            var node = new NodeState
            {
                Id = nodeId,
                Ref = new PooledBoxRef(path),
                Path = path,
                Kind = BoxInstanceKind.Pooled,
                EditorId = id,
                IdSource = source,
            };
            _nodes[nodeId] = node;
            return node;
        }

        private NodeState AnonymousNode(string path, string function)
        {
            int seq = _nodes.Keys.Count(k => k.StartsWith($"q?:{function}#", StringComparison.Ordinal));
            var node = new NodeState
            {
                Id = $"q?:{function}#{seq}",
                Ref = new PooledBoxRef(path),
                Path = path,
                Kind = BoxInstanceKind.Pooled,
                IdSource = BoxIdSource.None,
            };
            _nodes[node.Id] = node;
            return node;
        }

        // ------------------------------------------------------------ walking

        /// <summary>Walks one handler, entry pin or lifecycle function, with every `en_N`/`ex_N` it calls
        /// walked in place, and returns what it fires in order.</summary>
        private sealed class Walker(Builder b, UserGraphFunction root)
        {
            private readonly Dictionary<string, Episode> _open = new(StringComparer.Ordinal);
            private readonly Dictionary<string, NodeState> _lastFired = new(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _fireCount = new(StringComparer.Ordinal);
            private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
            private readonly List<Terminal> _terminals = [];

            public List<Terminal> Walk()
            {
                GraphFunction role = b._roles[root.Name];
                WalkBody(root, role.Role == FunctionRole.Handler ? role.BoxId : null, prologueOf: null);
                foreach ((string path, Episode episode) in _open)
                {
                    b.Report(LintSeverity.Warning, "unfired-configuration",
                        $"{Short(path)} is configured but never fired", root.Name, null);
                    Flush(episode, path, stmt: null, ordinal: null, physical: root.Name);
                }
                return _terminals;
            }

            private void WalkBody(UserGraphFunction fn, long? owner, long? prologueOf)
            {
                _visiting.Add(fn.Name);
                foreach (UserGraphStmt stmt in fn.Body)
                {
                    b._order++;
                    Visit(fn, stmt, owner, prologueOf);
                }
                _visiting.Remove(fn.Name);
            }

            private void Visit(UserGraphFunction fn, UserGraphStmt stmt, long? owner, long? prologueOf)
            {
                switch (stmt)
                {
                    case RegisterBoxStmt r:
                        b._registered.Add(r.Path);
                        break;

                    case LoadResourceStmt l:
                        b._resources.Add((l.ResourceName, l.ResourceType));
                        break;

                    case CreateBoxStmt create when b._persistentIds.TryGetValue(create.Box, out string? created):
                        Note(b._nodes[created], stmt);
                        break;

                    case SetGraphFieldStmt field when root.Name == "Init"
                                                      && !field.FieldName.StartsWith("box_", StringComparison.Ordinal)
                                                      && !DominoNodeCatalog.IsDummyFunction(field.Value):
                        b._defaults[field.FieldName] = DominoExprPreview.Full(field.Value);
                        break;

                    case SetGraphBackrefStmt g:
                        Configure(g.Box, stmt, prologueOf, _ => { });
                        break;

                    case SetDynamicAnchorsStmt anchors:
                        Configure(anchors.Box, stmt, prologueOf, node =>
                        {
                            foreach ((string pin, int count) in anchors.Counts)
                            {
                                node.DynamicSlots[pin] = count;
                            }
                        });
                        break;

                    case SetParamStmt p:
                        SetParam(p, owner, prologueOf);
                        break;

                    case WireControlOutStmt w:
                        Configure(w.Box, stmt, prologueOf, node =>
                            b._wires.Add(new Wire(node.Id, w.PinName, w.Index, w.TargetHandler, stmt.Syntax?.SpanStart)));
                        if (w.Box is PooledBoxRef wired && w.TargetHandler is { } handler && b.HandlerBox(handler) is { } named)
                        {
                            Nominate(_open[wired.Path], named, BoxIdSource.Wire, fn.Name, stmt);
                        }
                        break;

                    case FireControlInStmt f when root.Name != "ShutDown":
                        Fire(fn, f, owner);
                        break;

                    case CallOwnHandlerStmt call:
                        Call(fn, call, owner);
                        break;

                    case FireOwnPinStmt pin:
                        _terminals.Add(new Terminal(TerminalKind.Exit, null, pin.PinName, null, fn.Name, null));
                        break;

                    case ReadDataStmt read:
                        Read(fn, read, owner);
                        break;

                    case OtherStmt when fn.Name != "LuaDependencies":
                        b.Report(LintSeverity.Warning, "unrepresented",
                            "a statement the graph view does not show", fn.Name, stmt.Syntax);
                        break;
                }
            }

            /// <summary>Applies a configuring statement to its box: now for a persistent box, at the next
            /// fire for a pooled one.</summary>
            private void Configure(BoxRef box, UserGraphStmt stmt, long? prologueOf, Action<NodeState> apply)
            {
                if (box is PooledBoxRef pooled)
                {
                    if (!_open.TryGetValue(pooled.Path, out Episode? episode))
                    {
                        episode = new Episode();
                        _open[pooled.Path] = episode;
                    }
                    if (prologueOf is { } id)
                    {
                        Nominate(episode, id, BoxIdSource.Prologue, root.Name, stmt);
                    }
                    int? position = stmt.Syntax?.SpanStart;
                    episode.Apply.Add(node =>
                    {
                        Note(node, position);
                        apply(node);
                    });
                    return;
                }
                if (b.Persistent(box, root.Name, stmt) is { } node)
                {
                    Note(node, stmt);
                    apply(node);
                }
            }

            private void Nominate(Episode episode, long id, BoxIdSource source, string function, UserGraphStmt stmt)
            {
                if (episode.Candidate is { } existing && existing != id)
                {
                    b.Report(LintSeverity.Error, "identity-conflict",
                        $"one configuration names both box {existing} and box {id}", function, stmt.Syntax);
                    return;
                }
                episode.Candidate = id;
                if (source < episode.CandidateSource)
                {
                    episode.CandidateSource = source;
                }
            }

            private void SetParam(SetParamStmt p, long? owner, long? prologueOf)
            {
                int order = b._order;
                string function = root.Name;
                (string? variable, (BoxRef Box, string Pin)? direct) = DataFlowResolver.ClassifyParamValue(p.Value);
                NodeState? directSource = direct is { } d ? ResolveRead(d.Box, owner, p) : null;

                Configure(p.Box, p, prologueOf, node =>
                {
                    if (node.Params.TryGetValue(p.ParamName, out ExpressionSyntax? earlier)
                        && earlier.ToString() != p.Value.ToString())
                    {
                        b.Report(LintSeverity.Info, "param-conflict",
                            $"{p.ParamName} is set to both {earlier} and {p.Value}", function, p.Syntax, node.Id);
                    }
                    node.Params[p.ParamName] = p.Value;

                    if (variable is not null)
                    {
                        b._dataEvents.Add(new DataEvent(DataEventKind.Consume, node.Id, p.ParamName, variable, null, null, function, order));
                    }
                    else if (directSource is not null)
                    {
                        b._dataEvents.Add(new DataEvent(DataEventKind.DirectConsume, node.Id, p.ParamName, null,
                            directSource.Id, direct!.Value.Pin, function, order));
                    }
                });
            }

            private void Fire(UserGraphFunction fn, FireControlInStmt f, long? owner)
            {
                int ordinal = _fireCount.GetValueOrDefault(fn.Name);
                _fireCount[fn.Name] = ordinal + 1;

                NodeState? node = f.Box is PooledBoxRef pooled
                    ? FirePooled(pooled.Path, f, owner, ordinal, fn.Name)
                    : b.Persistent(f.Box, fn.Name, f);
                if (node is null)
                {
                    return;
                }
                Note(node, f);
                _terminals.Add(new Terminal(TerminalKind.Node, node.Id, f.PinName, f.Index, fn.Name, ordinal));
            }

            private NodeState FirePooled(string path, FireControlInStmt f, long? owner, int ordinal, string physical)
            {
                NodeState node;
                if (_open.Remove(path, out Episode? episode))
                {
                    node = Flush(episode, path, f, ordinal, physical);
                }
                else if (_lastFired.TryGetValue(path, out NodeState? again))
                {
                    node = again;
                }
                else if (owner is { } id && b._pathOf.GetValueOrDefault(id) == path)
                {
                    node = b.PooledNode(id, path, BoxIdSource.Continuation, root.Name, f.Syntax);
                }
                else
                {
                    b.Report(LintSeverity.Error, "bare-fire",
                        $"{Short(path)} is fired without being configured", root.Name, f.Syntax);
                    node = b.TwinNamed(path, physical, ordinal, f.Syntax) ?? b.AnonymousNode(path, root.Name);
                }
                _lastFired[path] = node;
                return node;
            }

            private NodeState Flush(Episode episode, string path, UserGraphStmt? stmt, int? ordinal, string physical)
            {
                NodeState node = episode.Candidate is { } id
                    ? b.PooledNode(id, path, episode.CandidateSource, root.Name, stmt?.Syntax)
                    : (ordinal is { } k ? b.TwinNamed(path, physical, k, stmt?.Syntax) : null) ?? b.AnonymousNode(path, root.Name);
                foreach (Action<NodeState> apply in episode.Apply)
                {
                    apply(node);
                }
                return node;
            }

            private void Call(UserGraphFunction fn, CallOwnHandlerStmt call, long? owner)
            {
                if (!b._functions.TryGetValue(call.HandlerName, out UserGraphFunction? callee))
                {
                    b.Report(LintSeverity.Error, "undefined-handler",
                        $"{call.HandlerName} is called but not defined", fn.Name, call.Syntax);
                    return;
                }
                if (_visiting.Contains(callee.Name))
                {
                    return;
                }

                GraphFunction role = b._roles[callee.Name];
                bool helper = role.Role is FunctionRole.Prologue or FunctionRole.Epilogue;
                if (helper)
                {
                    b._calledHelpers.Add(callee.Name);
                }
                WalkBody(callee, helper ? role.BoxId : owner, role.Role == FunctionRole.Prologue ? role.BoxId : null);
            }

            private void Read(UserGraphFunction fn, ReadDataStmt read, long? owner)
            {
                if (ResolveRead(read.Box, owner, read) is not { } node)
                {
                    return;
                }
                Note(node, read);
                if (DominoNodeCatalog.GraphFieldName(read.Target) is { } variable)
                {
                    b._dataEvents.Add(new DataEvent(DataEventKind.Produce, node.Id, read.PinName, variable, null, null, root.Name, b._order));
                }
                else if (!read.Target.ToString().StartsWith("Globals.", StringComparison.Ordinal))
                {
                    b.Report(LintSeverity.Warning, "unrepresented",
                        $"{read.Target} is written from a box output", fn.Name, read.Syntax);
                }
            }

            /// <summary>The box a data-out read refers to. A pooled read never starts a configuration: it
            /// is the box this handler just fired, or else the box whose continuation this is.</summary>
            private NodeState? ResolveRead(BoxRef box, long? owner, UserGraphStmt stmt)
            {
                if (box is not PooledBoxRef pooled)
                {
                    return b.Persistent(box, root.Name, stmt);
                }
                if (_lastFired.TryGetValue(pooled.Path, out NodeState? fired))
                {
                    return fired;
                }
                if (owner is { } id && b._pathOf.GetValueOrDefault(id) == pooled.Path)
                {
                    return b.PooledNode(id, pooled.Path, BoxIdSource.Continuation, root.Name, stmt.Syntax);
                }
                b.Report(LintSeverity.Error, "unbound-read",
                    $"{Short(pooled.Path)}'s output is read outside its own continuation", root.Name, stmt.Syntax);
                return null;
            }

            private static void Note(NodeState node, UserGraphStmt stmt) => Note(node, stmt.Syntax?.SpanStart);

            private static void Note(NodeState node, int? position)
            {
                if (position is { } p && !node.Positions.Contains(p))
                {
                    node.Positions.Add(p);
                }
            }
        }

        private NodeState? Persistent(BoxRef box, string function, UserGraphStmt stmt)
        {
            if (_persistentIds.TryGetValue(box, out string? id))
            {
                return _nodes[id];
            }
            Report(LintSeverity.Error, "undefined-box", $"{Label(box)} is used but never created", function, stmt.Syntax);
            return null;
        }

        /// <summary>A pooled box nothing in the release code names, named by the twin's trace of this fire.</summary>
        private NodeState? TwinNamed(string path, string function, int ordinal, StatementSyntax? at) =>
            _alignment?.TraceFor(function, ordinal) is { TargetBox: { } box } && DominoDebugTwin.TryParseBoxId(box, out long id)
                ? PooledNode(id, path, BoxIdSource.Twin, function, at)
                : null;

        // ------------------------------------------------------------ edges

        private List<GraphEdge> ResolveEdges()
        {
            var edges = new List<GraphEdge>();
            var seen = new Dictionary<(string, string, int?), string?>();

            foreach (Wire wire in _wires)
            {
                if (seen.TryGetValue((wire.SourceNodeId, wire.Pin, wire.Index), out string? earlier))
                {
                    if (earlier != wire.Handler)
                    {
                        Report(LintSeverity.Error, "wire-conflict",
                            $"{wire.Pin} is wired to both {earlier ?? "nothing"} and {wire.Handler ?? "nothing"}", null, null, wire.SourceNodeId);
                    }
                    continue;
                }
                seen[(wire.SourceNodeId, wire.Pin, wire.Index)] = wire.Handler;

                if (wire.Handler is null)
                {
                    edges.Add(new GraphEdge(wire.SourceNodeId, wire.Pin, wire.Index, EdgeTarget.Unwired, null, null, null));
                    continue;
                }
                if (!_functions.ContainsKey(wire.Handler))
                {
                    Report(LintSeverity.Error, "undefined-handler",
                        $"{wire.Pin} is wired to {wire.Handler}, which is not defined", null, null, wire.SourceNodeId);
                }
                edges.AddRange(EdgesTo(wire.SourceNodeId, wire.Pin, wire.Index, wire.Handler));
            }

            foreach (GraphFunction entry in _roles.Values.Where(f => f.Role == FunctionRole.Entry))
            {
                edges.AddRange(EdgesTo(null, entry.Name, null, entry.Name));
            }

            var wiredHandlers = _wires.Select(w => w.Handler).ToHashSet(StringComparer.Ordinal);
            foreach (GraphFunction handler in _roles.Values.Where(f => f.Role == FunctionRole.Handler && !wiredHandlers.Contains(f.Name)))
            {
                Report(LintSeverity.Info, "unwired-handler", $"{handler.Name} is never wired, so it never runs", handler.Name, null);
            }
            return edges;
        }

        private IEnumerable<GraphEdge> EdgesTo(string? source, string pin, int? index, string handler)
        {
            if (!_terminals.TryGetValue(handler, out List<Terminal>? terminals) || terminals.Count == 0)
            {
                yield return new GraphEdge(source, pin, index, EdgeTarget.DeadEnd, null, null, null);
                yield break;
            }
            foreach (Terminal t in terminals)
            {
                yield return t.Kind == TerminalKind.Node
                    ? new GraphEdge(source, pin, index, EdgeTarget.Node, t.NodeId, t.Pin, null)
                    {
                        TargetIndex = t.Index,
                        FiredIn = t.FiredIn,
                        FireOrdinal = t.FireOrdinal,
                    }
                    : new GraphEdge(source, pin, index, EdgeTarget.GraphExit, null, null, t.Pin) { FiredIn = t.FiredIn };
            }
        }

        // ------------------------------------------------------------ output

        private ReconstructedGraph Assemble(List<GraphEdge> edges, DominoNodeCatalog? catalog)
        {
            IReadOnlyDictionary<long, string>? twinNames = _twin?.BoxNamesById;
            var nodes = _nodes.Values
                .Select(n => new GraphNode(n.Id, n.Ref, n.Path, n.Kind, n.Params)
                {
                    EditorId = n.EditorId,
                    IdSource = n.IdSource,
                    SourcePositions = n.Positions.Order().ToList(),
                    DynamicSlots = n.DynamicSlots,
                    Signature = catalog?.Resolve(n.Path),
                    OriginalName = n.Ref is NamedInstanceBoxRef named
                        ? named.FieldName
                        : n.EditorId is { } id && twinNames?.TryGetValue(id, out string? name) == true ? name : null,
                })
                .ToList();

            // Data attribution leans on control flow to tell which of several writers of a graph variable
            // actually reached a given consumer, so the resolver needs the just-resolved control edges.
            var controlAdjacency = edges
                .Where(e => e.Target == EdgeTarget.Node && e.SourceNodeId is not null && e.TargetNodeId is not null)
                .Select(e => (e.SourceNodeId!, e.TargetNodeId!))
                .Distinct()
                .ToList();

            return new ReconstructedGraph(
                nodes, edges, DataFlowResolver.Resolve(_dataEvents, controlAdjacency).Distinct().ToList(),
                _registered, _resources, _defaults)
            {
                Functions = _roles,
                Findings = _findings,
                Twin = _alignment is null ? null : Validate(edges),
            };
        }

        /// <summary>Checks every traced fire in the twin against the edge that fires it.</summary>
        private TwinValidation Validate(List<GraphEdge> edges)
        {
            var problems = _alignment!.Problems.Select(p => $"twin: {p}").ToList();
            int traced = _twin!.Connections.Count;
            if (!_alignment.IsProven)
            {
                return new TwinValidation(traced, 0, 0, problems);
            }

            var matched = new HashSet<TracedConnection>();
            int namedFromTwin = 0;
            foreach (GraphEdge edge in edges.Where(e => e.Target == EdgeTarget.Node))
            {
                TracedConnection? trace = _alignment.TraceFor(edge.FiredIn!, edge.FireOrdinal!.Value);
                if (trace is null)
                {
                    problems.Add($"untraced: {Describe(edge)}");
                    continue;
                }

                NodeState target = _nodes[edge.TargetNodeId!];
                bool sourceAgrees = edge.FromEntry
                    ? trace.SourceBox is null && DominoDebugTwin.ToIdentifier(trace.SourcePinLabel) == edge.SourcePin
                    : trace.SourceBox is { } sourceBox
                      && DominoDebugTwin.TryParseBoxId(sourceBox, out long sourceId)
                      && sourceId == _nodes[edge.SourceNodeId!].EditorId
                      && PinAgrees(trace.SourcePinLabel, edge.SourcePin, edge.Index);
                bool targetAgrees = trace.TargetBox is { } targetBox
                    && DominoDebugTwin.TryParseBoxId(targetBox, out long targetId)
                    && targetId == target.EditorId
                    && DominoDebugTwin.ToIdentifier(trace.TargetPinLabel) == edge.TargetPin;

                if (sourceAgrees && targetAgrees)
                {
                    if (matched.Add(trace) && target.IdSource == BoxIdSource.Twin)
                    {
                        namedFromTwin++;
                    }
                }
                else
                {
                    problems.Add($"disagrees: {Describe(edge)} vs {trace.SourceBox}.{trace.SourcePinLabel} -> {trace.TargetBox}.{trace.TargetPinLabel}");
                }
            }

            foreach (TracedConnection trace in _twin.Connections.Where(t => !matched.Contains(t)))
            {
                problems.Add($"not reconstructed: {trace.SourceBox}.{trace.SourcePinLabel} -> {trace.TargetBox}.{trace.TargetPinLabel} ({trace.Function})");
            }
            return new TwinValidation(traced, matched.Count, namedFromTwin, problems);
        }

        /// <summary>A traced source pin against a wired one; a dynamic out's slot shows in the twin's
        /// label as a suffix.</summary>
        private static bool PinAgrees(string label, string pin, int? index)
        {
            string traced = DominoDebugTwin.ToIdentifier(label);
            return traced == pin || (index is { } i && traced == $"{pin}_{i}");
        }

        private string Describe(GraphEdge edge) =>
            $"{(edge.FromEntry ? edge.SourcePin : $"{edge.SourceNodeId}.{edge.SourcePin}")} -> {edge.TargetNodeId}.{edge.TargetPin} ({edge.FiredIn}#{edge.FireOrdinal})";

        private void Report(LintSeverity severity, string rule, string message, string? function, StatementSyntax? syntax, string? nodeId = null) =>
            _findings.Add(new DominoFinding(severity, rule, message)
            {
                Function = function,
                Position = syntax?.SpanStart,
                NodeId = nodeId,
            });
    }

    private static string Label(BoxRef box) => box switch
    {
        InstanceBoxRef i => $"self[{i.Slot}]",
        NamedInstanceBoxRef n => $"self.{n.FieldName}",
        PooledBoxRef p => p.Path,
        _ => box.ToString(),
    };

    private static string Short(string path) => NodeSignature.ShortNameFor(path);
}
