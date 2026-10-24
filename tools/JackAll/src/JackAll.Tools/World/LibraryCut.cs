using System.Text;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// A world's entity library cut from the full archetype set, the way Ubisoft cut each world's from
/// <c>entitylibrary_full.fcb</c>: the groups the game reads by name wherever it is played, kept whole,
/// and every archetype the world's entities name, with every archetype those name in turn.
/// </summary>
/// <remarks>
/// Cut from world 1's placed entities and Domino scripts, this keeps every one of the 1,419 archetypes
/// Ubisoft's world 1 library holds. See docs/docs/engine-internals/entity-instancing.md.
/// </remarks>
public static class LibraryCut
{
    /// <summary>Top-level groups no placed entity names: the player, weapons and their data, curves,
    /// cameras, hand-held props and the rest the code and animations ask for by name, and the soldiers
    /// and vehicles scripts and spawners create by name at run time.</summary>
    public static IReadOnlySet<string> WholeGroups { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Beautifiers", "buddies", "cameras", "Curves", "Domino", "engine", "enemy_archetypes", "gadgets",
        "pickups", "player", "PostFXs", "props", "tables", "vehicle", "WeaponProperties", "weapons",
    };

    /// <summary>
    /// <paramref name="full"/> cut down to <see cref="WholeGroups"/> plus whatever <paramref name="namers"/>
    /// name, closed over the references between archetypes, in <paramref name="full"/>'s own order.
    /// </summary>
    public static FcbObject Cut(FcbObject full, IEnumerable<FcbObject> namers)
    {
        var prototypes = new Dictionary<string, FcbObject>(StringComparer.OrdinalIgnoreCase);
        foreach (FcbObject prototype in full.Children.SelectMany(g => g.Children))
        {
            if (NameOf(prototype) is { } name)
            {
                prototypes.TryAdd(name, prototype);
            }
        }

        var kept = new HashSet<string>(prototypes.Keys.Where(n => WholeGroups.Contains(n.Split('.')[0])), StringComparer.OrdinalIgnoreCase);
        kept.UnionWith(namers.SelectMany(n => Named(n, prototypes)));
        var pending = new Queue<string>(kept);
        while (pending.TryDequeue(out string? name))
        {
            foreach (string named in Named(prototypes[name], prototypes))
            {
                if (kept.Add(named))
                {
                    pending.Enqueue(named);
                }
            }
        }

        var cut = new FcbObject { TypeHash = full.TypeHash };
        foreach (FcbObject group in full.Children)
        {
            var keptGroup = new FcbObject { TypeHash = group.TypeHash };
            foreach ((uint key, byte[] value) in group.Values)
            {
                keptGroup.Values[key] = value;
            }
            keptGroup.Children.AddRange(group.Children.Where(p => NameOf(p) is { } name && kept.Contains(name)));
            if (keptGroup.Children.Count > 0)
            {
                cut.Children.Add(keptGroup);
            }
        }
        return cut;
    }

    private static string? NameOf(FcbObject prototype)
        => prototype.TypeHash == WorldHashes.EntityPrototype
           && prototype.Children.FirstOrDefault(c => c.TypeHash == WorldHashes.Entity) is { } entity
           && FcbEntityFields.ReadString(entity, WorldHashes.HidName) is { Length: > 0 } name
            ? name
            : null;

    /// <summary>Every archetype a string value anywhere under <paramref name="node"/> spells.</summary>
    private static IEnumerable<string> Named(FcbObject node, IReadOnlyDictionary<string, FcbObject> prototypes)
    {
        foreach (byte[] value in node.Values.Values)
        {
            if (value.Length > 1 && value[^1] == 0
                && prototypes.ContainsKey(Encoding.UTF8.GetString(value, 0, value.Length - 1)))
            {
                yield return Encoding.UTF8.GetString(value, 0, value.Length - 1);
            }
        }
        foreach (FcbObject child in node.Children)
        {
            foreach (string named in Named(child, prototypes))
            {
                yield return named;
            }
        }
    }
}
