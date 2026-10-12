using JackAll.Tools.Hkx;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>A row of the HKX editor's tree: a variant, body, shape or constraint.</summary>
public sealed class HkxTreeNode : TreeNodeBase<HkxTreeNode>
{
    private bool _isShown;

    public HkxTreeNode(HkxNode node)
    {
        Node = node;
        IsBody = node.Object?.Class.Name == "hkpRigidBody";
        // The game's high-detail and navigation twins overlap the body they copy.
        _isShown = IsBody && !node.Label.StartsWith("HIGH_", StringComparison.Ordinal) && !node.Label.StartsWith("NAV_", StringComparison.Ordinal);
        foreach (HkxNode child in node.Children)
        {
            AddChild(new HkxTreeNode(child));
        }
    }

    public HkxNode Node { get; }

    public string Label => Node.Label;

    public bool IsBody { get; }

    /// <summary>Whether this body's collision is drawn.</summary>
    public bool IsShown
    {
        get => _isShown;
        set => Set(ref _isShown, value);
    }

    /// <summary>Selects the first node under this one that <paramref name="matches"/>, expanding the way to it.</summary>
    public HkxTreeNode? RevealWhere(Func<HkxTreeNode, bool> matches) => Reveal(this, matches);

    public IEnumerable<HkxTreeNode> Bodies()
        => (IsBody ? new[] { this } : []).Concat(Children.SelectMany(c => c.Bodies()));
}
