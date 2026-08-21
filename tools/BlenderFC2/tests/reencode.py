# Decode every vertex buffer to float space, encode it back, compare bytes.
#
#   python reencode.py [--limit N]
#
# rebuild.py proves the container arithmetic while carrying vertex bytes
# through untouched. This proves the other half: that the float-space values an
# editor is handed quantise back to exactly what shipped. Anything that fails
# here is a component an exporter would silently corrupt.

import collections
import sys

from _corpus import find, require

from fc2fmt.encode import Layout, encode
from fc2fmt.vertex import VertexStream, buffer_vertex_count
from fc2fmt.xbg import BONE_WTS1, COLOR, NORMAL, UV0, UV1, XbgFile


def check(model, lod, index, damage):
    """Round-trip one buffer through float space and record what moved."""
    buffer = lod.vertex_buffers[index]
    count = buffer_vertex_count(lod, index)
    if not count:
        return
    stream = VertexStream.unpack(lod.vertex_data, buffer, count)
    layout = Layout.of(model)

    produced = encode(
        buffer.flags, count, layout, stream,
        positions=stream.positions(model.pos_scale),
        uvs=stream.uvs(model.uv_translate, model.uv_scale, 0) if buffer.flags & UV0 else None,
        uvs1=stream.uvs(model.uv_translate, model.uv_scale, 1) if buffer.flags & UV1 else None,
        normals=stream.normals() if buffer.flags & NORMAL else None,
        colours=stream.colors() if buffer.flags & COLOR else None,
        skin=stream.skin() if buffer.flags & BONE_WTS1 else None)

    for name, original in stream.components.items():
        got = produced.components[name]
        for vertex, (a, b) in enumerate(zip(original, got)):
            if a != b:
                damage[name] += 1
                break


def main(argv):
    if not require():
        return 0
    limit = int(argv[argv.index("--limit") + 1]) if "--limit" in argv else None

    damage = collections.Counter()
    buffers = files = 0
    for path in find(".xbg"):
        if limit is not None and files >= limit:
            break
        files += 1
        model = XbgFile.parse(open(path, "rb").read())
        for lod in model.lods:
            for index in range(len(lod.vertex_buffers)):
                buffers += 1
                check(model, lod, index, damage)

    print("re-encode: %d buffers in %d files" % (buffers, files))
    if damage:
        for name, count in damage.most_common():
            print("   %-12s differs in %d buffers" % (name, count))
    else:
        print("   every component quantises back to what shipped")
    return 1 if damage else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
