namespace JackAll.Core.Format.Fcb;

/// <summary>
/// Splices one or more replacement fragments (see <see cref="FcbFragments"/>' path-shaped id scheme)
/// into a container `.fcb`'s binary tree, without touching anything else in the file.
/// </summary>
/// <remarks>
/// This is what turns a fragment override — a single node's XML, staged under
/// <c>container.fcb\&lt;fragment id&gt;</c> — back into a real, engine-loadable `.fcb`, so a
/// one-entity edit doesn't require shipping the whole recompiled binary. See
/// docs/design/fcb-fragment-overlays.md Milestone 2 and docs/design/fcb-deep-fragments.md.
/// </remarks>
public static class FcbAssembler
{
    /// <summary>
    /// Decodes <paramref name="baseFcb"/>, replaces each fragment whose current id matches a key in
    /// <paramref name="fragmentXmlById"/> (via <see cref="FcbFragments.IdComparer"/> — a staged path
    /// is lowercased by <c>NameHash.Normalize</c> on the way in, and an entity override must match on
    /// its numeric id even if the entity was renamed) with that XML re-parsed, and re-encodes. Legacy
    /// whole-group ids resolve first, so a deep id staged inside a replaced group still lands in the
    /// group's new content. A fragment id with no match at all is appended as brand-new content — a
    /// mod adding an entity that never existed in the vanilla container, the normal "add new content"
    /// case (see <see cref="FragmentMerge.Resolve"/>'s matching empty-ancestor handling) — under the
    /// shape-defined parent (<see cref="FcbFragments.AppendTarget"/>), in a deterministic (ordinal)
    /// order so building twice from the same layers is byte-identical. Returns
    /// <paramref name="baseFcb"/> unchanged, with no decode/encode round trip, when there is nothing
    /// to splice.
    /// </summary>
    public static byte[] Apply(byte[] baseFcb, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseFcb;
        }

        // Re-keyed under the one canonical comparer regardless of what comparer the caller's
        // dictionary happened to use - this is the one place that has to get it right, so it doesn't
        // rely on every caller remembering to build theirs with FcbFragments.IdComparer too.
        var byId = new Dictionary<string, string>(fragmentXmlById, FcbFragments.IdComparer);

        FcbObject root = FcbDocument.Deserialize(baseFcb);
        var remaining = new HashSet<string>(byId.Keys, FcbFragments.IdComparer);
        List<FcbFragments.FragmentSlot> slots = FcbFragments.Slots(root);

        // Whole-group alias replacements land before deep ones, so a deep id staged inside a
        // replaced group resolves against the group's new content. An id claimed by a deep fragment
        // never consumes a group though - same deep-over-alias precedence FcbFragments.Find applies.
        if (FcbFragments.TryGetGroupIds(root, out IReadOnlyList<string> groupIds))
        {
            var deepClaimed = new HashSet<string>(slots.Select(s => s.Id), FcbFragments.IdComparer);
            bool groupReplaced = false;
            for (int i = 0; i < groupIds.Count; i++)
            {
                if (!deepClaimed.Contains(groupIds[i]) && remaining.Remove(groupIds[i]))
                {
                    root.Children[i] = FcbXml.FromXml(byId[groupIds[i]]);
                    groupReplaced = true;
                }
            }
            if (groupReplaced && remaining.Count > 0)
            {
                slots = FcbFragments.Slots(root);
            }
        }

        foreach (FcbFragments.FragmentSlot slot in slots)
        {
            if (remaining.Count == 0)
            {
                break;
            }
            if (remaining.Remove(slot.Id))
            {
                slot.Parent.Children[slot.Index] = FcbXml.FromXml(byId[slot.Id]);
            }
        }

        foreach (string id in remaining.OrderBy(x => x, StringComparer.Ordinal))
        {
            FcbObject addition = FcbXml.FromXml(byId[id]);
            FcbFragments.AppendTarget(root, addition).Children.Add(addition);
        }

        return FcbDocument.Serialize(root);
    }
}
