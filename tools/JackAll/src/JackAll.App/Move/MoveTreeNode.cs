using System.Globalization;
using JackAll.Core.Format.Move;

namespace JackAll.App.Move;

/// <summary>One field of the selected object, as the detail grid shows it.</summary>
public sealed record MoveFieldRow(string Kind, string Name, string Value, string Note);

/// <summary>
/// One object in the MOVE graph's ownership tree. The file is a flat stream, but every object is
/// owned by exactly one pointer in another, so it reads back as a tree; a back-reference is shown
/// as a leaf rather than followed, which is what keeps the tree finite.
/// </summary>
public sealed class MoveTreeNode : TreeNodeBase<MoveTreeNode>
{
    private static readonly string[] StateClasses =
        ["CMoveState", "CLayeredState", "CSyncState", "CFrankensteinState"];

    private MoveTreeNode(MoveObject target, string label, string detail)
    {
        Target = target;
        Label = label;
        Detail = detail;
    }

    public MoveObject Target { get; }

    public string Label { get; }

    /// <summary>The one thing worth reading at a glance: what a criterion tests, a state's hash.</summary>
    public string Detail { get; }

    public static MoveTreeNode Build(MoveFile file, IReadOnlyList<MoveChannel>? channels)
    {
        MoveObject root = file.Root.Ops
            .Where(op => op.Kind == MoveOpKind.PointerNew)
            .Select(op => op.Target!)
            .FirstOrDefault() ?? throw new MoveFormatException("this graph has no root object");

        return BuildNode(root, channels);
    }

    private static MoveTreeNode BuildNode(MoveObject target, IReadOnlyList<MoveChannel>? channels)
    {
        MoveTreeNode node = new(target, target.ClassName, Describe(target, channels));
        foreach (MoveOp op in target.Ops)
        {
            if (op.Kind == MoveOpKind.PointerNew)
            {
                node.AddChild(BuildNode(op.Target!, channels));
            }
        }

        return node;
    }

    /// <summary>Every field of one object, with the channel and enum names filled in.</summary>
    public static IReadOnlyList<MoveFieldRow> Fields(
        MoveObject target, IReadOnlyList<MoveChannel>? channels)
    {
        uint? channel = target.Field("m_eValueID");
        List<MoveFieldRow> rows = [];
        foreach (MoveOp op in target.Ops)
        {
            rows.Add(new MoveFieldRow(
                op.Kind.ToString(), op.Name, Value(op), Note(op, channels, channel)));
        }

        return rows;
    }

    private static string Value(MoveOp op) => op.Kind switch
    {
        MoveOpKind.U8 or MoveOpKind.U32 or MoveOpKind.Version =>
            op.Number.ToString(CultureInfo.InvariantCulture),
        MoveOpKind.S32 => unchecked((int)op.Number).ToString(CultureInfo.InvariantCulture),
        MoveOpKind.NoVersion => "(absent)",
        MoveOpKind.F32 => BitConverter.ToSingle(op.Bytes!).ToString("R", CultureInfo.InvariantCulture),
        MoveOpKind.Str => MoveText.Printable(op.Bytes!) ?? $"{op.Bytes!.Length} non-text bytes",
        MoveOpKind.Data or MoveOpKind.Raw => $"{op.Bytes!.Length} bytes",
        MoveOpKind.PointerNew => $"-> {op.Target!.ClassName} #{op.Target.Index}",
        MoveOpKind.PointerRef => $"ref #{op.Target!.Index} ({op.Target.ClassName})",
        _ => "null",
    };

    private static string Note(
        MoveOp op, IReadOnlyList<MoveChannel>? channels, uint? channel)
    {
        if (channels is null)
        {
            return string.Empty;
        }

        if (op.Name == "m_eValueID" && op.Number < channels.Count)
        {
            return channels[(int)op.Number].Name;
        }

        if (op.Name != "m_Value" || channel is not { } id || id >= channels.Count)
        {
            return string.Empty;
        }

        IReadOnlyList<string>? values = channels[(int)id].Values;
        int index = unchecked((int)op.Number);
        return values is not null && index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    private static string Describe(MoveObject target, IReadOnlyList<MoveChannel>? channels)
    {
        if (target.ClassName.Contains("Criteria", StringComparison.Ordinal))
        {
            return DescribeCriterion(target, channels);
        }

        if (StateClasses.Contains(target.ClassName) && target.Field("m_stateNameHash") is { } hash)
        {
            string parent = target.Field("aliasID") is { } alias && alias != 0xFFFFFFFF
                ? $" -> parent 0x{alias:X8}"
                : string.Empty;
            return $"0x{hash:X8}{parent}";
        }

        return target.ClassName switch
        {
            "CMoveStateMachine" => $"{target.Field("nbState") ?? 0} states",
            "CMoveValueContainer" => $"{target.Field("ms_iNumMoveValue") ?? 0} channels",
            _ => string.Empty,
        };
    }

    private static string DescribeCriterion(MoveObject target, IReadOnlyList<MoveChannel>? channels)
    {
        if (target.Field("m_eValueID") is not { } id)
        {
            return string.Empty;
        }

        string name = channels is not null && id < channels.Count
            ? channels[(int)id].Name
            : $"channel {id}";
        if (target.Field("m_Value") is not { } raw)
        {
            return name;
        }

        int value = unchecked((int)raw);
        IReadOnlyList<string>? values =
            channels is not null && id < channels.Count ? channels[(int)id].Values : null;
        string shown = values is not null && value >= 0 && value < values.Count
            ? values[value]
            : value.ToString(CultureInfo.InvariantCulture);
        string op = target.ClassName.Contains("NotEqual", StringComparison.Ordinal) ? "!=" : "==";
        return $"{name} {op} {shown}";
    }

    public static void Filter(MoveTreeNode root, string query) =>
        ApplyFilter(root, node =>
            query.Length == 0
            || node.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Detail.Contains(query, StringComparison.OrdinalIgnoreCase));
}
