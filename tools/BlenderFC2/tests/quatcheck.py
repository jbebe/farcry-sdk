# Discriminate the .mab quaternion component layout against the rest pose.
#
#   python tools/BlenderFC2/tests/quatcheck.py
#
# Unit norm cannot tell one component permutation from another, but a bone a
# clip holds CONSTANT should sit at or near its skeleton rest rotation. Scoring
# each candidate layout by |dot| against m_ChildToParent picks the real one out.

import collections
import os
import sys

from _corpus import GRAPHICS, PELVIS_REF, find, require

from fc2fmt.mab import ENGINE_LAYOUT, MabFile
from fc2fmt.skeleton import SkeletonFile

CANDIDATES = {
    "engine": ENGINE_LAYOUT,
    "d always last": ((0, 1, 2, 3),) * 4,
    "d always first": ((3, 0, 1, 2),) * 4,
    "sign bits swapped": tuple(reversed(ENGINE_LAYOUT)),
}


def main():
    if not require():
        return 0
    skeleton = SkeletonFile.parse(open(PELVIS_REF, "rb").read())
    rest = {b.id: b.child_to_parent for b in skeleton.bones}

    clips = list(find(".mab", os.path.join(GRAPHICS, "characters", "_common", "animations")))[:600]
    scores = collections.defaultdict(list)
    for path in clips:
        clip = MabFile.parse(open(path, "rb").read())
        for name, layout in CANDIDATES.items():
            for bone_id, quat in clip.constant_rotations(layout=layout).items():
                if bone_id in rest:
                    scores[name].append(abs(sum(x * y for x, y in zip(quat, rest[bone_id]))))

    print("clips %d" % len(clips))
    print("candidate            mean|dot|  frac>0.99  samples")
    for name in CANDIDATES:
        values = scores[name]
        if not values:
            continue
        print("  %-18s %.4f     %.3f      %d"
              % (name, sum(values) / len(values),
                 sum(1 for v in values if v > 0.99) / len(values), len(values)))

    best = max(scores, key=lambda k: sum(scores[k]) / len(scores[k]))
    print("best: %s" % best)
    return 0 if best == "engine" else 1


if __name__ == "__main__":
    sys.exit(main())
