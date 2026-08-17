using JackAll.Tools.World;

namespace JackAll.App.Library;

/// <summary>
/// One row in the Library tab's archetype tree: either a namespace group or a leaf archetype. The
/// engine keys archetypes on a flat dotted name (<c>Animals.Quadrupeds.CapeBuffalo</c>); this splits
/// that name back into the groups it reads as, so thousands of archetypes stay browsable.
/// </summary>
public sealed class ArchetypeTreeNode : TreeNodeBase<ArchetypeTreeNode>
{
    private ArchetypeTreeNode(string label, string? fullName)
    {
        Label = label;
        FullName = fullName;
    }

    public string Label { get; }

    /// <summary>The engine's key for this archetype, or null for a group row.</summary>
    public string? FullName { get; }

    /// <summary>The chain of layers declaring this archetype, in load order - null for a group row.</summary>
    public string? Chain { get; private set; }

    /// <summary>True when a later library declares this name too, so the earlier copies are dead.</summary>
    public bool IsShadowed { get; private set; }

    /// <summary>True for a group holding any shadowed archetype, so a collapsed branch still shows there
    /// is something contested inside it.</summary>
    public bool ContainsShadowed { get; private set; }

    /// <summary>Groups every name in <paramref name="index"/> per <see cref="ArchetypeIndex.SplitForDisplay"/>.</summary>
    public static ArchetypeTreeNode Build(ArchetypeIndex index)
    {
        var root = new ArchetypeTreeNode("Archetypes", null);
        foreach (string name in index.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyList<ArchetypeDefinition> chain = index.DefinitionsOf(name);
            (IReadOnlyList<string> groups, string label) = index.SplitForDisplay(name);

            ArchetypeTreeNode parent = root;
            foreach (string group in groups)
            {
                parent = parent.GroupFor(group);
            }

            var leaf = new ArchetypeTreeNode(label, name)
            {
                IsShadowed = chain.Count > 1,
                Chain = string.Join(" → ", chain.Select(d => d.Layer.ShortName)),
            };
            parent.AddChild(leaf);

            if (leaf.IsShadowed)
            {
                for (ArchetypeTreeNode? node = leaf; node is not null; node = node.Parent)
                {
                    node.ContainsShadowed = true;
                }
            }
        }
        return root;
    }

    private ArchetypeTreeNode GroupFor(string label)
    {
        foreach (ArchetypeTreeNode child in Children)
        {
            if (child.FullName is null && child.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        var group = new ArchetypeTreeNode(label, null);
        AddChild(group);
        return group;
    }

    /// <summary>Shows only archetypes matching the search text, and optionally only shadowed ones.</summary>
    public static void ApplyFilter(ArchetypeTreeNode node, string filter, bool shadowedOnly)
        => ApplyFilter(node, n =>
            n.FullName is not null
            && (filter.Length == 0 || n.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            && (!shadowedOnly || n.IsShadowed));

    /// <summary>Expands the path down to <paramref name="fullName"/> and selects it.</summary>
    public static ArchetypeTreeNode? Reveal(ArchetypeTreeNode node, string fullName)
        => Reveal(node, n => n.FullName is { } own && own.Equals(fullName, StringComparison.OrdinalIgnoreCase));
}
