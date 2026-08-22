# Build every retail .xbg from decoded content alone and require the file back.
#
#   python originate.py [--limit N]
#
# The round trip echoes whatever it parsed. This constructs a brand-new XbgFile
# holding only what a format-free pack carries - names, transforms, bounds,
# materials, palettes and geometry - and derives every structural field.
# Passing means a container can be authored rather than edited, which is what
# lets a model gain a part or an LOD instead of being transplanted into a donor.
#
# Two fields survive here because nothing derives them: header_words[0] and the
# LTMR trailing word. docs/docs/file-formats/xbm-xbg.md has the full table.

import sys

from _corpus import describe_difference, find, require

from fc2fmt.binary import name_hash
from fc2fmt.geometry import read_lod, write_lod
from fc2fmt.mesh import lod_tier
from fc2fmt.xbg import (LOD_WORD1, Chunk, Cluster, Lod, Node, PartRef, SkinDesc,
                        Submesh, VertexBuffer, XbgFile)


def originate(src):
    """A new XbgFile holding src's content, with every derivable field derived."""
    out = XbgFile()
    out.header_words = [src.header_words[0], 0, 0, 0, 0]
    out.nodes = [_node(node, index) for index, node in enumerate(src.nodes)]
    out.rebuild_hierarchy()
    out.bind_matrices = [list(matrix) for matrix in src.bind_matrices]
    out.materials = list(src.materials)
    # Zero on all but nineteen grass meshes, with nothing to say which.
    out.material_word = src.material_word
    out.skin_descs = [_part(desc) for desc in src.skin_descs]
    out.cluster_word0 = 1
    # DIKS is one entry per part, in part order, keyed by the part's name.
    out.part_refs = [PartRef(name_hash(desc.name), ref.node)
                     for desc, ref in zip(src.skin_descs, src.part_refs)]
    out.bbox = list(src.bbox)
    out.bsphere = list(src.bsphere)
    out.lod_words = [len(src.lods), LOD_WORD1]
    out.pos_compress = list(src.pos_compress)
    out.uv_compress = list(src.uv_compress)
    out.lods = [_lod(lod) for lod in src.lods]
    out.chunks = [Chunk(chunk.tag, 1, chunk.raw) for chunk in src.chunks]

    for source, target in zip(src.lods, out.lods):
        write_lod(out, target, read_lod(src, source))
    return out.write()


def _node(node, index):
    """The root carries a zero hash; every other node hashes its own name."""
    return Node(name=node.name, name_hash=0 if index == 0 else name_hash(node.name),
                first_child=0, next_sibling=0, parent=node.parent,
                rotation=list(node.rotation), translation=list(node.translation),
                scale=list(node.scale), skin_index=node.skin_index,
                weight=1.0, extent=node.extent)


def _part(desc):
    fresh = SkinDesc(name=desc.name, lod_metric=desc.lod_metric, reserved=0,
                     bounds=tuple(desc.bounds), lod=lod_tier(desc.name, desc.lod))
    fresh.clusters = [Cluster(material_index=cluster.material_index, face_count=0,
                              stride=0, vertex_count=0, flags=cluster.flags,
                              palette=list(cluster.palette))
                      for cluster in desc.clusters]
    return fresh


def _lod(lod):
    return Lod(lod.distance,
               [VertexBuffer(vb.flags, vb.stride, 0, 0) for vb in lod.vertex_buffers],
               [Submesh(sm.buffer, sm.part, sm.cluster, 0, [0, 0, 0]) for sm in lod.submeshes],
               b"", b"")


def main(argv):
    if not require():
        return 0
    limit = int(argv[argv.index("--limit") + 1]) if "--limit" in argv else None

    checked = failures = 0
    for path in find(".xbg"):
        if limit is not None and checked >= limit:
            break
        data = open(path, "rb").read()
        checked += 1
        try:
            produced = originate(XbgFile.parse(data))
        except Exception as error:
            failures += 1
            print("FAIL %s: %s" % (path, error))
            continue
        if produced != data:
            failures += 1
            print("FAIL %s: %s" % (path, describe_difference(data, produced)))

    print("originate: %d/%d files byte-identical from decoded content alone"
          % (checked - failures, checked))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
