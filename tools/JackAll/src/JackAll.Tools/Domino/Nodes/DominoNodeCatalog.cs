using JackAll.Tools.Domino.Graphs;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Nodes;

/// <summary>
/// Resolves a box's node-type path (`"Domino/System/Delay.lua"`) to the pin interface a viewer needs to
/// draw ports. Backed by a caller-supplied path reader rather than a bundled data file: every one of the
/// 1072 `domino\` paths is in the game's own name dictionary, so the app can read them straight out of
/// the mounted VFS. That keeps Ubisoft's script text out of this repo and out of the shipped exe, and
/// means a mod that edits or adds a `system\` node is picked up for free.
///
/// Results are cached per path, including negative ones - a graph fires the same handful of node types
/// over and over (232 boxes across ~30 distinct types in the largest graph), so without caching a single
/// open would re-parse the same file dozens of times.
///
/// Not thread-safe; construct one per open graph or guard it externally.
/// </summary>
public sealed class DominoNodeCatalog
{
    private readonly Func<string, string?> _readLuaByPath;
    private readonly Dictionary<string, NodeSignature?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="readLuaByPath">Returns a Domino script's source text for a game-relative path
    /// (`domino\system\delay.lua`), or null when it can't be found. The app backs this with the VFS;
    /// tests back it with a fixture folder.</param>
    public DominoNodeCatalog(Func<string, string?> readLuaByPath) => _readLuaByPath = readLuaByPath;

    /// <summary>Returns null when the referenced script can't be read or doesn't parse - a graph that
    /// names a node type this install doesn't have still opens, just without ports on that box.</summary>
    public NodeSignature? Resolve(string nodeTypePath)
    {
        if (_cache.TryGetValue(nodeTypePath, out NodeSignature? cached))
        {
            return cached;
        }

        NodeSignature? signature = Load(nodeTypePath);
        _cache[nodeTypePath] = signature;
        return signature;
    }

    /// <summary>Turns a Lua-side node-type path into the game-relative VFS path that names the same
    /// file: forward slashes become backslashes and the whole thing lowercases, matching how the name
    /// dictionary stores it.</summary>
    public static string ToVfsPath(string nodeTypePath) =>
        nodeTypePath.Replace('/', '\\').ToLowerInvariant();

    private NodeSignature? Load(string nodeTypePath)
    {
        string? source = _readLuaByPath(ToVfsPath(nodeTypePath));
        if (source is null)
        {
            return null;
        }

        CompilationUnitSyntax root;
        try
        {
            root = DominoLuaSource.Parse(source);
        }
        catch (FormatException)
        {
            return null;
        }

        // A `system\` node declares itself; a `user\` sub-graph has to be read.
        NodeReflection? reflection = TryParseReflection(root);
        return reflection is not null
            ? NodeSignature.FromReflection(nodeTypePath, reflection)
            : InferSubGraphSignature(nodeTypePath, root);
    }

    private static NodeReflection? TryParseReflection(CompilationUnitSyntax root)
    {
        try
        {
            return ReflectionBoxParser.Parse(root);
        }
        catch (FormatException)
        {
            // An unrecognized line inside the header means we can't trust the pin list we'd build from
            // it. Fall through to inference rather than reporting a half-parsed signature as declared.
            return null;
        }
    }

