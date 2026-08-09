namespace JackAll.Tools.Domino.Graphs;

/// <summary>
/// One connection exactly as the original Domino editor recorded it, recovered from a
/// `*.debug.lua`'s `TraceConnection` call.
///
/// <see cref="SourceBox"/>/<see cref="TargetBox"/> are null when that end is the graph itself rather
/// than a box - a graph's own control-in firing inward, or a box firing the graph's control-out.
/// Pin labels are the human strings the editor displayed, spaces and all (`"Greet finished"`), which is
/// what makes them worth keeping: the generated Lua only ever has the mangled identifier
/// (`Greet_finished`).
/// </summary>
public sealed record TracedConnection(
    string ConnectionId,
    string? SourceBox,
    string SourcePinLabel,
    string? TargetBox,
    string TargetPinLabel);

/// <summary>
/// The contents of a mission graph's `*.debug.lua` twin - the same graph, compiled with instrumentation
/// that restates every control connection verbatim. 16,005 of these across the corpus.
///
/// Worth reading for two reasons. It carries names the release file threw away: each box is
/// `box_&lt;DisplayText&gt;_&lt;id&gt;` (`box_Set_Entity_2`, from `&lt;Display Text="Set Entity"/&gt;`),
/// where the id is the box's original `.domino.xml` identifier - the same number the release file uses
/// as its `self[N]` slot. And because it is an independent statement of the same topology, it can be
/// diffed against what <see cref="GraphBuilder"/> inferred, which is the only external check available
/// on the reconstruction.
///
/// It covers control connections only; data links never appear.
/// </summary>
public sealed record DominoDebugTwin(
    string? DocumentPath,
    string? GraphName,
    IReadOnlyList<TracedConnection> Connections)
{
    /// <summary>Rebuilds the twin's view of a graph from its parsed `*.debug.lua`. Returns null when the
    /// file carries no `TraceConnection` calls at all - i.e. it isn't actually a debug twin.</summary>
    public static DominoDebugTwin? FromGraph(UserGraph twinGraph)
    {
        var connections = new List<TracedConnection>();
        string? documentPath = null;
        string? graphName = null;

        foreach (UserGraphFunction fn in twinGraph.Functions)
        {
            foreach (UserGraphStmt stmt in fn.Body)
            {
                if (stmt is not TraceConnectionStmt trace)
                {
                    continue;
                }

                (string? doc, string? graph, string id) = SplitContainer(trace.DocumentContainer);
                documentPath ??= doc;
                graphName ??= graph;

                (string? sourceBox, string sourcePin) = SplitPinLabel(trace.SourcePinLabel);
                (string? targetBox, string targetPin) = SplitPinLabel(trace.TargetPinLabel);
                connections.Add(new TracedConnection(id, sourceBox, sourcePin, targetBox, targetPin));
            }
        }

        return connections.Count > 0 ? new DominoDebugTwin(documentPath, graphName, connections) : null;
    }

    /// <summary>The path of a graph's debug twin, given the graph's own path. The two always sit
    /// side by side (`foo.lua` / `foo.debug.lua`).</summary>
    public static string TwinPathFor(string luaPath) =>
        luaPath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(luaPath.AsSpan(0, luaPath.Length - 4), ".debug.lua")
            : luaPath + ".debug.lua";

    /// <summary>True for a path that is itself a debug twin - those have no twin of their own.</summary>
    public static bool IsTwinPath(string luaPath) =>
        luaPath.EndsWith(".debug.lua", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts an editor pin label to the identifier the generated Lua uses for it. A designer could
    /// type anything into a pin name, so BlackBox mangles it into something Lua will accept: every
    /// character outside `[A-Za-z0-9_]` becomes an underscore, and a name that would then start with a
    /// digit gets one more prefixed.
    ///
    /// `"Greet finished"` → `Greet_finished`, `"Free, if this pawn"` → `Free__if_this_pawn` (the comma
    /// and the space each produce one), `"4a. Wager finished, Buddy healthy"` →
    /// `_4a__Wager_finished__Buddy_healthy`.
    /// </summary>
    public static string ToIdentifier(string pinLabel)
    {
        var chars = new char[pinLabel.Length];
        for (int i = 0; i < pinLabel.Length; i++)
        {
            char c = pinLabel[i];
            chars[i] = char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_';
        }

        string mangled = new(chars);
        return mangled.Length > 0 && char.IsAsciiDigit(mangled[0]) ? '_' + mangled : mangled;
    }

    /// <summary>Every box name the twin mentions, indexed by the trailing original box ID. For a
    /// persistent box that ID is also its `self[N]` slot, which is what lets a reconstructed node
    /// recover its real name.</summary>
    public IReadOnlyDictionary<long, string> BoxNamesById
    {
        get
        {
            var byId = new Dictionary<long, string>();
            foreach (TracedConnection c in Connections)
            {
                foreach (string? box in (string?[])[c.SourceBox, c.TargetBox])
                {
                    if (box is not null && TryParseBoxId(box, out long id))
                    {
                        byId[id] = box;
                    }
                }
            }
            return byId;
        }
    }

    /// <summary>Reads the trailing `_&lt;digits&gt;` off a `box_Set_Entity_2`-style name.</summary>
    public static bool TryParseBoxId(string boxName, out long id)
    {
        id = 0;
        int underscore = boxName.LastIndexOf('_');
        return underscore >= 0
            && underscore < boxName.Length - 1
            && long.TryParse(boxName.AsSpan(underscore + 1), out id);
    }

    /// <summary>`"DocumentContainer|R:\main\...\A1LM02_ReapSew.domino.xml|@A1LM02_BriefingSubvPawnBrief|430462006"`
    /// - the source document, the graph within it, and the connection's own ID.</summary>
    private static (string? Document, string? Graph, string Id) SplitContainer(string container)
    {
        string[] parts = container.Split('|');
        string? document = parts.Length > 1 ? parts[1] : null;
        string? graph = parts.Length > 2 ? parts[2].TrimStart('@') : null;
        string id = parts.Length > 3 ? parts[3] : container;
        return (document, graph, id);
    }

    /// <summary>`"box_Set_Entity_2.FromEntity"` splits into box and pin; a label with no dot is one of
    /// the graph's own pins, so the box is null. Box names never contain a dot (they're built from a
    /// display name), so the first dot is the separator.</summary>
    private static (string? Box, string Pin) SplitPinLabel(string label)
    {
        int dot = label.IndexOf('.');
        return dot < 0 ? (null, label) : (label[..dot], label[(dot + 1)..]);
    }
}

/// <summary>The outcome of diffing a reconstruction against its debug twin. <see cref="NotComparable"/>
/// counts connections skipped because an endpoint is a pooled box: the twin numbers those with their
/// original editor IDs, which the release file discards when it collapses them onto a shared runtime
/// slot, so there is no sound way to line them up one-to-one.</summary>
public sealed record TwinValidation(
    int Matched,
    int MissingFromReconstruction,
    int ExtraInReconstruction,
    int NotComparable,
    IReadOnlyList<string> Details)
{
    public bool IsClean => MissingFromReconstruction == 0 && ExtraInReconstruction == 0;
}

/// <summary>Cross-checks a <see cref="GraphBuilder"/> reconstruction against the independent topology
/// its debug twin records. Comparison is limited to box-to-box control connections where both ends are
/// persistent boxes - graph-boundary connections aren't <see cref="GraphEdge"/>s (they originate in an
/// entry function, not from a wired pin), and pooled occurrences can't be aligned by ID.</summary>
public static class DebugTwinValidator
{
    public static TwinValidation Validate(ReconstructedGraph graph, DominoDebugTwin twin)
    {
        var nameByNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyDictionary<long, string> namesById = twin.BoxNamesById;

        foreach (GraphNode node in graph.Nodes)
        {
            switch (node.Ref)
            {
                case InstanceBoxRef instance when namesById.TryGetValue(instance.Slot, out string? name):
                    nameByNodeId[node.Id] = name;
                    break;
                case NamedInstanceBoxRef named:
                    nameByNodeId[node.Id] = named.FieldName;
                    break;
            }
        }

        var reconstructed = new HashSet<string>(StringComparer.Ordinal);
        int notComparable = 0;

        foreach (GraphEdge edge in graph.Edges)
        {
            if (edge.Target != EdgeTarget.Node || edge.TargetNodeId is null || edge.TargetPin is null)
            {
                continue;
            }
            if (!nameByNodeId.TryGetValue(edge.SourceNodeId, out string? source)
                || !nameByNodeId.TryGetValue(edge.TargetNodeId, out string? target))
            {
                notComparable++;
                continue;
            }
            reconstructed.Add(Key(source, edge.SourcePin, target, edge.TargetPin));
        }

        var knownNames = new HashSet<string>(nameByNodeId.Values, StringComparer.Ordinal);
        var traced = new HashSet<string>(StringComparer.Ordinal);
        foreach (TracedConnection c in twin.Connections)
        {
            if (c.SourceBox is null || c.TargetBox is null)
            {
                notComparable++; // a graph-boundary connection, which has no GraphEdge counterpart
                continue;
            }
            if (!knownNames.Contains(c.SourceBox) || !knownNames.Contains(c.TargetBox))
            {
                notComparable++; // pooled occurrence - the twin's ID has no release-file equivalent
                continue;
            }
            traced.Add(Key(c.SourceBox, DominoDebugTwin.ToIdentifier(c.SourcePinLabel), c.TargetBox, DominoDebugTwin.ToIdentifier(c.TargetPinLabel)));
        }

        var missing = traced.Except(reconstructed, StringComparer.Ordinal).ToList();
        var extra = reconstructed.Except(traced, StringComparer.Ordinal).ToList();

        var details = missing.Select(m => $"missing: {m}")
            .Concat(extra.Select(e => $"extra:   {e}"))
            .ToList();

        return new TwinValidation(
            traced.Intersect(reconstructed, StringComparer.Ordinal).Count(),
            missing.Count,
            extra.Count,
            notComparable,
            details);
    }

    private static string Key(string sourceBox, string sourcePin, string targetBox, string targetPin) =>
        $"{sourceBox}.{sourcePin} -> {targetBox}.{targetPin}";
}
