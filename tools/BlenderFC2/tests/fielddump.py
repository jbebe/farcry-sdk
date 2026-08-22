# Dump each corpus file's decoded fields as canonical JSON, one file per line.
#
#   python fielddump.py skeleton [--out PATH]
#
# Scaffolding for the port to C#: a byte-exact round trip cannot catch a
# symmetric misreading, because a reader that swaps two same-width fields and a
# writer that swaps them back still reproduces the file. Comparing the decoded
# meaning against this dump can.
#
# Floats are emitted as their raw bits, so nothing depends on two languages
# agreeing about float formatting. Bulk vertex and index streams are left out:
# the round trip already covers them, and they would dwarf the structure.
#
# This dies with fc2fmt once the C# codecs are trusted.

import json
import os
import struct
import sys

from _corpus import CORPUS, find, require

from fc2fmt.skeleton import SkeletonFile
from fc2fmt.xbg import XbgFile
from fc2fmt.xbm import XbmMaterial

DEFAULT_OUT = os.path.join(CORPUS, "..", "fielddump")


def bits(value):
    """A float as the uint32 it is stored as."""
    return struct.unpack("<I", struct.pack("<f", value))[0]


def bit_list(values):
    return [bits(v) for v in values]


def constraint(c):
    return {"kind": c.kind, "bones": list(c.bones),
            "weights": bit_list(c.weights), "offset": bit_list(c.offset)}


def dump_skeleton(data):
    s = SkeletonFile.parse(data)
    return {
        "file_version": s.file_version,
        "version": s.version,
        "scale_factor": bits(s.scale_factor),
        "common_bone_ids": list(s.common_bone_ids),
        "translation_bone_ids": list(s.translation_bone_ids),
        "lod_masks": [list(mask) for mask in s.lod_masks],
        "bones": [{
            "name": b.name, "name_hash": b.name_hash, "id": b.id, "parent": b.parent,
            "first_child": b.first_child, "next_sibling": b.next_sibling,
            "child_to_parent": bit_list(b.child_to_parent),
            "local_offset": bit_list(b.local_offset), "length": bits(b.length),
            "ori": constraint(b.ori), "pos": constraint(b.pos),
            "animated_translation": b.animated_translation, "body_part": b.body_part,
            "com_weight": bits(b.com_weight), "version": b.version,
        } for b in s.bones],
        "handles": [{
            "id": h.id, "name": h.name, "name_hash": h.name_hash,
            "parent_bone": h.parent_bone, "parent_bone_hash": h.parent_bone_hash,
            "child_to_parent": bit_list(h.child_to_parent),
            "local_offset": bit_list(h.local_offset),
            "parent_to_child": bit_list(h.parent_to_child),
            "local_offset_inverted": bit_list(h.local_offset_inverted),
            "parent_to_child_repeat": bit_list(h.parent_to_child_repeat),
            "version": h.version,
        } for h in s.handles],
    }


def dump_xbg(data):
    """Structure only: the bulk vertex and index blocks are the round trip's job."""
    m = XbgFile.parse(data)
    return {
        "version": m.version,
        "header_words": list(m.header_words),
        "chunks": [{"tag": c.tag, "word0": c.word0, "raw_length": len(c.raw)}
                   for c in m.chunks],
        "materials": list(m.materials),
        "material_word": m.material_word,
        "cluster_word0": m.cluster_word0,
        "lod_words": list(m.lod_words),
        "bbox": bit_list(m.bbox),
        "bsphere": bit_list(m.bsphere),
        "pos_compress": bit_list(m.pos_compress),
        "uv_compress": bit_list(m.uv_compress),
        "bind_matrices": [bit_list(matrix) for matrix in m.bind_matrices],
        "nodes": [{
            "name": n.name, "name_hash": n.name_hash, "first_child": n.first_child,
            "next_sibling": n.next_sibling, "parent": n.parent,
            "rotation": bit_list(n.rotation), "translation": bit_list(n.translation),
            "scale": bit_list(n.scale), "skin_index": n.skin_index,
            "weight": bits(n.weight), "extent": bits(n.extent),
        } for n in m.nodes],
        "part_refs": [{"name_hash": p.name_hash, "node": p.node} for p in m.part_refs],
        "parts": [{
            "name": d.name, "lod_metric": bits(d.lod_metric), "bounds": bit_list(d.bounds),
            "lod": d.lod, "reserved": d.reserved,
            "clusters": [{
                "material_index": c.material_index, "face_count": c.face_count,
                "stride": c.stride, "vertex_count": c.vertex_count, "flags": c.flags,
                "palette": list(c.palette),
            } for c in d.clusters],
        } for d in m.skin_descs],
        "lods": [{
            "distance": bits(lod.distance),
            "vertex_bytes": len(lod.vertex_data), "index_bytes": len(lod.index_data),
            "vertex_buffers": [{"flags": b.flags, "stride": b.stride,
                                "vertex_count": b.vertex_count, "offset": b.offset}
                               for b in lod.vertex_buffers],
            "submeshes": [{"buffer": s.buffer, "part": s.part, "cluster": s.cluster,
                           "index_offset": s.index_offset, "trailing": list(s.trailing)}
                          for s in lod.submeshes],
        } for lod in m.lods],
    }


def entry_value(section, value):
    if section == "textures":
        return {"path": value}
    if section == "floats":
        return {"floats": bit_list(value)}
    return {"integer": value}


def dump_xbm(data):
    x = XbmMaterial.parse(data)
    return {
        "name": x.name, "part": x.part, "shader": x.shader,
        "preamble": list(x.preamble), "trailing": x.trailing,
        "entries": [dict({"section": section, "key": key}, **entry_value(section, value))
                    for section, key, value in x.entries],
    }


FORMATS = {"skeleton": (".skeleton", dump_skeleton),
           "xbg": (".xbg", dump_xbg),
           "xbm": (".xbm", dump_xbm)}


def main(argv):
    if not argv or argv[0] not in FORMATS:
        print("usage: fielddump.py {%s} [--out PATH]" % "|".join(sorted(FORMATS)))
        return 2
    if not require():
        return 0

    suffix, dump = FORMATS[argv[0]]
    out_dir = argv[argv.index("--out") + 1] if "--out" in argv else DEFAULT_OUT
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, argv[0] + ".jsonl")

    written = failed = 0
    with open(out_path, "w", encoding="utf-8", newline="\n") as handle:
        for path in find(suffix):
            relative = os.path.relpath(path, CORPUS).replace("\\", "/").lower()
            try:
                fields = dump(open(path, "rb").read())
            except Exception as error:
                failed += 1
                print("FAIL %s: %s" % (relative, error), file=sys.stderr)
                continue
            handle.write(json.dumps({"path": relative, "fields": fields},
                                    separators=(",", ":"), sort_keys=True) + "\n")
            written += 1

    print("fielddump: %d %s files to %s%s"
          % (written, argv[0], out_path, ", %d failed" % failed if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
