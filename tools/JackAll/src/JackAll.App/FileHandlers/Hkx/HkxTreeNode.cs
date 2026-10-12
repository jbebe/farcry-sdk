using JackAll.Tools.Hkx;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>A row of the HKX editor's tree: a variant, body, shape or constraint.</summary>
public sealed class HkxTreeNode : TreeNodeBase<HkxTreeNode>
{
    private bool _isShown;

    public HkxTreeNode(HkxNode node)
    {
        Node = node;
        // A twin overlaps the body it copies.
        _isShown = IsBody && !node.IsTwin;
        foreach (HkxNode child in node.Children)
        {
            AddChild(new HkxTreeNode(child));
        }
    }

    public HkxNode Node { get; }

    public bool IsBody => Node.Kind == HkxNodeKind.Body;

    /// <summary>Whether this body's collision is drawn.</summary>
    public bool IsShown
    {
        get => _isShown;
        set => Set(ref _isShown, value);
    }

    /// <summary>Selects the first node that <paramref name="matches"/> under any of the roots, expanding the way to it.</summary>
    public static HkxTreeNode? Reveal(IEnumerable<HkxTreeNode> roots, Func<HkxTreeNode, bool> matches)
        => roots.Select(r => Reveal(r, matches)).FirstOrDefault(n => n is not null);

    public IEnumerable<HkxTreeNode> Bodies()
        => (IsBody ? new[] { this } : []).Concat(Children.SelectMany(c => c.Bodies()));
}
