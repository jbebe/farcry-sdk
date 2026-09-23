using System.Text.RegularExpressions;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.Tools.Domino.Graphs;

/// <summary>What a `function export:Name` in a user graph is for.</summary>
public enum FunctionRole
{
    /// <summary>`LuaDependencies`, `Create`, `Init`, `ShutDown`.</summary>
    Lifecycle,

    /// <summary>`f_N_Pin` - runs when box N's control-out Pin fires.</summary>
    Handler,

    /// <summary>`en_N` - pushes box N's data-ins just before it fires.</summary>
    Prologue,

    /// <summary>`ex_N` - copies box N's data-outs into graph variables.</summary>
    Epilogue,

    /// <summary>An empty stub the graph fires as `self:Pin()` - one of its control-outs.</summary>
    OutAnchor,

    /// <summary>One of the graph's own control-ins, fired by the parent graph or the engine.</summary>
    Entry,
}

/// <summary>A function's role, and for the generated ones the editor box ID they belong to.</summary>
public sealed record GraphFunction(string Name, FunctionRole Role, long? BoxId);

/// <summary>
/// Reads a function's role from its name. BlackBox names every generated function after the editor box it
/// serves - `f_2_Out`, `en_2`, `ex_2` all belong to box 2, pooled or not - and a debug twin spells the same
/// functions `f_box_Set_Entity_2_Out`, `OnEnter_box_Set_Entity_2`, `OnExit_box_Set_Entity_2`.
/// </summary>
public static partial class GraphFunctions
{
    public static IReadOnlyDictionary<string, GraphFunction> Classify(UserGraph graph)
    {
        var outAnchors = OutAnchorsOf(graph);
        var boxNames = TwinBoxNamesOf(graph);
        var byName = new Dictionary<string, GraphFunction>(StringComparer.Ordinal);
        foreach (UserGraphFunction fn in graph.Functions)
        {
            byName[fn.Name] = Classify(fn.Name, outAnchors.Contains(fn.Name) && fn.Body.Count == 0, boxNames);
        }
        return byName;
    }

    /// <param name="twinBoxNames">The `box_X_N` names a debug twin mentions, which settle where the box
    /// name ends in `f_box_Wait_2_seconds_5_Time_1_elapsed`.</param>
    public static GraphFunction Classify(string name, bool isOutAnchor, IReadOnlySet<string>? twinBoxNames = null)
    {
        if (name is "LuaDependencies" or "Create" or "Init" or "ShutDown")
        {
            return new(name, FunctionRole.Lifecycle, null);
        }
        if (isOutAnchor)
        {
            return new(name, FunctionRole.OutAnchor, null);
        }
        if (HandlerName().Match(name) is { Success: true } handler)
        {
            return new(name, FunctionRole.Handler, long.Parse(handler.Groups["id"].Value));
        }
        if (name.StartsWith("f_box_", StringComparison.Ordinal))
        {
            var splits = TwinBoxIdRun().Matches(name).ToList();
            Match? split = splits.FirstOrDefault(m => twinBoxNames?.Contains(name[2..(m.Index + m.Length)]) == true)
                ?? splits.LastOrDefault();
            if (split is not null)
            {
                return new(name, FunctionRole.Handler, long.Parse(split.Groups["id"].Value));
            }
        }
        if (HelperName().Match(name) is { Success: true } helper)
        {
            FunctionRole role = helper.Groups["kind"].Value is "en" or "OnEnter" ? FunctionRole.Prologue : FunctionRole.Epilogue;
            return new(name, role, long.Parse(helper.Groups["id"].Value));
        }
        return new(name, FunctionRole.Entry, null);
    }

    /// <summary>The control-outs a graph declares: `self.Pin = DummyFunction;` in `Init`, until the parent
    /// wires them.</summary>
    private static HashSet<string> OutAnchorsOf(UserGraph graph) =>
        graph.Functions
            .Where(fn => fn.Name == "Init")
            .SelectMany(fn => fn.Body)
            .OfType<SetGraphFieldStmt>()
            .Where(field => DominoNodeCatalog.IsDummyFunction(field.Value))
            .Select(field => field.FieldName)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Every `box_X_N` name a debug twin states: its named boxes and its traced connections.</summary>
    private static HashSet<string> TwinBoxNamesOf(UserGraph graph)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (UserGraphStmt stmt in graph.Functions.SelectMany(fn => fn.Body))
        {
            switch (stmt)
            {
                case CreateBoxStmt { Box: NamedInstanceBoxRef named }:
                    names.Add(named.FieldName);
                    break;
                case TraceConnectionStmt trace:
                    foreach (string label in (string[])[trace.SourcePinLabel, trace.TargetPinLabel])
                    {
                        if (DominoDebugTwin.SplitPinLabel(label).Box is { } box)
                        {
                            names.Add(box);
                        }
                    }
                    break;
            }
        }
        return names;
    }

    [GeneratedRegex(@"^f_(?<id>\d+)_")]
    private static partial Regex HandlerName();

    // Each place a twin handler name could split into `box_..._N` and a pin.
    [GeneratedRegex(@"(?<=.)_(?<id>\d+)(?=_.)")]
    private static partial Regex TwinBoxIdRun();

    [GeneratedRegex(@"^(?:(?<kind>en|ex)_|(?<kind>OnEnter|OnExit)_box_.+_)(?<id>\d+)$")]
    private static partial Regex HelperName();
}
