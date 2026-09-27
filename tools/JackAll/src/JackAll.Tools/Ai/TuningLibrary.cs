using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tools.Ai;

/// <summary>
/// One world's copy of a tuned archetype: its prototype fragment, being edited, beside the base
/// game's copy of the same entity.
/// </summary>
public sealed class TuningCopy
{
    private readonly FcbObject _vanillaRoot;
    private readonly Dictionary<TuningField, double?> _vanilla = [];

    internal TuningCopy(ArchetypeDefinition definition, FcbObject root, FcbObject entity, FcbObject vanillaRoot, FcbObject vanillaEntity)
    {
        Definition = definition;
        Root = root;
        Entity = entity;
        _vanillaRoot = vanillaRoot;
        VanillaEntity = vanillaEntity;
    }

    public ArchetypeDefinition Definition { get; }

    /// <summary>The whole prototype - the fragment that is staged.</summary>
    public FcbObject Root { get; }

    public FcbObject Entity { get; }

    public FcbObject VanillaEntity { get; }

    public double? Vanilla(TuningField field)
    {
        if (!_vanilla.TryGetValue(field, out double? value))
        {
            _vanilla[field] = value = field.Read(VanillaEntity);
        }
        return value;
    }

    public bool DiffersIn(IEnumerable<TuningField> fields) => fields.Any(f => f.Read(Entity) != Vanilla(f));

    /// <summary>The fragment to stage, and whether it is back to the base game's text.</summary>
    public (string FragmentId, string Xml, bool IsVanilla) Plan(FcbClassDefinitions definitions)
    {
        string xml = FcbXml.ToXml(Root, definitions);
        return (Definition.FragmentId!, xml, xml == FcbXml.ToXml(_vanillaRoot, definitions));
    }
}

public static class TuningLibrary
{
    /// <summary>
    /// The prototypes of <paramref name="archetypes"/> out of one entity library, decoded once.
    /// <paramref name="original"/> is the base game's library; null or identical means the base game
    /// is what <paramref name="merged"/> holds.
    /// </summary>
    public static IReadOnlyList<TuningCopy> Open(IEnumerable<ArchetypeDefinition> archetypes, byte[] merged, byte[]? original)
    {
        Dictionary<string, FcbObject> prototypes = ById(FcbDocument.Deserialize(merged));
        Dictionary<string, FcbObject>? vanillaPrototypes = original is null || original.AsSpan().SequenceEqual(merged)
            ? null
            : ById(FcbDocument.Deserialize(original));

        List<TuningCopy> copies = [];
        foreach (ArchetypeDefinition definition in archetypes)
        {
            if (definition.FragmentId is not { } id || !prototypes.TryGetValue(id, out FcbObject? root))
            {
                continue;
            }
            FcbObject vanillaRoot = vanillaPrototypes?.GetValueOrDefault(id) ?? root.Clone();
            if (EntityNamed(root, definition.Name) is { } entity && EntityNamed(vanillaRoot, definition.Name) is { } vanillaEntity)
            {
                copies.Add(new TuningCopy(definition, root, entity, vanillaRoot, vanillaEntity));
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
