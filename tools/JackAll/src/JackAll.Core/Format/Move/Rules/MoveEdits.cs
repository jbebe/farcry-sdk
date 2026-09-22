using System.Text;

namespace JackAll.Core.Format.Move.Rules;

/// <summary>A change the graph cannot take, phrased for the person who asked for it.</summary>
public sealed class MoveEditException(string message) : Exception(message);

/// <summary>What copying one weapon's animations onto another index did.</summary>
/// <param name="States">The top-level states that gained branches.</param>
/// <param name="Skipped">States left alone, and why.</param>
public sealed record MoveCloneResult(IReadOnlyList<uint> States, int Branches, IReadOnlyList<string> Skipped);

/// <summary>The structural edits a rule view makes on a live graph.</summary>
internal static class MoveEdits
{
    /// <summary>
    /// Copies a subtree: every object it owns is duplicated, references between them follow the
    /// copies, and references leaving it still point at the originals.
    /// </summary>
    public static MoveObject DeepCopy(MoveObject root)
    {
        Dictionary<MoveObject, MoveObject> copies = [];
        MoveObject copy = Copy(root);
        foreach (MoveObject clone in copies.Values)
        {
            for (int i = 0; i < clone.Ops.Count; i++)
            {
                MoveOp op = clone.Ops[i];
                if (op.Kind == MoveOpKind.PointerRef && copies.TryGetValue(op.Target!, out MoveObject? inside))
                {
                    clone.Ops[i] = op.WithTarget(inside);
                }
            }
        }

        return copy;

        MoveObject Copy(MoveObject node)
        {
            MoveObject clone = new(node.ClassName);
            copies[node] = clone;
            foreach (MoveOp op in node.Ops)
            {
                clone.Ops.Add(op.Kind == MoveOpKind.PointerNew ? op.WithTarget(Copy(op.Target!)) : op);
            }

            return clone;
        }
    }

    public static bool HoldsState(MoveObject root) => root.Subtree().Any(o => MoveStateIndex.NameHashOf(o) is not null);

    /// <summary>
    /// Gives <paramref name="target"/> a copy of every branch <paramref name="donor"/> has, beside the
    /// donor's own, in every state that has any.
    /// </summary>
    public static MoveCloneResult CloneWeapon(
        MoveFile file, MoveStateIndex index, MoveNames names, int donor, int target, string? package)
    {
        List<uint> states = [];
        List<string> skipped = [];
        List<MoveObject> copies = [];
        foreach (MoveObject state in index.TopLevelStates)
        {
            if (MoveStateIndex.NameHashOf(state) is not { } hash)
            {
                continue;
            }

            IReadOnlyList<MoveUnits.Site> sites = MoveUnits.BranchesOf(state, hash);
            List<MoveUnits.Site> donors = [.. sites.Where(s => s.Unit.Weapon == donor)];
            if (donors.Count == 0)
            {
                continue;
            }

            string name = new MoveUnit(hash, 0, null).LabelFor(names.Of(hash));
            if (sites.Any(s => s.Unit.Weapon == target))
            {
                skipped.Add($"{name} already has animations for index {target}");
                continue;
            }

            if (donors.Any(s => HoldsState(s.Branch)))
            {
                skipped.Add($"{name}: a branch holds a nested state, whose name would be duplicated");
                continue;
            }

            foreach (MoveUnits.Site site in donors)
            {
                MoveObject copy = DeepCopy(site.Branch);
                Repin(copy, donor, target);
                MoveObject owner = index.OwnerOf(site.Branch)!;
                int at = owner.Ops.FindIndex(op => op.Kind == MoveOpKind.PointerNew && op.Target == site.Branch);
                owner.Ops.Insert(at + 1, owner.Ops[at].WithTarget(copy));
                copies.Add(copy);
            }

            states.Add(hash);
        }

        if (package is { Length: > 0 } && file.Manager is { } manager)
        {
            Repackage(manager, copies, package);
        }

        return new MoveCloneResult(states, copies.Count, skipped);
    }

    /// <summary>Rewrites every weapon test in a copied branch that names the donor.</summary>
    private static void Repin(MoveObject copy, int donor, int target)
    {
        foreach (MoveObject node in copy.Subtree())
        {
            if (node.ClassName is "CMoveCriteriaEnumEqual" or "CMoveCriteriaEnumNotEqual"
                && node.Field("m_eValueID") is MoveWeapons.EquippedWeaponChannel or MoveWeapons.DesiredWeaponChannel
                && node.Field("m_Value") == unchecked((uint)donor))
            {
                node.SetField("m_Value", unchecked((uint)target));
            }
        }
    }

    /// <summary>
    /// Moves the copies' clips from the donor's own package to <paramref name="package"/>, and
    /// registers it in the manager's package list. Clips the donor borrows from another package keep it.
    /// </summary>
    private static void Repackage(MoveObject manager, List<MoveObject> copies, string package)
    {
        List<MoveObject> sites = [.. copies.SelectMany(c => c.Subtree()).Where(o => o.Field("m_package") is not null)];
        if (sites.Count == 0)
        {
            return;
        }

        uint own = sites.GroupBy(o => o.Field("m_package")!.Value).MaxBy(g => g.Count())!.Key;
        uint renamed = MoveNames.HashOf(package);
        foreach (MoveObject site in sites.Where(o => o.Field("m_package") == own))
        {
            site.SetField("m_package", renamed);
        }

        AddPackage(manager, package, own);
    }

    /// <summary>Adds a row to the package list, shaped like the row of the package it stands in for.</summary>
    private static void AddPackage(MoveObject manager, string package, uint likeHash)
    {
        (int start, int count) = MoveSections.Ranges(manager)[MoveSection.Packages];
        int end = start + count;
        int size = manager.Ops.FindIndex(start, count, op => op.Name == "size");
        List<int> rows = [.. Enumerable.Range(start, count).Where(i => manager.Ops[i].Name == "Name")];
        if (rows.Any(i => string.Equals(Text(manager.Ops[i]), package, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        int like = rows.FirstOrDefault(i => MoveNames.HashOf(Text(manager.Ops[i])) == likeHash, rows[0]);
        int next = rows.FirstOrDefault(i => i > like, end);
        List<MoveOp> row = [MoveOp.Blob(MoveOpKind.Str, "Name", Encoding.ASCII.GetBytes(package))];
        row.AddRange(manager.Ops.GetRange(like + 1, next - like - 1));
        manager.Ops.InsertRange(end, row);
        manager.Ops[size] = manager.Ops[size].WithNumber(manager.Ops[size].Number + 1);
    }

    private static string Text(MoveOp op) => Encoding.ASCII.GetString(op.Bytes!);
}
