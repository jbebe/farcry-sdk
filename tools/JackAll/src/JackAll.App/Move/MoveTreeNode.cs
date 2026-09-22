using System.Globalization;
using JackAll.Core.Format.Move;
using JackAll.Core.Format.Move.Rules;

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

    /// <summary>One object and everything it owns.</summary>
    public static MoveTreeNode Build(MoveObject target, MoveChannels channels)
    {
        MoveTreeNode node = new(target, target.ClassName, Describe(target, channels));
        foreach (MoveOp op in target.Ops)
        {
            if (op.Kind == MoveOpKind.PointerNew)
            {
                node.AddChild(Build(op.Target!, channels));
            }
        }

        return node;
    }

    /// <summary>Every field of one object, with the channel and enum names filled in.</summary>
    public static IReadOnlyList<MoveFieldRow> Fields(MoveObject target, MoveChannels channels)
    {
        int? channel = (int?)target.Field("m_eValueID");
        return
        [
            .. target.Ops.Select(op => new MoveFieldRow(
                op.Kind.ToString(), op.Name, Value(op), Note(op, channels, channel))),
        ];
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

    private static string Note(MoveOp op, MoveChannels channels, int? channel) => op.Name switch
    {
        "m_eValueID" => channels.NameOf((int)op.Number),
        "m_Value" when channel is { } id => channels.Format(id, unchecked((int)op.Number)),
        _ => string.Empty,
    };

    private static string Describe(MoveObject target, MoveChannels channels)
    {
        if (target.ClassName.Contains("Criteria", StringComparison.Ordinal))
        {
            return new MoveCondition(target).Describe(channels);
        }

        if (MoveStateIndex.NameHashOf(target) is { } hash)
        {
            string parent = target.Field("aliasID") is { } alias && alias != 0xFFFFFFFF
                ? $" -> parent 0x{alias:X8}"
                : string.Empty;
            return $"0x{hash:X8}{parent}";
        }

        return string.Empty;
    }

    public static void Filter(MoveTreeNode root, string query) =>
        ApplyFilter(root, node =>
            query.Length == 0
            || node.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Detail.Contains(query, StringComparison.OrdinalIgnoreCase));
}
