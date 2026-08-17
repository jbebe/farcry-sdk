using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using JackAll.Tools.World;

namespace JackAll.App.Library;

/// <summary>
/// One row in the Library tab's archetype tree: either a namespace group or a leaf archetype. The
/// engine keys archetypes on a flat dotted name (<c>Animals.Quadrupeds.CapeBuffalo</c>); this splits
/// that name back into the groups it reads as, so 1,400 archetypes are browsable.
/// </summary>
public sealed class ArchetypeTreeNode : INotifyPropertyChanged
{
    private ArchetypeTreeNode? _parent;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isVisible = true;

    private ArchetypeTreeNode(string label, string? fullName)
    {
        Label = label;
        FullName = fullName;
    }

    public string Label { get; }

    /// <summary>The engine's key for this archetype, or null for a group row.</summary>
    public string? FullName { get; }

    public ObservableCollection<ArchetypeTreeNode> Children { get; } = [];

    /// <summary>The chain of layers declaring this archetype, in load order - null for a group row.</summary>
    public string? Chain { get; private set; }

    /// <summary>True when a later library declares this name too, so the earlier copies are dead.</summary>
    public bool IsShadowed { get; private set; }

    /// <summary>True for a group holding any shadowed archetype, so a collapsed branch still shows there
    /// is something contested inside it.</summary>
    public bool ContainsShadowed { get; private set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible == value) return; _isVisible = value; OnPropertyChanged(); }
    }

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
                _parent = parent,
                IsShadowed = chain.Count > 1,
                Chain = string.Join(" → ", chain.Select(d => d.Layer.ShortName)),
            };
            parent.Children.Add(leaf);

            if (leaf.IsShadowed)
            {
                for (ArchetypeTreeNode? node = leaf; node is not null; node = node._parent)
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

        var group = new ArchetypeTreeNode(label, null) { _parent = this };
        Children.Add(group);
        return group;
    }

    /// <summary>Hides rows that match neither the search text nor the shadowed-only filter, keeping any
    /// group that still has a visible descendant.</summary>
    public static bool ApplyFilter(ArchetypeTreeNode node, string filter, bool shadowedOnly)
    {
        bool anyChildVisible = false;
        foreach (ArchetypeTreeNode child in node.Children)
        {
            anyChildVisible |= ApplyFilter(child, filter, shadowedOnly);
        }

        bool selfMatches =
            node.FullName is not null
            && (filter.Length == 0 || node.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            && (!shadowedOnly || node.IsShadowed);

        node.IsVisible = selfMatches || anyChildVisible;
        return node.IsVisible;
    }

    /// <summary>Expands the path down to <paramref name="fullName"/> and selects it.</summary>
    public static ArchetypeTreeNode? Reveal(ArchetypeTreeNode node, string fullName)
    {
        if (node.FullName is { } own && own.Equals(fullName, StringComparison.OrdinalIgnoreCase))
        {
            node.IsSelected = true;
            return node;
        }
        foreach (ArchetypeTreeNode child in node.Children)
        {
            if (Reveal(child, fullName) is { } found)
            {
                node.IsExpanded = true;
                return found;
            }
        }
        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