    /// <summary>
    /// Recovers a `user\` sub-graph's interface from its generated code, since sub-graphs carry no
    /// reflection header. The two control-pin rules are structural, so they're exact:
    ///
    /// <list type="bullet">
    /// <item>A control-out is a `self.Name = DummyFunction;` in `Init()` - the graph's own out anchor,
    /// left unbound for a parent to overwrite. BlackBox also emits a matching empty
    /// `function export:Name() end` ("Empty out anchor definitions"), which is why those functions must
    /// not be counted as control-ins.</item>
    /// <item>A control-in is any other exported function that isn't lifecycle boilerplate
    /// (`Create`/`Init`/`ShutDown`/`LuaDependencies`) or generated internals (`f_*`, `en_N`, `ex_N`,
    /// `OnEnter_*`, `OnExit_*`).</item>
    /// </list>
    ///
    /// Data pins are best-effort and untyped: a graph field that is only ever read is an input, and one
    /// assigned from some box's data-out is a candidate output. Nothing in the generated code records a
    /// sub-graph's declared data types, so <see cref="DataInPin.Type"/> is reported as
    /// <see cref="UnknownType"/> rather than guessed.
    /// </summary>
    private static NodeSignature InferSubGraphSignature(string nodeTypePath, CompilationUnitSyntax root)
    {
        UserGraph graph;
        try
        {
            graph = UserGraphParser.Parse(root);
        }
        catch (Exception)
        {
            return EmptySignature(nodeTypePath);
        }

        var controlOuts = new List<string>();
        var producedFields = new HashSet<string>(StringComparer.Ordinal);
        var readFields = new HashSet<string>(StringComparer.Ordinal);
        var declaredFields = new List<string>();

        foreach (UserGraphFunction fn in graph.Functions)
        {
            foreach (UserGraphStmt stmt in fn.Body)
            {
                switch (stmt)
                {
                    case SetGraphFieldStmt { FieldName: var field, Value: var value }:
                        if (IsDummyFunction(value))
                        {
                            if (!controlOuts.Contains(field, StringComparer.Ordinal))
                            {
                                controlOuts.Add(field);
                            }
                        }
                        else if (!field.StartsWith("box_", StringComparison.Ordinal) && !declaredFields.Contains(field, StringComparer.Ordinal))
                        {
                            declaredFields.Add(field);
                        }
                        break;

                    // `self.Field = self[N].Pin;` - produced internally, so not an input.
                    case ReadDataStmt { Target: var target } when GraphFieldName(target) is { } produced:
                        producedFields.Add(produced);
                        break;

                    // `self[N].Param = self.Field;` - the graph reading one of its own fields.
                    case SetParamStmt { Value: var paramValue } when GraphFieldName(paramValue) is { } read:
                        readFields.Add(read);
                        break;
                }
            }
        }

        // Anything read but never produced in this graph has to arrive from the parent.
        var dataIns = readFields
            .Where(f => !producedFields.Contains(f) && !controlOuts.Contains(f, StringComparer.Ordinal))
            .Concat(declaredFields.Where(f => !producedFields.Contains(f) && !readFields.Contains(f) && !controlOuts.Contains(f, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new DataInPin(f, UnknownType))
            .ToList();

        var dataOuts = producedFields
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new DataOutPin(f, UnknownType))
            .ToList();

        var controlIns = graph.Functions
            .Select(fn => fn.Name)
            .Where(name => !IsLifecycle(name) && !IsGenerated(name) && !controlOuts.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(name => new ControlInPin(name, Dynamic: false))
            .ToList();

        return new NodeSignature(
            nodeTypePath,
            NodeSignature.ShortNameFor(nodeTypePath),
            SubGraphCategory,
            controlIns,
            controlOuts.Select(name => new ControlOutPin(name, Delayed: false, Dynamic: false)).ToList(),
            dataIns,
            dataOuts,
            Stateless: false,
            SignatureOrigin.Inferred);
    }

    /// <summary>Stands in for a data pin's declared type where none is recoverable - a sub-graph's
    /// generated code never restates the types its parent document declared.</summary>
    public const string UnknownType = "?";

    /// <summary>The pseudo-category inferred sub-graph nodes are filed under, so the viewer can group
    /// them alongside the 15 real <c>&lt;Display Category="..."/&gt;</c> values.</summary>
    public const string SubGraphCategory = "Sub-graph";

    private static NodeSignature EmptySignature(string nodeTypePath) => new(
        nodeTypePath, NodeSignature.ShortNameFor(nodeTypePath), SubGraphCategory,
        [], [], [], [], Stateless: false, SignatureOrigin.Inferred);

    private static bool IsLifecycle(string name) =>
        name is "Create" or "Init" or "ShutDown" or "LuaDependencies";

    private static bool IsGenerated(string name) =>
        name.StartsWith("f_", StringComparison.Ordinal)
        || name.StartsWith("OnEnter_", StringComparison.Ordinal)
        || name.StartsWith("OnExit_", StringComparison.Ordinal)
        || IsIndexedHelper(name, "en_")
        || IsIndexedHelper(name, "ex_");

    /// <summary>Matches `en_12`/`ex_3` but not a real pin that merely starts with those letters - the
    /// suffix must be all digits.</summary>
    private static bool IsIndexedHelper(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.Ordinal)
        && name.Length > prefix.Length
        && name.AsSpan(prefix.Length).ToString().All(char.IsAsciiDigit);

    private static bool IsDummyFunction(ExpressionSyntax expr) =>
        expr is IdentifierNameSyntax { Name: "DummyFunction" };

    /// <summary>Recognizes `self.FieldName` (and nothing else), returning the field name.</summary>
    internal static string? GraphFieldName(ExpressionSyntax expr) =>
        expr is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Name: "self" },
            MemberName.Text: var field,
        } && !field.StartsWith("box_", StringComparison.Ordinal)
            ? field
            : null;
}
