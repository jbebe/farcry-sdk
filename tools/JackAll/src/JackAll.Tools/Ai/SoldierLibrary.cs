using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tools.Ai;

/// <summary>
/// One world's copy of a soldier archetype: its prototype fragment, being edited, beside the base
/// game's values for every <see cref="SoldierFields"/> tunable.
/// </summary>
public sealed class SoldierCopy
{
    private readonly FcbObject _vanillaRoot;
    private readonly Dictionary<SoldierField, double?> _vanilla;

    internal SoldierCopy(ArchetypeDefinition definition, FcbObject root, FcbObject entity, FcbObject vanillaRoot, FcbObject vanillaEntity)
    {
        Definition = definition;
        Root = root;
        Entity = entity;
        _vanillaRoot = vanillaRoot;
        _vanilla = SoldierFields.All.ToDictionary(f => f, f => f.Read(vanillaEntity));
    }

    public ArchetypeDefinition Definition { get; }

    /// <summary>The whole prototype - the fragment that is staged.</summary>
    public FcbObject Root { get; }

    public FcbObject Entity { get; }

    public double? Vanilla(SoldierField field) => _vanilla.GetValueOrDefault(field);

    public bool IsEdited => SoldierFields.All.Any(f => f.Read(Entity) != Vanilla(f));

    /// <summary>The fragment to stage, and whether it is back to the base game's text.</summary>
    public (string FragmentId, string Xml, bool IsVanilla) Plan(FcbClassDefinitions definitions)
    {
        string xml = FcbXml.ToXml(Root, definitions);
        return (Definition.FragmentId!, xml, xml == FcbXml.ToXml(_vanillaRoot, definitions));
    }
}

public static class SoldierLibrary
{
    /// <summary>
    /// The soldiers' prototypes out of one entity library, decoded once. <paramref name="original"/> is
    /// the base game's library; null or identical means the base game is what <paramref name="merged"/> holds.
    /// </summary>
    public static IReadOnlyList<SoldierCopy> Open(IEnumerable<ArchetypeDefinition> soldiers, byte[] merged, byte[]? original)
    {
        Dictionary<string, FcbObject> prototypes = ById(FcbDocument.Deserialize(merged));
        Dictionary<string, FcbObject>? vanillaPrototypes = original is null || original.AsSpan().SequenceEqual(merged)
            ? null
            : ById(FcbDocument.Deserialize(original));

        List<SoldierCopy> copies = [];
        foreach (ArchetypeDefinition definition in soldiers)
        {
            if (definition.FragmentId is not { } id || !prototypes.TryGetValue(id, out FcbObject? root))
            {
                continue;
            }
            FcbObject vanillaRoot = vanillaPrototypes is null ? root.Clone() : vanillaPrototypes.GetValueOrDefault(id) ?? root.Clone();
            if (EntityNamed(root, definition.Name) is { } entity && EntityNamed(vanillaRoot, definition.Name) is { } vanillaEntity)
            {
                copies.Add(new SoldierCopy(definition, root, entity, vanillaRoot, vanillaEntity));
            }
        }
        return copies;
    }

    private static Dictionary<string, FcbObject> ById(FcbObject library)
    {
        var byId = new Dictionary<string, FcbObject>(FcbFragments.IdComparer);
        foreach (FcbFragment fragment in FcbFragments.List(library))
        {
            byId[fragment.Id] = fragment.Node;
        }
        return byId;
    }

    private static FcbObject? EntityNamed(FcbObject node, string name)
    {
        if (node.TypeHash == WorldHashes.Entity && FcbEntityFields.ReadString(node, WorldHashes.HidName) == name)
        {
            return node;
        }
        return node.Children.Select(c => EntityNamed(c, name)).FirstOrDefault(e => e is not null);
    }
}
