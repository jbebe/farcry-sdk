# Round-trip every retail file of a format through fc2fmt and require the bytes
# back unchanged. A mismatch means the reader dropped something the writer then
# could not restore, so it is a reader bug, not a tolerance to relax.
#
#   python tools/BlenderFC2/tests/roundtrip.py xbg
#   python tools/BlenderFC2/tests/roundtrip.py xbg --corpus D:/other/export
#
# A round trip only proves the framing: bytes the reader keeps as an opaque blob
# pass it for free. `--coverage` reports how much of the corpus is blob, so the
# pass count cannot be mistaken for full understanding.

import argparse
import os
import sys
import traceback

from _corpus import CORPUS, describe_difference, find, require

from fc2fmt import mab
from fc2fmt.mab import SECTION_NEXT_CLIP, MabFile
from fc2fmt.skeleton import SkeletonFile
from fc2fmt.xbg import XbgFile
from fc2fmt.xbm import XbmMaterial

FORMATS = {
    "skeleton": (".skeleton", SkeletonFile),
    "xbg": (".xbg", XbgFile),
    "xbm": (".xbm", XbmMaterial),
    "mab": (".mab", MabFile),
}


# Sections fc2fmt.mab turns into rotations, translations and trajectories.
DECODED_SECTIONS = (mab.SECTION_ROOT_TRANSLATION, mab.SECTION_ROOT_ROTATION,
                    mab.SECTION_CONSTANT_ROTATION, mab.SECTION_KEYFRAME_ROTATION,
                    mab.SECTION_CONSTANT_TRANSLATION, mab.SECTION_ANIMATED_TRANSLATION)


def clip_opaque(clip):
    """Bytes of one clip in a bank that nothing decodes, its children included."""
    decoded = sum(len(clip.section(index) or b"") for index in DECODED_SECTIONS)
    child = clip.section(SECTION_NEXT_CLIP)
    own = len(clip.data) - decoded - len(child or b"")
    return own + (clip_opaque(clip.next_clip()) if child else 0)


def opaque_bytes(model):
    """Bytes nothing in fc2fmt can interpret.

    Blocks stored as bytes but decoded and re-encoded elsewhere do not count: an
    .xbg's vertex and index data goes through fc2fmt.vertex, and a .mab's
    rotation and translation sections through fc2fmt.mab. What is left is the
    chunks and sections still carried verbatim.
    """
    if isinstance(model, XbgFile):
        return sum(len(c.raw) for c in model.chunks)
    if isinstance(model, MabFile):
        return clip_opaque(model)
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("format", choices=sorted(FORMATS))
    ap.add_argument("--corpus", default=CORPUS)
    ap.add_argument("--coverage", action="store_true",
                    help="also report the share of bytes kept as opaque blobs")
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    if not require():
        return 0

    suffix, codec = FORMATS[args.format]
    passed, failures, total_bytes, blob_bytes = 0, [], 0, 0
    for path in find(suffix, args.corpus):
        original = open(path, "rb").read()
        try:
            model = codec.parse(original)
            rewritten = model.write()
        except Exception:
            failures.append((path, traceback.format_exc().strip().splitlines()[-1]))
            continue
        if rewritten != original:
            failures.append((path, describe_difference(original, rewritten)))
            continue
        passed += 1
        total_bytes += len(original)
        blob_bytes += opaque_bytes(model)

    total = passed + len(failures)
    print("%s: %d/%d byte-identical" % (args.format, passed, total))
    if args.coverage and total_bytes:
        print("  %.2f%% of those bytes were carried through as opaque blobs"
              % (100.0 * blob_bytes / total_bytes))
    shown = failures if args.verbose else failures[:20]
    for path, why in shown:
        print("  FAIL %s: %s" % (os.path.relpath(path, args.corpus), why))
    if len(failures) > len(shown):
        print("  ... and %d more" % (len(failures) - len(shown)))
    return 1 if failures or not total else 0


if __name__ == "__main__":
    sys.exit(main())
