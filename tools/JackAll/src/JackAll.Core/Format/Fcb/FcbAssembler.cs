using System.Text;

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
    /// its numeric id even if the entity was renamed) with that XML re-parsed, and re-encodes. A
    /// fragment id with no match at all is appended as brand-new content — a mod adding an entity that
    /// never existed in the vanilla container, the normal "add new content" case (see
    /// <see cref="FragmentMerge.Resolve"/>'s matching empty-ancestor handling) — under the
    /// shape-defined parent (<see cref="FcbFragments.AppendTarget"/>), in a deterministic (ordinal)
    /// order so building twice from the same layers is byte-identical. Returns
    /// <paramref name="baseFcb"/> unchanged, with no decode/encode round trip, when there is nothing
    /// to splice.
    ///
    /// The reserved id <see cref="WorldSectorLayout.Id"/> is not a fragment but a world sector's
    /// mission-layer placement: it creates the layers it names and moves the entities it lists under
    /// them, which is the one edit no per-entity fragment can express.
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
        byId.Remove(WorldSectorLayout.Id, out string? layoutXml);

        FcbObject root = FcbDocument.Deserialize(baseFcb);
        var remaining = new HashSet<string>(byId.Keys, FcbFragments.IdComparer);

        foreach (FcbFragments.FragmentSlot slot in FcbFragments.Slots(root))
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

        WorldSectorLayout? layout = layoutXml is null ? null : WorldSectorLayout.Parse(layoutXml);

        // An entity this override set also stages content for is not deleted: the two staged files
        // contradict each other, and keeping it is the lesser harm. Whoever assembled the set
        // reports that; here it only has to not act on it.
        Dictionary<string, FcbObject>? layers =
            layout is null ? null : ApplyLayout(root, layout, [.. layout.Contested(byId.Keys)]);

        foreach (string id in remaining.OrderBy(x => x, StringComparer.Ordinal))
        {
            FcbObject addition = FcbXml.FromXml(byId[id]);
            LayerFor(root, layout, layers, addition).Children.Add(addition);
        }

        return FcbDocument.Serialize(root);
    }

    /// <summary>
    /// Creates the mission layers <paramref name="layout"/> declares and moves every entity it lists
    /// under the one it names, returning the sector's layers by path. Entities the layout says
    /// nothing about keep the place the base container gave them.
    /// </summary>
    private static Dictionary<string, FcbObject> ApplyLayout(
        FcbObject root, WorldSectorLayout layout, HashSet<ulong> contested)
    {
        if (root.TypeHash != WorldHashes.WorldSector)
        {
            throw new InvalidDataException(
                $"'{WorldSectorLayout.Id}' describes a world sector's mission layers, and this container is not one.");
        }

        Dictionary<string, FcbObject> layers = LayersByPath(root);

        // Node references rather than the (parent, index) slots they came from: the first move
        // shifts every later index in that layer, so indices taken up front go stale as soon as they
        // are used. Removing by reference does not care where the node ended up.
        Dictionary<ulong, (FcbObject Node, FcbObject Parent)> byEntityId = FcbFragments.Slots(root)
            .ToDictionary(
                s => FcbEntityFields.ReadU64(s.Node, WorldHashes.DisEntityId),
                s => (s.Node, s.Parent));

        foreach (LayerSpec spec in layout.Layers)
        {
            if (!layers.TryGetValue(spec.Path, out FcbObject? target))
            {
                target = BuildLayer(spec);
                root.Children.Insert(InsertionPointFor(root, layers, spec.Before), target);
                layers.Add(spec.Path, target);
            }

            foreach (ulong entityId in spec.Entities)
            {
                // An id naming nothing is left alone rather than refused: it may be an entity this
                // build does not have, or one a sibling fragment is about to add.
                if (!byEntityId.TryGetValue(entityId, out (FcbObject Node, FcbObject Parent) found)
                    || ReferenceEquals(found.Parent, target))
                {
                    continue;
                }

                found.Parent.Children.Remove(found.Node);
                target.Children.Add(found.Node);
            }
        }

        foreach (ulong entityId in layout.Deleted)
        {
            if (!contested.Contains(entityId)
                && byEntityId.TryGetValue(entityId, out (FcbObject Node, FcbObject Parent) doomed))
            {
                doomed.Parent.Children.Remove(doomed.Node);
            }
        }

        foreach (string path in layout.Removed)
        {
            // Only ever an empty grouping. A layer still holding entities is left in place: dropping
            // it would delete content, which no override may do.
            if (layers.TryGetValue(path, out FcbObject? empty) && empty.Children.Count == 0)
            {
                root.Children.Remove(empty);
                layers.Remove(path);
            }
        }

        return layers;
    }

    /// <summary>Where a new node goes: the layer a staged layout files it under, or the shape's own
    /// default (see <see cref="FcbFragments.AppendTarget"/>).</summary>
    private static FcbObject LayerFor(
        FcbObject root, WorldSectorLayout? layout, Dictionary<string, FcbObject>? layers, FcbObject addition)
    {
        if (layout is not null
            && layers is not null
            && layout.LayerOf(FcbEntityFields.ReadU64(addition, WorldHashes.DisEntityId)) is { } path
            && layers.TryGetValue(path, out FcbObject? layer))
        {
            return layer;
        }

        return FcbFragments.AppendTarget(root, addition);
    }

    private static Dictionary<string, FcbObject> LayersByPath(FcbObject root)
    {
        var layers = new Dictionary<string, FcbObject>(StringComparer.OrdinalIgnoreCase);
        foreach (FcbObject child in root.Children)
        {
            if (child.TypeHash == WorldHashes.MissionLayer)
            {
                layers[MissionLayers.NameOf(child)] = child;
            }
        }
        return layers;
    }

    /// <summary>The index a layer named by <paramref name="before"/> sits at, or one past the last
    /// mission layer when it names nothing this container has.</summary>
    private static int InsertionPointFor(FcbObject root, Dictionary<string, FcbObject> layers, string? before)
    {
        if (before is not null && layers.TryGetValue(before, out FcbObject? anchor))
        {
            return root.Children.IndexOf(anchor);
        }

        int last = root.Children.FindLastIndex(c => c.TypeHash == WorldHashes.MissionLayer);
        return last + 1;
    }

    private static FcbObject BuildLayer(LayerSpec spec)
    {
        var layer = new FcbObject { TypeHash = WorldHashes.MissionLayer };
        layer.Values.Add(WorldHashes.TextPathId, FcbWire.NullTerminate(Encoding.UTF8.GetBytes(spec.Path)));
        layer.Values.Add(WorldHashes.PathId, BitConverter.GetBytes(spec.PathId));
        foreach ((uint hash, byte[] bytes) in spec.Values)
        {
            layer.Values[hash] = bytes;
        }
        return layer;
    }
}
