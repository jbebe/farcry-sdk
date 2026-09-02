namespace JackAll.Core.Format.Move;

/// <summary>One animation clip a weapon plays, and who else plays it.</summary>
/// <param name="Hash">CPathID of the clip's game path: CRC32 of the lowercased path.</param>
/// <param name="References">How many <c>m_animNameHash</c> fields in the whole graph name it.</param>
/// <param name="PlayedBy">Every EquippedWeapon index whose scope reaches it.</param>
public sealed record MoveClip(uint Hash, int References, IReadOnlyList<int> PlayedBy)
{
    /// <summary>True when only one weapon plays it, so repointing it affects nothing else.</summary>
    public bool IsExclusive => PlayedBy.Count == 1;
}

/// <summary>What a given EquippedWeapon index plays.</summary>
/// <remarks>
/// Scoping is the whole problem here. A criterion pins the subtree it hangs off, so the clips of
/// weapon N are the ones under an object whose <em>own</em> criteria test
/// <c>EquippedWeapon == N</c> - not the subtree of any top-level state that mentions N somewhere.
/// Top-level states are shared containers holding one branch per weapon: walking a whole one
/// reaches 1,761 clips spanning every weapon in the game rather than the ~50 that weapon plays.
/// </remarks>
public static class MoveWeapons
{
    public const int EquippedWeaponChannel = 17;

    public const int DesiredWeaponChannel = 18;

    private const string ClipField = "m_animNameHash";

    /// <summary>
    /// The weapon this object's own criteria pin it to, if any.
    /// </summary>
    /// <remarks>
    /// Both weapon channels count. A draw or holster branch is gated on <c>DesiredWeapon</c> - the
    /// weapon being switched to - rather than <c>EquippedWeapon</c>, so a rule that reads only
    /// channel 17 misses those sites entirely: three of the five sites playing the Dart Rifle's
    /// draw clip are pinned by channel 18.
    /// </remarks>
    public static int? WeaponOf(MoveObject obj)
    {
        foreach (MoveOp op in obj.Ops)
        {
            if (op.Name != "CMoveCriteria" || op.Target is not { } criterion)
            {
                continue;
            }

            if (criterion.ClassName != "CMoveCriteriaEnumEqual")
            {
                continue;
            }

            uint? channel = criterion.Field("m_eValueID");
            if (channel is EquippedWeaponChannel or DesiredWeaponChannel
                && criterion.Field("m_Value") is { } value)
            {
                return unchecked((int)value);
            }
        }

        return null;
    }

    /// <summary>Every EquippedWeapon index that scopes anything, in order.</summary>
    public static IReadOnlyList<int> Indices(MoveFile file) =>
        [.. file.Objects.Select(WeaponOf).OfType<int>().Distinct().Order()];

    /// <summary>Who owns each object: the one <c>pnew</c> pointer that created it.</summary>
    public static IReadOnlyDictionary<MoveObject, MoveObject> Owners(MoveFile file)
    {
        Dictionary<MoveObject, MoveObject> owners = [];
        foreach (MoveObject obj in file.Objects)
        {
            foreach (MoveOp op in obj.Ops)
            {
                if (op.Kind == MoveOpKind.PointerNew)
                {
                    owners[op.Target!] = obj;
                }
            }
        }

        return owners;
    }

    /// <summary>
    /// The weapon governing this object: the nearest pinned ancestor, itself included.
    /// </summary>
    /// <remarks>
    /// Null means no ancestor pins a weapon, so the object is shared behaviour that every weapon
    /// can reach. Most of the graph is like this - 3,925 of <c>movemgr.bin</c>'s 6,341 clip
    /// reference sites - and rewriting one of those while scoped to a single weapon would change
    /// how every other weapon animates.
    /// </remarks>
    public static int? GoverningWeapon(
        MoveObject obj, IReadOnlyDictionary<MoveObject, MoveObject> owners)
    {
        for (MoveObject? node = obj; node is not null; node = owners.GetValueOrDefault(node))
        {
            if (WeaponOf(node) is { } weapon)
            {
                return weapon;
            }
        }

        return null;
    }

    /// <summary>Every clip reference in the graph, counted by hash.</summary>
    public static IReadOnlyDictionary<uint, int> AllClipReferences(MoveFile file)
    {
        Dictionary<uint, int> counts = [];
        foreach (MoveObject obj in file.Objects)
        {
            foreach (MoveOp op in obj.Ops)
            {
                if (op.Name == ClipField)
                {
                    counts[op.Number] = counts.GetValueOrDefault(op.Number) + 1;
                }
            }
        }

        return counts;
    }

    /// <summary>The clip hashes each weapon index's scopes reach.</summary>
    public static IReadOnlyDictionary<int, IReadOnlySet<uint>> ClipsByWeapon(MoveFile file)
    {
        Dictionary<int, IReadOnlySet<uint>> result = [];
        foreach (int weapon in Indices(file))
        {
            HashSet<uint> clips = [];
            foreach (MoveObject root in file.Objects.Where(o => WeaponOf(o) == weapon))
            {
                Collect(root, weapon, clips, []);
            }

            result[weapon] = clips;
        }

        return result;
    }

    /// <summary>What <paramref name="weapon"/> plays, with each clip's sharing worked out.</summary>
    public static IReadOnlyList<MoveClip> ClipsFor(MoveFile file, int weapon)
    {
        IReadOnlyDictionary<int, IReadOnlySet<uint>> byWeapon = ClipsByWeapon(file);
        if (!byWeapon.TryGetValue(weapon, out IReadOnlySet<uint>? mine))
        {
            return [];
        }

        IReadOnlyDictionary<uint, int> references = AllClipReferences(file);
        List<MoveClip> clips = [];
        foreach (uint hash in mine.Order())
        {
            List<int> playedBy = [.. byWeapon.Where(p => p.Value.Contains(hash)).Select(p => p.Key).Order()];
            clips.Add(new MoveClip(hash, references.GetValueOrDefault(hash), playedBy));
        }

        return clips;
    }

    private static void Collect(MoveObject obj, int weapon, HashSet<uint> clips, HashSet<MoveObject> seen)
    {
        // A nested branch pinned to a different weapon excludes this one.
        if (WeaponOf(obj) is { } pinned && pinned != weapon)
        {
            return;
        }

        foreach (MoveOp op in obj.Ops)
        {
            if (op.Name == ClipField)
            {
                clips.Add(op.Number);
            }
            else if (op.Kind == MoveOpKind.PointerNew && seen.Add(op.Target!))
            {
                Collect(op.Target!, weapon, clips, seen);
            }
        }
    }
}
