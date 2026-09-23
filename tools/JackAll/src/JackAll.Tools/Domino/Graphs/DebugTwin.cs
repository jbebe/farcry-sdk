using System.Text.RegularExpressions;

namespace JackAll.Tools.Domino.Graphs;

/// <summary>
/// One connection exactly as the original Domino editor recorded it, recovered from a
/// `*.debug.lua`'s `TraceConnection` call. Every trace sits directly before the fire it describes;
/// <see cref="Function"/> and <see cref="FireOrdinal"/> say which fire that is.
///
/// <see cref="SourceBox"/>/<see cref="TargetBox"/> are null when that end is the graph itself rather
/// than a box - a graph's own control-in firing inward. Pin labels are the human strings the editor
/// displayed, spaces and all (`"Greet finished"`); the generated Lua only has the mangled identifier.
/// </summary>
public sealed record TracedConnection(
    string ConnectionId,
    string? SourceBox,
    string SourcePinLabel,
    string? TargetBox,
    string TargetPinLabel)
{
    public string Function { get; init; } = "";

    public int FireOrdinal { get; init; }
}

/// <summary>
/// The contents of a mission graph's `*.debug.lua` twin - the same graph, compiled with instrumentation
/// that restates every control connection it fires.
///
/// It carries names the release file threw away: each box is `box_&lt;DisplayText&gt;_&lt;id&gt;`
/// (`box_Set_Entity_2`), where the id is the box's original `.domino.xml` identifier - pooled boxes
/// included. And because it is an independent statement of the same topology, it is what
/// <see cref="GraphBuilder"/>'s reconstruction is checked against. Graph exits (`self:Pin()`) and data
/// links are never traced.
/// </summary>
public sealed record DominoDebugTwin(
    string? DocumentPath,
    string? GraphName,
    IReadOnlyList<TracedConnection> Connections,
    UserGraph Graph)
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
            TraceConnectionStmt? pending = null;
            int fires = 0;
            foreach (UserGraphStmt stmt in fn.Body)
            {
                if (stmt is TraceConnectionStmt trace)
                {
                    pending = trace;
                    continue;
                }
                if (stmt is not FireControlInStmt)
                {
                    continue;
                }
                if (pending is not null)
                {
                    (string? doc, string? graph, string id) = SplitContainer(pending.DocumentContainer);
                    documentPath ??= doc;
                    graphName ??= graph;

                    (string? sourceBox, string sourcePin) = SplitPinLabel(pending.SourcePinLabel);
                    (string? targetBox, string targetPin) = SplitPinLabel(pending.TargetPinLabel);
                    connections.Add(new TracedConnection(id, sourceBox, sourcePin, targetBox, targetPin)
                    {
                        Function = fn.Name,
                        FireOrdinal = fires,
                    });
                    pending = null;
                }
                fires++;
            }
        }

        return connections.Count > 0 ? new DominoDebugTwin(documentPath, graphName, connections, twinGraph) : null;
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
    /// Converts an editor pin label to the identifier the generated Lua uses for it: every character
    /// outside `[A-Za-z0-9_]` becomes an underscore, and a name that would then start with a digit gets
    /// one more prefixed. `"4a. Wager finished, Buddy healthy"` → `_4a__Wager_finished__Buddy_healthy`.
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

    /// <summary>Every box name the twin mentions, indexed by its editor ID.</summary>
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

    /// <summary>Splits a `box_Set_Entity_2`-style name into `Set_Entity` and 2.</summary>
    public static bool TryParseBoxName(string boxName, out string stem, out long id)
    {
        stem = string.Empty;
        if (!TryParseBoxId(boxName, out id))
        {
            return false;
        }
        stem = boxName[..boxName.LastIndexOf('_')];
        stem = stem.StartsWith("box_", StringComparison.Ordinal) ? stem[4..] : stem;
        return true;
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
    /// the graph's own pins, so the box is null.</summary>
    private static (string? Box, string Pin) SplitPinLabel(string label)
    {
        int dot = label.IndexOf('.');
        return dot < 0 ? (null, label) : (label[..dot], label[(dot + 1)..]);
    }
}

