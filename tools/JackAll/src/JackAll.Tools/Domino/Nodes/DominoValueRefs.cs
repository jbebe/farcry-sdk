using System.Globalization;
using JackAll.Tools.Domino.Graphs;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Nodes;

/// <summary>What a box parameter's value refers to in the game.</summary>
public enum ValueRefKind
{
    Sound,
    SoundType,
    SoundMix,
    Animation,
    Texture,
    Graph,
    LocText,
    Entity,

    /// <summary>A PlayBark line: <see cref="ValueRef.Value"/> is the bank's mission tag and
    /// <see cref="ValueRef.Detail"/> the block (`GREET`).</summary>
    Bark,
    BarkBank,
    Weapon,
    MissionLayer,
    Message,
}

/// <summary>One parameter value that names something in the game.</summary>
/// <param name="Variable">The graph variable the value was read through, or null for a literal.</param>
public sealed record ValueRef(string Pin, ValueRefKind Kind, string Value, string? Detail = null, string? Variable = null)
{
    /// <summary>The value as a sound ID, for <see cref="ValueRefKind.Sound"/>.</summary>
    public uint? SoundId => Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && uint.TryParse(Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id) ? id : null;

    /// <summary>The value as a `disEntityId`, for <see cref="ValueRefKind.Entity"/>.</summary>
    public ulong? EntityId => ulong.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) ? id : null;
}

/// <summary>Finds the parameter values of a box that refer to game resources: by the pin's declared
/// type first, then by a table of known text pins, and for a sub-graph's untyped pins by what the
/// graph preloads.</summary>
public static class DominoValueRefs
{
    private static readonly Dictionary<string, ValueRefKind> ByPinType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nomad|Sound"] = ValueRefKind.Sound,
        ["Nomad|SoundType"] = ValueRefKind.SoundType,
        ["Nomad|SoundMixing"] = ValueRefKind.SoundMix,
        ["Nomad|animation"] = ValueRefKind.Animation,
        ["Nomad|texture"] = ValueRefKind.Texture,
        ["Nomad|entity"] = ValueRefKind.Entity,
        ["Core|boxclass"] = ValueRefKind.Graph,
    };

    /// <summary>Text pins whose value is a key rather than prose, by `box.pin` (box = script stem).</summary>
    private static readonly Dictionary<string, ValueRefKind> ByBoxPin = new(StringComparer.OrdinalIgnoreCase)
    {
        ["objectivestate.ObjectiveState"] = ValueRefKind.LocText,
        ["objectivepopup.Text"] = ValueRefKind.LocText,
        ["setmissionbarkbankstate.MissionTag"] = ValueRefKind.BarkBank,
        ["playbark.Mission"] = ValueRefKind.Bark,
        ["manageinventory.WeaponType"] = ValueRefKind.Weapon,
        ["setmissionstate.Mission"] = ValueRefKind.MissionLayer,
        ["broadcastmessage.Message"] = ValueRefKind.Message,
        ["messagelistener.Message"] = ValueRefKind.Message,
    };

    /// <summary>Pins whose value completes another pin's, which it carries as <see cref="ValueRef.Detail"/>.</summary>
    private static readonly Dictionary<string, string> DetailPins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["playbark.Mission"] = "Block",
    };

    public static IReadOnlyList<ValueRef> For(GraphNode node, ReconstructedGraph graph)
    {
        string box = NodeSignature.ShortNameFor(node.NodeTypePath);
        var refs = new List<ValueRef>();

        foreach ((string pin, ExpressionSyntax expr) in node.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (Resolve(expr, graph) is not var (value, variable) || value.Length == 0
                || KindOf(node, box, pin, value, graph) is not { } kind)
            {
                continue;
            }

            string? detail = DetailPins.TryGetValue($"{box}.{pin}", out string? detailPin)
                && node.Params.TryGetValue(detailPin, out var detailExpr)
                    ? Resolve(detailExpr, graph)?.Value
                    : null;
            var found = new ValueRef(pin, kind, value, detail, variable);
            if (IsWellFormed(found))
            {
                refs.Add(found);
            }
        }
        return refs;
    }

    /// <summary>The graph variable an expression reads (`self.X` → `X`), or null.</summary>
    public static string? VariableOf(ExpressionSyntax expr) =>
        expr is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" } } member
            ? member.MemberName.Text
            : null;

    /// <summary>The value a parameter holds when the graph starts: a literal, or a graph variable's
    /// `Init()` value. Null when it is only known while the graph runs.</summary>
    public static (string Value, string? Variable)? Resolve(ExpressionSyntax expr, ReconstructedGraph graph)
    {
        if (expr is LiteralExpressionSyntax lit)
        {
            return lit.Kind() == SyntaxKind.NilLiteralExpression ? null : (lit.Token.ValueText, null);
        }
        if (VariableOf(expr) is { } variable && graph.VariableDefaults.TryGetValue(variable, out string? init))
        {
            return (init.Length >= 2 && init[0] == '"' && init[^1] == '"' ? init[1..^1] : init, variable);
        }
        return null;
    }

    private static ValueRefKind? KindOf(GraphNode node, string box, string pin, string value, ReconstructedGraph graph)
    {
        string? type = node.Signature?.DataIns.FirstOrDefault(p => p.Name == pin)?.Type;
        if (type is not null && ByPinType.TryGetValue(type, out ValueRefKind byType))
        {
            return byType;
        }
        if (ByBoxPin.TryGetValue($"{box}.{pin}", out ValueRefKind byName))
        {
            return byName;
        }
        if (pin.EndsWith("LocId", StringComparison.Ordinal))
        {
            return ValueRefKind.LocText;
        }
        if (pin == "Layer" || pin.StartsWith("BriefingLayer", StringComparison.Ordinal))
        {
            return ValueRefKind.MissionLayer;
        }
        if (type is not null)
        {
            return null;
        }

        // An untyped (sub-graph) pin: the graph preloads every sound and animation it hands to one.
        foreach ((string name, string resourceType) in graph.LoadedResources)
        {
            if (resourceType == "CSoundResource" && name.EndsWith(value, StringComparison.OrdinalIgnoreCase)
                && name.Length == value.Length + "sndres".Length)
            {
                return ValueRefKind.Sound;
            }
            if (resourceType == "CMovementResource" && name.Equals(value, StringComparison.Ordinal))
            {
                return ValueRefKind.Animation;
            }
        }
        return null;
    }

    private static bool IsWellFormed(ValueRef value) => value.Kind switch
    {
        ValueRefKind.Sound => value.SoundId is not null,
        ValueRefKind.Entity => value.EntityId is > 0,
        ValueRefKind.SoundType => int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        _ => true,
    };
}
