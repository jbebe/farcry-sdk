namespace JackAll.Tools.Move;

/// <summary>What a repoint did, and what it deliberately did not do.</summary>
/// <param name="Rewritten">Reference sites the target weapon governs, which were retargeted.</param>
/// <param name="OtherWeapon">Sites another weapon governs; leaving them is the point.</param>
/// <param name="Ungoverned">
/// Sites no weapon governs. Left alone for the same reason, but they make the repoint
/// <em>incomplete</em>: the weapon still reaches the original clip through them.
/// </param>
/// <param name="Unreferenced">Mapped clips the graph never names, usually a typo in the map.</param>
public sealed record MoveRepointResult(
    int Rewritten,
    int OtherWeapon,
    int Ungoverned,
    IReadOnlyList<uint> Unreferenced)
{
    /// <summary>True when every site the weapon can reach was retargeted.</summary>
    public bool IsComplete => Ungoverned == 0;
}

/// <summary>
/// Retargets the animation clips one weapon plays, leaving every other weapon's alone.
/// </summary>
/// <remarks>
/// Scoping is what makes this safe. Rewriting clip references by hash across the whole graph also
/// retargets the clips other weapons share, which is how the Dart Rifle's draw animation can be
/// repointed and silently take the MGL-140's with it. Only sites the target weapon governs are
/// rewritten.
///
/// The corollary is that a scoped repoint cannot retarget shared behaviour at all - if a mapped
/// clip is also reached from a site no weapon governs, the weapon keeps playing the original
/// through that path and the result is reported as incomplete. Making that behaviour differ per
/// weapon means cloning the states so the sites become governed, which is an expansion rather than
/// a repoint.
/// </remarks>
public static class MoveRepoint
{
    private const string ClipField = "m_animNameHash";

    public static MoveRepointResult Apply(MoveFile file, int weapon, IReadOnlyDictionary<uint, uint> map)
    {
        IReadOnlyDictionary<MoveObject, MoveObject> owners = MoveWeapons.Owners(file);
        int rewritten = 0, otherWeapon = 0, ungoverned = 0;
        HashSet<uint> seen = [];

        foreach (MoveObject obj in file.Objects)
        {
            for (int i = 0; i < obj.Ops.Count; i++)
            {
                MoveOp op = obj.Ops[i];
                if (op.Name != ClipField || !map.TryGetValue(op.Number, out uint replacement))
                {
                    continue;
                }

                seen.Add(op.Number);
                int? governing = MoveWeapons.GoverningWeapon(obj, owners);
                if (governing == weapon)
                {
                    obj.Ops[i] = op.WithNumber(replacement);
                    rewritten++;
                }
                else if (governing is null)
                {
                    ungoverned++;
                }
                else
                {
                    otherWeapon++;
                }
            }
        }

        return new MoveRepointResult(
            rewritten, otherWeapon, ungoverned, [.. map.Keys.Where(h => !seen.Contains(h)).Order()]);
    }
}