/// <summary>
/// Pairs a release graph's functions with its twin's. BlackBox emits both files' functions in the same
/// order, so the pairing is positional - and it only counts once every paired body says the same thing
/// with the twin's traces dropped and its names (`self.box_X_N`, `f_box_X_N_Pin`, `OnEnter_box_X_N`,
/// `_sld_Pin_box_X_N`, `Sub.debug.lua`) rewritten to the release file's (`self[N]`, `f_N_Pin`, `en_N`,
/// `_sld_Pin_N`, `Sub.lua`).
/// </summary>
internal sealed partial class TwinAlignment
{
    private readonly Dictionary<string, string> _twinByRelease = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Function, int Ordinal), TracedConnection> _traces = new();

    public List<string> Problems { get; } = [];

    public TwinAlignment(UserGraph release, DominoDebugTwin twin)
    {
        IReadOnlyList<UserGraphFunction> releaseFns = release.Functions;
        IReadOnlyList<UserGraphFunction> twinFns = twin.Graph.Functions;
        if (releaseFns.Count != twinFns.Count)
        {
            Problems.Add($"the twin has {twinFns.Count} functions, the release file {releaseFns.Count}");
            return;
        }

        var releaseRoles = GraphFunctions.Classify(release);
        var twinRoles = GraphFunctions.Classify(twin.Graph);
        var releaseByTwin = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < releaseFns.Count; i++)
        {
            GraphFunction r = releaseRoles[releaseFns[i].Name];
            GraphFunction t = twinRoles[twinFns[i].Name];
            if (r.Role != t.Role || r.BoxId != t.BoxId)
            {
                Problems.Add($"function {i} is {releaseFns[i].Name} in the release file but {twinFns[i].Name} in the twin");
                return;
            }
            releaseByTwin[twinFns[i].Name] = releaseFns[i].Name;
            _twinByRelease[releaseFns[i].Name] = twinFns[i].Name;
        }

        for (int i = 0; i < releaseFns.Count; i++)
        {
            var releaseBody = releaseFns[i].Body.Select(UserGraphWriter.Canonical).ToList();
            var twinBody = twinFns[i].Body
                .Where(stmt => stmt is not TraceConnectionStmt)
                .Select(stmt => Normalize(UserGraphWriter.Canonical(stmt), releaseByTwin))
                .ToList();
            if (!releaseBody.SequenceEqual(twinBody, StringComparer.Ordinal))
            {
                int at = releaseBody.Zip(twinBody).TakeWhile(p => p.First == p.Second).Count();
                Problems.Add($"{releaseFns[i].Name} differs from its twin at statement {at}");
            }
        }

        foreach (TracedConnection trace in twin.Connections)
        {
            _traces[(trace.Function, trace.FireOrdinal)] = trace;
        }
    }

    public bool IsProven => Problems.Count == 0;

    /// <summary>The trace the twin records for a release function's Nth fire.</summary>
    public TracedConnection? TraceFor(string releaseFunction, int fireOrdinal) =>
        IsProven && _twinByRelease.TryGetValue(releaseFunction, out string? twinFunction)
        && _traces.TryGetValue((twinFunction, fireOrdinal), out TracedConnection? trace)
            ? trace
            : null;

    /// <summary>Rewrites a twin statement in release terms, including its sub-graphs, which a twin names by
    /// their own twins.</summary>
    private static string Normalize(string statement, Dictionary<string, string> releaseByTwin)
    {
        string subGraphs = statement.Replace(".debug.lua\"", ".lua\"", StringComparison.Ordinal);
        string temporaries = TwinTemporary().Replace(subGraphs, m => $"_sld_{m.Groups["pin"].Value}_{m.Groups["id"].Value}");
        string boxes = TwinBoxRef().Replace(temporaries, m => $"self[{m.Groups["id"].Value}]");
        return OwnHandler().Replace(boxes, m =>
            releaseByTwin.TryGetValue(m.Groups["name"].Value, out string? name) ? $"self._type.{name}" : m.Value);
    }

    [GeneratedRegex(@"self\.box_\w+_(?<id>\d+)\b")]
    private static partial Regex TwinBoxRef();

    // `_sld_Target_box_String_Concatenate_8` is the release file's `_sld_Target_8`.
    [GeneratedRegex(@"_sld_(?<pin>\w+?)_box_\w+_(?<id>\d+)\b")]
    private static partial Regex TwinTemporary();

    [GeneratedRegex(@"self\._type\.(?<name>\w+)")]
    private static partial Regex OwnHandler();
}

/// <summary>
/// The outcome of checking a reconstruction against its debug twin. Every traced fire has to be one
/// of the reconstruction's edges, source and target alike; <see cref="NamedFromTwin"/> counts the ones
/// whose target box only got its ID from the twin itself, so agree by construction.
/// </summary>
public sealed record TwinValidation(
    int TracedFires,
    int Matched,
    int NamedFromTwin,
    IReadOnlyList<string> Problems)
{
    public bool IsClean => Problems.Count == 0;
}
