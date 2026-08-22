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
from fc2fmt import mab
from fc2fmt.mab import MabFile, mask_bones, mask_slot
from fc2fmt.mesh import extract
from fc2fmt.skeleton import SkeletonFile
from fc2fmt.vertex import VertexStream, pack_indices, unpack_indices
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
            count = buffer.vertex_count
            # The buffer states its own vertex count; walking to where the next
            # buffer starts has to agree, or the block is not a concatenation.
            following = [b.offset for b in lod.vertex_buffers if b.offset > buffer.offset]
            end = min(following) if following else len(lod.vertex_data)
            if (end - buffer.offset) // buffer.stride != count:
                fail("LOD %d buffer %d says %d vertices, its extent holds %d"
                     % (index, slot, count, (end - buffer.offset) // buffer.stride))
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


# Each array section, the mask that sizes it, and the bytes one entry takes.
MAB_SECTIONS = (
    (mab.SECTION_CONSTANT_ROTATION, mab.MASK_CONSTANT_ROTATION, mab.QUAT_BYTES, False),
    (mab.SECTION_CONSTANT_TRANSLATION, mab.MASK_CONSTANT_TRANSLATION, mab.VEC3_BYTES, False),
    (mab.SECTION_ANIMATED_TRANSLATION, mab.MASK_ANIMATED_TRANSLATION, mab.VEC3_BYTES, True),
    (mab.SECTION_ROOT_TRANSLATION, None, mab.VEC3_BYTES, True),
    (mab.SECTION_ROOT_ROTATION, None, mab.QUAT_BYTES, True),
)

# What alignment padding may leave unused at the end of a section.
MAB_ALIGNMENT = 16

# The exporter writes the tag array even when it is empty, and then leaves the
# table slot at zero, so those 16 bytes trail the animated translations.
MAB_EMPTY_TAGS = 16


def check_mab(bank, fail):
    """The readers index sections by ordinal and size them off the masks.

    Prove both: that ordinal equals the popcount rule, and that the entries each
    section is then read to hold end inside it, with only padding to spare.
    """
    for depth, clip in enumerate(bank.clips()):
        where = "clip %d: " % depth
        for index, mask in enumerate(clip.masks):
            for ordinal, bone_id in enumerate(mask_bones(mask)):
                if mask_slot(mask, bone_id) != ordinal:
                    fail("%smask %d bone %d slot %r != ordinal %d"
                         % (where, index, bone_id, mask_slot(mask, bone_id), ordinal))
                    return

        for section, mask, stride, dense in MAB_SECTIONS:
            block = clip.section(section)
            header = clip.track_header(section)
            if not block or not header:
                continue
            count, last_frame, _rate = header
            if mask is not None and count != len(mask_bones(clip.masks[mask])):
                fail("%ssection %d holds %d entries for %d bones in its mask"
                     % (where, section, count, len(mask_bones(clip.masks[mask]))))
            needed = mab.TRACK_HEADER + count * stride * (last_frame + 1 if dense else 1)
            spare = MAB_ALIGNMENT
            if section == mab.SECTION_ANIMATED_TRANSLATION and not clip.sections[mab.SECTION_TAGS]:
                spare += MAB_EMPTY_TAGS
            if not 0 <= len(block) - needed < spare:
                fail("%ssection %d needs %d bytes of the %d it was given"
                     % (where, section, needed, len(block)))

        for bone_id, quat in clip.constant_rotations().items():
            if abs(sum(c * c for c in quat) ** 0.5 - 1.0) > 1e-3:
                fail("%sconstant rotation for bone %d is not a rotation" % (where, bone_id))
                break

    check_participants(bank, fail)


def _clip_identity(clip):
    return clip.masks, clip.sections, clip.duration, len(clip.data)


def check_participants(bank, fail):
    """The tag table is the participant index.

    One record per chained clip, each naming the thing it animates and the bone
    that thing hangs from, and pointing at the clip that drives it. An importer
    reads the attachment straight out of this, so all three have to hold.
    """
    chain = bank.clips()
    block = bank.section(mab.SECTION_TAGS)
    pairs = bank.participant_clips()
    if len(pairs) != len(chain) - 1:
        fail("%d tag records for %d chained clips" % (len(pairs), len(chain) - 1))
        return
    for index, (part, clip) in enumerate(pairs):
        if _clip_identity(clip) != _clip_identity(chain[index + 1]):
            fail("tag record %d does not point at chained clip %d" % (index, index + 1))
        base = mab.TAG_COUNT_BYTES + index * mab.TAG_STRIDE
        slots = (part.name, part.parent, "", part.reference)
        for at, name in zip(mab.TAG_NAMES, slots):
            if mab.tag_hash(block, base + at) != name_hash(name):
                fail("tag record %d slot %#x holds %r, which is not its hash"
                     % (index, at, name))


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
        stats["clips"] += len(clip.clips())
        stats["banks"] += 1
        check_mab(clip, lambda why, p=path: failures.append((p, why)))

    for key, value in sorted(stats.items()):
        print("  %-14s %d" % (key, value))
    print("invariant failures: %d" % len(failures))
    for path, why in failures[:20]:
        print("  FAIL %s: %s" % (path, why))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
