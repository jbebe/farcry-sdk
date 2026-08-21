# Dump one file's structure, for when a round trip fails and you need to see
# which chunk drifted.
#
#   python tools/BlenderFC2/tests/probe.py some.xbg

import sys
import traceback

from _corpus import describe_difference

from fc2fmt.xbg import XbgFile


def main(path):
    data = open(path, "rb").read()
    try:
        model = XbgFile.parse(data)
    except Exception:
        traceback.print_exc()
        return 1

    print("size %d  version %#x" % (len(data), model.version))
    for chunk in model.chunks:
        print("  %-8r word0=%#x  opaque=%d" % (chunk.tag, chunk.word0, len(chunk.raw)))
    print("nodes %d  materials %d  lods %d  parts %d  clusters %d"
          % (len(model.nodes), len(model.materials), len(model.lods), len(model.skin_descs),
             sum(len(d.clusters) for d in model.skin_descs)))

    rewritten = model.write()
    if rewritten == data:
        print("round trip: byte-identical")
        return 0
    print("round trip: %s" % describe_difference(data, rewritten))
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
