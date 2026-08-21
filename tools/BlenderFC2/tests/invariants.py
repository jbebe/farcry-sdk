# Check what a round trip cannot: that the decoded values mean something.
#
#   python tools/BlenderFC2/tests/invariants.py
#
# A round trip passes on any blob the reader echoes, and most of an .xbg is
# blob. These are the invariants an importer and exporter rely on, asserted
# across the whole retail set. Anything derived is also recomputed and required
# to reproduce what shipped.

import collections
import sys

from _corpus import find, require

from fc2fmt.binary import name_hash
from fc2fmt.mab import MabFile, mask_slot
from fc2fmt.mesh import extract
from fc2fmt.skeleton import SkeletonFile
from fc2fmt.vertex import VertexStream, buffer_vertex_count, pack_indices, unpack_indices
from fc2fmt.xbg import EMPTY_SLOT, NO_NODE, NO_PLACEMENT, XbgFile, vertex_layout


def check_xbg(model, fail):
    skinning = [n for n in model.nodes if n.skin_index != EMPTY_SLOT]
    if sorted(n.skin_index for n in skinning) != list(range(len(skinning))):
        fail("skin_index is not a permutation of 0..n-1")
    if model.bind_matrices and len(model.bind_matrices) != len(skinning):
        fail("MB2O count %d != %d skinning nodes" % (len(model.bind_matrices), len(skinning)))

    for i, node in enumerate(model.nodes):
        if node.parent != NO_NODE and node.parent >= len(model.nodes):
            fail("node %d parent out of range" % i)
        if i and name_hash(node.name) != node.name_hash:
            fail("node %r name hash mismatch" % node.name)

    # DIKS names every part exactly once and says which node places it, so an
    # importer never has to match part names against node names.
    if len(model.part_refs) != len(model.skin_descs):
        fail("DIKS has %d entries for %d parts" % (len(model.part_refs), len(model.skin_descs)))
    hashes = {name_hash(desc.name) for desc in model.skin_descs}
    for ref in model.part_refs:
        if ref.name_hash not in hashes:
            fail("DIKS entry %d names no part" % ref.name_hash)
        if ref.node != NO_PLACEMENT and ref.node >= len(model.nodes):
            fail("DIKS entry names node %d, past the node array" % ref.node)
    for desc in model.skin_descs:
        if any(c.is_skinned for c in desc.clusters) and model.part_node(desc.name) is not None:
            fail("skinned part %r is given a placement node" % desc.name)
        for cluster in desc.clusters:
            slots = cluster.bones()
            if not cluster.is_skinned:
                if slots:
                    fail("static cluster in %r has a non-empty palette" % desc.name)
                continue
            if not slots:
                fail("skinned cluster in %r has an empty palette" % desc.name)
            elif max(slots) >= len(model.nodes):
                fail("palette in %r indexes past the node array" % desc.name)
            elif any(model.nodes[s].skin_index == EMPTY_SLOT for s in slots):
                fail("palette in %r names a non-skinning node" % desc.name)
            if any(slot != EMPTY_SLOT for slot in cluster.palette[len(slots):]):
                fail("palette in %r is not a contiguous prefix" % desc.name)

    links = [(n.first_child, n.next_sibling, n.skin_index) for n in model.nodes]
    model.rebuild_hierarchy()
    if links != [(n.first_child, n.next_sibling, n.skin_index) for n in model.nodes]:
        fail("rebuild_hierarchy disagrees with the shipped links")

    for index, lod in enumerate(model.lods):
        for slot, buffer in enumerate(lod.vertex_buffers):
            _offsets, stride = vertex_layout(buffer.flags)
            if stride != buffer.stride:
                fail("LOD %d flags %#x imply stride %d, file says %d"
                     % (index, buffer.flags, stride, buffer.stride))
                continue
            count = buffer_vertex_count(lod, slot)
            stream = VertexStream.unpack(lod.vertex_data, buffer, count)
            original = lod.vertex_data[buffer.offset:buffer.offset + count * buffer.stride]
            if stream.pack() != original:
                fail("LOD %d buffer %d does not survive unpack/pack" % (index, slot))
        if pack_indices(unpack_indices(lod)) != lod.index_data:
            fail("LOD %d indices do not survive unpack/pack" % index)

    # Decode, place, and require the result to land inside the bounds the file
    # ships. This is the end-to-end check: quantisation, pivots and the skinned
    # root all have to be right for it to hold. Two destructible variants
    # (urbanmedium00_gazebo02_part02_bk, buddytable_flip_bk) ship bounds a few
    # centimetres tighter than their own geometry, hence the relative slack.
    if model.lods and model.bbox:
        points = [p for part in extract(model, 0) for p in part.positions]
        for axis in range(3) if points else ():
            span = model.bbox[axis + 3] - model.bbox[axis]
            slack = model.pos_scale * 2 + abs(span) * 0.05 + 1e-3
            low = min(p[axis] for p in points)
            high = max(p[axis] for p in points)
            if low < model.bbox[axis] - slack or high > model.bbox[axis + 3] + slack:
                fail("placed LOD0 exceeds XOBB on axis %d: %.3f..%.3f vs %.3f..%.3f"
                     % (axis, low, high, model.bbox[axis], model.bbox[axis + 3]))


def check_skeleton(skeleton, fail):
    for bone in skeleton.bones:
        if name_hash(bone.name) != bone.name_hash:
            fail("bone %r name hash mismatch" % bone.name)
        if bone.parent != 0xFFFF and bone.parent >= len(skeleton.bones):
            fail("bone %r parent out of range" % bone.name)
    for handle in skeleton.handles:
        if not skeleton.bone_by_name(handle.parent_bone):
            fail("handle %r names unknown bone %r" % (handle.name, handle.parent_bone))

    links = [(b.first_child, b.next_sibling) for b in skeleton.bones]
    skeleton.rebuild_hierarchy()
    if links != [(b.first_child, b.next_sibling) for b in skeleton.bones]:
        fail("rebuild_hierarchy disagrees with the shipped links")


def check_mab(clip, fail):
    """The readers index sections by ordinal; prove that equals the popcount rule."""
    for mask, bones in ((clip.constant_mask, clip.constant_bones()),
                        (clip.keyframe_mask, clip.keyframed_bones())):
        for ordinal, bone_id in enumerate(bones):
            if mask_slot(mask, bone_id) != ordinal:
                fail("bone %d slot %r != ordinal %d"
                     % (bone_id, mask_slot(mask, bone_id), ordinal))
                return


def main():
    if not require():
        return 0
    stats, failures = collections.Counter(), []

    for path in find(".xbg"):
        model = XbgFile.parse(open(path, "rb").read())
        stats["xbg files"] += 1
        stats["nodes"] += len(model.nodes)
        stats["clusters"] += sum(len(d.clusters) for d in model.skin_descs)
        stats["skinned xbg"] += 1 if model.bind_matrices else 0
        check_xbg(model, lambda why, p=path: failures.append((p, why)))

    for path in find(".skeleton"):
        skeleton = SkeletonFile.parse(open(path, "rb").read())
        stats["skeletons"] += 1
        stats["bones"] += len(skeleton.bones)
        check_skeleton(skeleton, lambda why, p=path: failures.append((p, why)))

    for path in find(".mab"):
        clip = MabFile.parse(open(path, "rb").read())
        stats["clips"] += 1
        check_mab(clip, lambda why, p=path: failures.append((p, why)))

    for key, value in sorted(stats.items()):
        print("  %-14s %d" % (key, value))
    print("invariant failures: %d" % len(failures))
    for path, why in failures[:20]:
        print("  FAIL %s: %s" % (path, why))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
