using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tools.Sav;

/// <summary>
/// A save's persisted entities: each record's <c>State</c>, read over the entity placed with the
/// record's <c>Id</c>, itself merged over its archetype - the order the engine spawns then restores
/// them in. See docs/docs/file-formats/savegame.md.
/// </summary>
public sealed class SaveGameBases(
    string world, Func<ulong, WorldEntity?> placedOf, Func<string, FcbObject?> archetypeOf) : IEntityBases
{
    public bool IsEntity(FcbObject node, FcbObject? parent)
        => node.TypeHash == WorldHashes.Entity
           || node.TypeHash == PersistenceHashes.State && parent?.TypeHash == PersistenceHashes.Record;

    public (FcbObject? Base, string Details) BaseOf(FcbObject entity, FcbObject? parent)
    {
        if (parent is null || placedOf(FcbEntityFields.ReadU64(parent, PersistenceHashes.Id)) is not { } placed)
        {
            return (null, $"not an entity placed in {world} - shown without a base");
        }

        FcbObject? archetype = placed.ArchetypeName.Length > 0 ? archetypeOf(placed.ArchetypeName) : null;
        return (MergedNode.Of(placed.Node, archetype).Flatten(),
            $"persisted over {EntityHierarchy.LabelOf(placed)} · {ArchetypeBases.Line(placed.ArchetypeName, archetype is not null)}");
    }
}
