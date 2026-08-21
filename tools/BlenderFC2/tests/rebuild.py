# Rebuild every LOD's geometry from scratch and require the file back, byte for byte.
#
#   python rebuild.py [--limit N]
#
# The plain round trip echoes the vertex and index blocks, so it says nothing
# about whether an exporter could produce them. This decomposes every LOD into
# per-cluster geometry and reassembles it, regenerating every buffer offset,
# index offset, face count and vertex count on the way. A writer that cannot
# reproduce an untouched file is not going to be trusted with an edited one.

import sys

from _corpus import describe_difference, find, require

from fc2fmt.geometry import read_lod, write_lod
from fc2fmt.xbg import XbgFile


def rebuilt(data):
    """Parse, throw the geometry blocks away, rebuild them, and write."""
    model = XbgFile.parse(data)
    for lod in model.lods:
        geometries = read_lod(model, lod)
        lod.vertex_data = b""
        lod.index_data = b""
        for buffer in lod.vertex_buffers:
            buffer.offset = -1
        write_lod(model, lod, geometries)
    return model.write()


def main(argv):
    if not require():
        return 0
    limit = int(argv[argv.index("--limit") + 1]) if "--limit" in argv else None

    checked = failures = clusters = 0
    for path in find(".xbg"):
        if limit is not None and checked >= limit:
            break
        data = open(path, "rb").read()
        checked += 1
        try:
            produced = rebuilt(data)
        except Exception as error:
            failures += 1
            print("FAIL %s: %s" % (path, error))
            continue
        if produced != data:
            failures += 1
            print("FAIL %s: %s" % (path, describe_difference(data, produced)))
        else:
            clusters += sum(len(lod.submeshes) for lod in XbgFile.parse(data).lods)

    print("rebuild: %d/%d files byte-identical from regenerated geometry (%d clusters)"
          % (checked - failures, checked, clusters))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
