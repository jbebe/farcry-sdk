using System.Globalization;

namespace JackAll.Core.Format.Move;

/// <summary>
/// One overridable piece of a state: either the state with its weapon branches elided, or all of one
/// weapon's branches within it.
/// </summary>
/// <remarks>
/// <see cref="Weapon"/> is null for the remainder. The triple is three engine-assigned values with no
/// positional component - the state's <c>m_stateNameHash</c>, the MOVE channel index, and the
/// <c>EquippedWeapon</c> enum value a criterion compares against, which is the same integer a weapon
/// archetype's <c>iAnimationValue</c> holds.
/// </remarks>
public readonly record struct MoveUnit(uint StateHash, int Channel, int? Weapon)
{
    public bool IsRemainder => Weapon is null;

    /// <summary>
    /// The name this unit hashes from, and the one a hand-written fragment id may spell out.
    /// </summary>
    public string Key => Weapon is { } weapon
        ? $"movestate:{StateHash:x8}:ch{Channel}:w{weapon}"
        : $"movestate:{StateHash:x8}";

    /// <summary>A short, readable label for a fragment filename; the number after it is what binds.</summary>
    public string Label => Weapon is { } weapon
        ? $"state_{StateHash:x8}_ch{Channel}_w{weapon}"
        : $"state_{StateHash:x8}";

    public uint Id => Weapon is null ? StateHash : NameHash.Compute(Key);

    public override string ToString() => Key;
}

/// <summary>
/// Splits one state into the pieces a mod overrides independently.
/// </summary>
/// <remarks>
/// A state on its own is the wrong unit for the job. The graph's big top-level states are shared
/// containers holding one branch per weapon, so retargeting a single weapon's clips rewrites a few
/// values inside a subtree of thousands - the VSS Vintorez changes 81 clip hashes, 324 bytes, and
/// shipping the states that hold them costs 11.2 MB of XML, worse than the 1.8 MB binary it replaces.
///
/// Splitting a state again at its weapon-pinned branches takes that to 287 objects, and every one of
/// them belongs to the weapon being modded - so two mods retargeting different weapons never touch
/// the same file. Only 43 of <c>movemgr.bin</c>'s 1,687 states have such branches, so the other 1,644
/// are unaffected.
///
/// See docs/docs/file-formats/move.md.
/// </remarks>
public static class MoveUnits
{
    /// <summary>What one state decomposes into: its own objects, and its branches by unit.</summary>
    /// <param name="Remainder">The state, with every branch site elided.</param>
    /// <param name="Branches">
    /// The elided subtrees, grouped by unit and kept in the state's own pre-order. The key repeats
    /// across sites - 228 of <c>movemgr.bin</c>'s 621 keys cover more than one branch - which is why
    /// a fragment is the whole group rather than a single site.
    /// </param>
    public sealed record Decomposition(
        MoveObject Remainder,
        IReadOnlyDictionary<MoveUnit, List<MoveObject>> Branches);

    /// <summary>
    /// The weapon this object's own criteria pin it to, as a channel and value.
    /// </summary>
    /// <remarks>
    /// Both weapon channels count, for the reason <see cref="MoveWeapons.WeaponOf"/> gives: a draw or
    /// holster branch is gated on <c>DesiredWeapon</c> rather than <c>EquippedWeapon</c>, and the VSS
    /// mod edits two such branches.
    /// </remarks>
    public static (int Channel, int Weapon)? PinOf(MoveObject obj)
    {
        foreach (MoveOp op in obj.Ops)
        {
            if (op.Name != "CMoveCriteria" || op.Target is not { } criterion
                || criterion.ClassName != "CMoveCriteriaEnumEqual")
            {
                continue;
            }

            uint? channel = criterion.Field("m_eValueID");
            if (channel is MoveWeapons.EquippedWeaponChannel or MoveWeapons.DesiredWeaponChannel
                && criterion.Field("m_Value") is { } value)
            {
                return ((int)channel.Value, unchecked((int)value));
            }
        }

        return null;
    }

    /// <summary>One elided branch: the subtree, its unit, and the field it hung off.</summary>
    public readonly record struct Site(MoveObject Branch, MoveUnit Unit, string Name);

    /// <summary>
    /// Finds every outermost weapon-pinned branch in a state: pinned itself, with no pinned ancestor
    /// inside the state. Descending no further is what keeps a weapon's whole branch in one piece.
    /// </summary>
    /// <remarks>Pre-order, which is the order a fragment's sites are matched back up in.</remarks>
    public static IReadOnlyList<Site> BranchesOf(MoveObject state, uint stateHash)
    {
        List<Site> found = [];
        Walk(state, string.Empty, root: true);
        return found;

        void Walk(MoveObject node, string name, bool root)
        {
            if (!root && PinOf(node) is { } pin)
            {
                found.Add(new Site(node, new MoveUnit(stateHash, pin.Channel, pin.Weapon), name));
                return;
            }

            foreach (MoveOp op in node.Ops)
            {
                if (op.Kind == MoveOpKind.PointerNew)
                {
                    Walk(op.Target!, op.Name, root: false);
                }
            }
        }
    }

    /// <summary>Every unit a state splits into, remainder first.</summary>
    public static IReadOnlyList<MoveUnit> UnitsOf(MoveObject state, uint stateHash)
    {
        List<MoveUnit> units = [new MoveUnit(stateHash, 0, null)];
        foreach (Site site in BranchesOf(state, stateHash))
        {
            if (!units.Contains(site.Unit))
            {
                units.Add(site.Unit);
            }
        }

        return units;
    }

    /// <summary>The unit a fragment id names, when the id spells the key out rather than hashing it.</summary>
    public static MoveUnit? Parse(string key)
    {
        string[] parts = key.Split(':');
        if (parts.Length is not (2 or 4) || parts[0] != "movestate"
            || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
        {
            return null;
        }

        if (parts.Length == 2)
        {
            return new MoveUnit(hash, 0, null);
        }

        if (!parts[2].StartsWith("ch", StringComparison.Ordinal)
            || !parts[3].StartsWith('w')
            || !int.TryParse(parts[2][2..], out int channel)
            || !int.TryParse(parts[3][1..], out int weapon))
        {
            return null;
        }

        return new MoveUnit(hash, channel, weapon);
    }
}
