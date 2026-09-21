using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>Which nodes of a document are entities, and what each one merges over.</summary>
public interface IEntityBases
{
    bool IsEntity(FcbObject node, FcbObject? parent);

    /// <summary>The node <paramref name="entity"/> merges over, if any, and a line saying what it is.</summary>
    (FcbObject? Base, string Details) BaseOf(FcbObject entity, FcbObject? parent);
}

/// <summary>A data file's entities: every <c>Entity</c> node, over the archetype it names.</summary>
/// <param name="archetypeOf">The archetype of a name, or null when the document's world is not known.</param>
public sealed class ArchetypeBases(Func<string, FcbObject?>? archetypeOf) : IEntityBases
{
    public bool IsEntity(FcbObject node, FcbObject? parent) => node.TypeHash == WorldHashes.Entity;

    public (FcbObject? Base, string Details) BaseOf(FcbObject entity, FcbObject? parent)
    {
        string name = FcbEntityFields.ReadString(entity, WorldHashes.TplCreatureType);
        if (name.Length == 0)
        {
            return (null, Line(name, false));
        }
        if (archetypeOf is null)
        {
            return (null, $"archetype {name} - this document's world is not known, so it is shown unmerged");
        }
        FcbObject? archetype = archetypeOf(name);
        return (archetype, Line(name, archetype is not null));
    }

    /// <summary>The line saying what an entity merges over, or why it spawns nothing.</summary>
    public static string Line(string archetypeName, bool found)
        => archetypeName.Length == 0
            ? "standalone - no archetype"
            : found
                ? $"archetype {archetypeName}"
                : $"archetype {archetypeName} is not in this world's library - the game will not spawn it";
}
