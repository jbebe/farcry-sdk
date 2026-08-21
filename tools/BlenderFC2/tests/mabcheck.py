# Validate the .mab decode against the skeleton the clips are authored for.
#
#   python tools/BlenderFC2/tests/mabcheck.py
#
# Character clips all target characters/_common/pelvis_ref.skeleton, so every
# mask bit must name a bone in it and every rotation must decode to unit length.

import collections
import os
import sys

from _corpus import GRAPHICS, PELVIS_REF, find, require

from fc2fmt.mab import MabFile
from fc2fmt.skeleton import SkeletonFile


def main():
    if not require():
        return 0
    skeleton = SkeletonFile.parse(open(PELVIS_REF, "rb").read())
    by_id = {b.id: b for b in skeleton.bones}
    print("pelvis_ref.skeleton: %d bones" % len(skeleton.bones))

    stats, animated, worst_norm = collections.Counter(), collections.Counter(), 0.0
    for path in find(".mab", os.path.join(GRAPHICS, "characters", "_common", "animations")):
        clip = MabFile.parse(open(path, "rb").read())
        stats["clips"] += 1
        for bone_id in clip.constant_bones() + clip.keyframed_bones():
            stats["bone in skeleton" if bone_id in by_id else "BONE OUT OF RANGE"] += 1
        for quat in clip.constant_rotations().values():
            worst_norm = max(worst_norm, abs(sum(c * c for c in quat) ** 0.5 - 1.0))
            stats["rotation decoded"] += 1
        for bone_id in clip.keyframed_bones():
            animated[by_id[bone_id].name if bone_id in by_id else "?%d" % bone_id] += 1
        stats["keyframe header present" if clip.keyframe_header() else "no keyframe block"] += 1

    for key, value in sorted(stats.items()):
        print("  %-24s %d" % (key, value))
    print("  worst |norm-1|          %.2e" % worst_norm)
    print("most animated bones:", [name for name, _ in animated.most_common(12)])
    return 0 if not stats["BONE OUT OF RANGE"] and worst_norm < 1e-3 else 1


if __name__ == "__main__":
    sys.exit(main())
