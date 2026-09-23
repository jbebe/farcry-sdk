using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>The dropdown each <c>selXxx</c> field gets, and the <c>enumXxx</c> groups behind them,
/// which show as the dropdown rather than as nodes of their own.</summary>
internal sealed record EnumChoices(Dictionary<uint, IReadOnlyList<string>> Choices, HashSet<string> Groups)
{
    private static readonly uint ValueFieldHash = FcbClassDefinitions.Crc32Ascii("Value");

    /// <summary>
    /// The engine's labels for each registered enum member, replaced by the data's own convention
    /// where either side has it: a UInt32 <c>selType</c> beside an <c>enumType</c> child whose
    /// <c>enum</c> children each hold one String <c>Value</c>, in index order.
    /// </summary>
    public static EnumChoices Of(MergedNode node, FcbClass cls)
    {
        var found = new EnumChoices([], []);
        foreach ((uint hash, FcbMember member) in cls.AllMembers())
        {
            if (member.Labels is { Count: > 0 } labels)
            {
                found.Choices[hash] = labels;
            }
        }
        foreach (FcbObject side in new[] { node.Base, node.Instance }.OfType<FcbObject>())
        {
            found.AddDataGroups(side, cls);
        }
        return found;
    }

    private void AddDataGroups(FcbObject obj, FcbClass cls)
    {
        foreach (uint hash in obj.Values.Keys)
        {
            if (cls.FindMember(hash) is not { Name: { } name, Type: FcbMemberType.UInt32 } member
                || !member.HasPrefix("sel"))
            {
                continue;
            }

            string groupName = "enum" + name[3..];
            if (obj.Children.FirstOrDefault(c => cls.Resolve(c.TypeHash).Name == groupName) is not { } group)
            {
                continue;
            }

            List<string> names = [];
            foreach (FcbObject entry in group.Children)
            {
                if (entry.Values.TryGetValue(ValueFieldHash, out byte[]? bytes)
                    && FcbValueCodec.TryDecode(FcbMemberType.String, bytes, out object decoded))
                {
                    names.Add((string)decoded);
                }
            }

            // An empty group leaves the plain integer field rather than an empty dropdown.
            if (names.Count > 0)
            {
                Choices[hash] = names;
                Groups.Add(groupName);
            }
        }
    }
}

/// <summary>A merged node, its counterpart in the document's baseline, and the class it resolves to.</summary>
internal sealed record ScopedNode(MergedNode Node, MergedNode? Baseline, FcbClass Class)
{
    private EnumChoices? _enums;

    private EnumChoices Enums => _enums ??= EnumChoices.Of(Node, Class);

    public IReadOnlyList<FieldView> Fields(IEnumerable<MergedField> fields, FcbEditContext context)
        => [.. fields.Select(f => FieldView.Bound(
            Node, f, Class, Baseline?.ValueOf(f.Hash), Enums.Choices.GetValueOrDefault(f.Hash), context))];

    /// <summary>The children, each paired with its baseline by tag in order; enum groups left out.</summary>
    public IEnumerable<ScopedNode> Children()
    {
        IReadOnlyList<MergedNode> children = Node.Children;
        int[]? partners = Baseline is null ? null : MergedNode.PairByTag(children, Baseline.Children, c => c.TypeHash);
        for (int i = 0; i < children.Count; i++)
        {
            FcbClass cls = Class.Resolve(children[i].Shown);
            if (!Enums.Groups.Contains(cls.Name ?? ""))
            {
                yield return new ScopedNode(
                    children[i], partners is null || partners[i] < 0 ? null : Baseline!.Children[partners[i]], cls);
            }
        }
    }
}

/// <summary>The class-scoped walk every view over an <see cref="FcbObject"/> tree shares.</summary>
internal static class FcbNodeViews
{
    /// <summary>The recursive key/value rows of <paramref name="scope"/>: its fields, then every
    /// child node <paramref name="skip"/> leaves in, however deep.</summary>
    public static IReadOnlyList<object> KeyValueItems(ScopedNode scope, FcbEditContext context, Func<FcbObject, bool> skip)
        => [.. scope.Fields(scope.Node.Fields, context), .. KeyValueChildren(scope, context, skip)];

    private static IReadOnlyList<NodeView> KeyValueChildren(ScopedNode scope, FcbEditContext context, Func<FcbObject, bool> skip)
        => [.. scope.Children()
            .Where(c => !skip(c.Node.Shown))
            .Select(c => new NodeView(
                Label(c.Node.Shown, c.Class, context),
                () => c.Fields(c.Node.Fields, context),
                () => KeyValueChildren(c, context, skip)))];

    /// <summary>A node's class name - or its hash - and, when it has one, the text that tells it
    /// apart from its siblings.</summary>
    public static string Label(FcbObject obj, FcbClass cls, FcbEditContext context)
        => (context.ClassName(obj.TypeHash, cls), FindIdentifyingText(obj, cls)) switch
        {
            ({ } name, { Length: > 0 } text) => $"{name} - {text}",
            ({ } name, _) => name,
            (null, { Length: > 0 } text) => $"{text} ({obj.TypeHash:X8})",
            (null, _) => $"hash {obj.TypeHash:X8}",
        };

    /// <summary>A literal <c>Name</c> field, else the first String field in file order.</summary>
    private static string? FindIdentifyingText(FcbObject obj, FcbClass cls)
    {
        if (obj.Values.TryGetValue(WorldHashes.Name, out byte[]? nameBytes)
            && FcbValueCodec.TryDecode(FcbMemberType.String, nameBytes, out object name))
        {
            return (string)name;
        }

        foreach ((uint hash, byte[] bytes) in obj.Values)
        {
            if (cls.FindMember(hash)?.Type == FcbMemberType.String
                && FcbValueCodec.TryDecode(FcbMemberType.String, bytes, out object text)
                && ((string)text).Length > 0)
            {
                return (string)text;
            }
        }
        return null;
    }
}
