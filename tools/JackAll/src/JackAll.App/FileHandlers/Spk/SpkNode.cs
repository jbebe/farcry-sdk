using JackAll.Core.Vfs;
using JackAll.Tools.Spk;

namespace JackAll.App.FileHandlers.Spk;

/// <summary>A bank record in the tree under whatever plays it, or an id that lives in another bank.</summary>
public sealed class SpkNode : TreeNodeBase<SpkNode>
{
    public required string Label { get; init; }

    public required string Detail { get; init; }

    /// <summary>Null for an id this bank does not hold.</summary>
    public SpkBankRecord? Record { get; init; }

    /// <summary>The bank file an outside id resolves to, when the loaded game has it.</summary>
    public VfsFile? External { get; init; }

    public void Add(SpkNode child) => AddChild(child);

    /// <summary>The name UI Automation and screen readers give the row.</summary>
    public override string ToString() => $"{Label} {Detail}";

    /// <summary>Selects the first node for record <paramref name="id"/>, expanding the way down to it.</summary>
    public static SpkNode? Select(IEnumerable<SpkNode> roots, uint id) =>
        roots.Select(root => Reveal(root, n => n.Record?.Id == id)).FirstOrDefault(n => n is not null);
}
