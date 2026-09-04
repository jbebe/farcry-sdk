using System.Globalization;
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
    /// The reserved id <see cref="ContainerLayout.Id"/> is not a fragment but a world sector's
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
        byId.Remove(ContainerLayout.Id, out string? layoutXml);

        FcbObject root = FcbDocument.Deserialize(baseFcb);
        var remaining = new HashSet<string>(byId.Keys, FcbFragments.IdComparer);

        List<FcbFragments.FragmentSlot> slots = FcbFragments.Slots(root);
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

        ContainerLayout? layout = layoutXml is null ? null : ContainerLayout.Parse(layoutXml);

        // An entity this override set also stages content for is not deleted: the two staged files
        // contradict each other, and keeping it is the lesser harm. Whoever assembled the set
        // reports that; here it only has to not act on it.
        Dictionary<string, FcbObject>? layers =
            layout is null ? null : ApplyLayout(root, layout, slots, [.. layout.Contested(byId.Keys)]);

        foreach (string id in remaining.OrderBy(x => x, StringComparer.Ordinal))
        {
            FcbObject addition = FcbXml.FromXml(byId[id]);
            LayerFor(root, layout, layers, addition).Children.Add(addition);
        }

        if (layout is not null && layers is not null)
        {
            Arrange(layout, layers);
        }

        return FcbDocument.Serialize(root);
    }

    /// <summary>
    /// Creates the mission layers <paramref name="layout"/> declares and moves every entity it lists
    /// under the one it names, returning the sector's layers by path. Entities the layout says
    /// nothing about keep the place the base container gave them.
    /// </summary>
    private static Dictionary<string, FcbObject> ApplyLayout(
        FcbObject root, ContainerLayout layout, List<FcbFragments.FragmentSlot> slots,
        HashSet<ulong> contested)
    {
        if (!FcbFragments.IsLayerBearing(root))
        {
            throw new InvalidDataException(
                $"'{ContainerLayout.Id}' describes mission layers, and this container places none.");
        }

        Dictionary<string, FcbObject> layers = LayersByKey(root);

        // Node references rather than the (parent, index) slots they came from: the first move
        // shifts every later index in that layer, so indices taken up front go stale as soon as they
        // are used. Removing by reference does not care where the node ended up.
        Dictionary<ulong, (FcbObject Node, FcbObject Parent)> byEntityId = slots
            .ToDictionary(
                s => FcbEntityFields.ReadU64(s.Node, WorldHashes.DisEntityId),
                s => (s.Node, s.Parent));

        foreach (LayerSpec spec in layout.Layers)
        {
            if (!layers.TryGetValue(spec.Key, out FcbObject? target))
            {
                FcbObject parent = CellFor(root, spec.Under);
                target = BuildLayer(spec);
                parent.Children.Insert(InsertionPointFor(parent, layers, spec), target);
                layers.Add(spec.Key, target);
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

        foreach (string key in layout.Removed)
        {
            // Only ever an empty grouping. A layer still holding entities is left in place: dropping
            // it would delete content, which no override may do.
            if (layers.TryGetValue(key, out FcbObject? empty) && empty.Children.Count == 0)
            {
                foreach (FcbObject parent in FcbFragments.LayerParentsOf(root))
                {
                    parent.Children.Remove(empty);
                }
                layers.Remove(key);
            }
        }

        return layers;
    }

    /// <summary>Puts each listed layer's entities in the order the layout gives, once every addition
    /// has landed - a moved entity is appended, so without this it sits behind the ones that stayed.
    /// Only a layer the layout accounts for entirely is touched.</summary>
    private static void Arrange(ContainerLayout layout, Dictionary<string, FcbObject> layers)
    {
        foreach (LayerSpec spec in layout.Layers)
        {
            if (!layers.TryGetValue(spec.Key, out FcbObject? layer)
                || layer.Children.Count != spec.Entities.Count
                || !NeedsArranging(layer, spec.Entities))
            {
                continue;
            }

            var wanted = new Dictionary<ulong, int>(spec.Entities.Count);
            for (int i = 0; i < spec.Entities.Count; i++)
            {
                wanted[spec.Entities[i]] = i;
            }

            List<FcbObject> ordered = [.. layer.Children.OrderBy(c =>
                wanted.TryGetValue(FcbEntityFields.ReadU64(c, WorldHashes.DisEntityId), out int at)
                    ? at
                    : int.MaxValue)];
            layer.Children.Clear();
            layer.Children.AddRange(ordered);
        }
    }

    /// <summary>
    /// Whether reordering this layer is both safe and necessary, answered in one allocation-free
    /// pass. A child that is not an addressable entity makes it unsafe - the layout accounts for
    /// entities only, and world2's mapsdata has a <c>BindingHierarchy</c> sitting among them - and a
    /// layer already in the wanted order, which is the common case, makes it unnecessary.
    /// </summary>
    private static bool NeedsArranging(FcbObject layer, IReadOnlyList<ulong> wanted)
    {
        bool ordered = true;
        for (int i = 0; i < wanted.Count; i++)
        {
            FcbObject child = layer.Children[i];
            if (child.TypeHash != WorldHashes.Entity)
            {
                return false;
            }
            ordered &= FcbEntityFields.ReadU64(child, WorldHashes.DisEntityId) == wanted[i];
        }

        return !ordered;
    }

    /// <summary>Where a new node goes: the layer a staged layout files it under, or the shape's own
    /// default (see <see cref="FcbFragments.AppendTarget"/>).</summary>
    private static FcbObject LayerFor(
        FcbObject root, ContainerLayout? layout, Dictionary<string, FcbObject>? layers, FcbObject addition)
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

    /// <summary>Every mission layer under the key a layout addresses it by - qualified by its level
    /// cell where the container groups layers into cells (see <see cref="LayerSpec.Key"/>).</summary>
    /// <summary>Every mission layer under the key a layout addresses it by (see
    /// <see cref="LayerSpec.Key"/>).</summary>
    private static Dictionary<string, FcbObject> LayersByKey(FcbObject root)
    {
        var layers = new Dictionary<string, FcbObject>(StringComparer.OrdinalIgnoreCase);
        foreach ((string? under, FcbObject layer) in FcbFragments.KeyedLayersOf(root))
        {
            layers[LayerSpec.KeyOf(under, MissionLayers.NameOf(layer))] = layer;
        }
        return layers;
    }

    /// <summary>The node a new layer hangs off: the level cell the spec names, or the root. A cell the
    /// container doesn't have falls back to the root rather than inventing one.</summary>
    private static FcbObject CellFor(FcbObject root, string? under)
        => under is not null
           && uint.TryParse(under, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint cell)
            ? FcbFragments.LayerParentsOf(root).FirstOrDefault(p => p.TypeHash == cell) ?? root
            : root;

    /// <summary>The index within <paramref name="parent"/> that the spec's <c>before</c> layer sits
    /// at, or one past the last mission layer there when it names nothing.</summary>
    private static int InsertionPointFor(
        FcbObject parent, Dictionary<string, FcbObject> layers, LayerSpec spec)
    {
        if (spec.Before is { } before
            && layers.TryGetValue(LayerSpec.KeyOf(spec.Under, before), out FcbObject? anchor))
        {
            int at = parent.Children.IndexOf(anchor);
            if (at >= 0)
            {
                return at;
            }
        }

        int last = parent.Children.FindLastIndex(c => c.TypeHash == WorldHashes.MissionLayer);
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
