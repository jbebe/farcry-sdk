namespace JackAll.Core.Format.Fcb;

/// <summary>
/// Splices one or more replacement fragments (see <see cref="FcbXml.ListFragmentIds"/>'s
/// <c>NN_Name.xml</c> scheme) into a container `.fcb`'s binary tree, without touching anything else
/// in the file.
/// </summary>
/// <remarks>
/// This is what turns a fragment override — a single child's XML, staged under
/// <c>container.fcb\NN_Name.xml</c> — back into a real, engine-loadable `.fcb`, so a one-entity edit
/// doesn't require shipping the whole recompiled binary. See docs/design/fcb-fragment-overlays.md,
/// Milestone 2.
/// </remarks>
public static class FcbAssembler
{
    /// <summary>
    /// Decodes <paramref name="baseFcb"/>, replaces each child whose current <see cref="FcbXml"/>-
    /// assigned id matches a key in <paramref name="fragmentXmlById"/> (case-insensitively — a staged
    /// path is lowercased by <c>NameHash.Normalize</c> on the way in, but <see cref="FcbXml.ListFragmentIds"/>
    /// preserves whatever case the game data's entity name actually has) with that XML re-parsed, and
    /// re-encodes. A fragment id with no matching child is appended as a brand-new child instead — a
    /// mod adding an entity that never existed in the vanilla container, the normal "add new content"
    /// case (see <see cref="FragmentMerge.Resolve"/>'s matching empty-ancestor handling) — appended in
    /// a deterministic (ordinal) order so building twice from the same layers is byte-identical.
    /// Returns <paramref name="baseFcb"/> unchanged, with no decode/encode round trip, when there is
    /// nothing to splice.
    /// </summary>
    public static byte[] Apply(byte[] baseFcb, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseFcb;
        }

        // Normalized into a case-insensitive copy regardless of what comparer the caller's dictionary
        // happened to use - this is the one place that has to get it right, so it doesn't rely on
        // every caller remembering to build theirs with StringComparer.OrdinalIgnoreCase too.
        var byId = new Dictionary<string, string>(fragmentXmlById, StringComparer.OrdinalIgnoreCase);

        FcbObject root = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<string> ids = FcbXml.ListFragmentIds(root);

        var remaining = new HashSet<string>(byId.Keys, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ids.Count; i++)
        {
            if (byId.TryGetValue(ids[i], out string? xml))
            {
                root.Children[i] = FcbXml.FromXml(xml);
                remaining.Remove(ids[i]);
            }
        }

        foreach (string id in remaining.OrderBy(x => x, StringComparer.Ordinal))
        {
            root.Children.Add(FcbXml.FromXml(byId[id]));
        }

        return FcbDocument.Serialize(root);
    }
}
