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

    # The skeleton says which bones may carry translation at all; a clip must
    # not name any other, which is the cross-check on the translation masks.
    movable = set(skeleton.translation_bone_ids)
    print("translation bones: %s"
          % [by_id[i].name for i in sorted(movable) if i in by_id])

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
        worst_norm = max(worst_norm, check_tracks(clip, stats))
        check_translations(clip, movable, stats)

    for key, value in sorted(stats.items()):
        print("  %-24s %d" % (key, value))
    print("  worst |norm-1|          %.2e" % worst_norm)
    print("most animated bones:", [name for name, _ in animated.most_common(12)])
    return 0 if not (stats["BONE OUT OF RANGE"] or stats["TRANSLATES A FIXED BONE"]
                     or stats["TRANSLATION NOT FINITE"]) and worst_norm < 1e-3 else 1


def check_translations(clip, movable, stats):
    """Only the bones the skeleton frees to move may carry an offset."""
    for source, values in (("constant", clip.constant_translations()),
                           ("animated", clip.translation_tracks())):
        for bone_id, value in values.items():
            stats["TRANSLATES A FIXED BONE" if bone_id not in movable
                  else "%s translation" % source] += 1
            samples = [value] if source == "constant" else [v for _f, v in value]
            if any(abs(c) > 1e6 or c != c for v in samples for c in v):
                stats["TRANSLATION NOT FINITE"] += 1


def check_tracks(clip, stats):
    """Every key must decode, land inside the clip and arrive in frame order."""
    header = clip.keyframe_header()
    if not header:
        return 0.0
    _tracks, last_frame, rate = header
    # The engine indexes the group table with time * rate, so the clip cannot
    # run past the last frame that table describes.
    if rate and clip.duration * rate > last_frame + 1:
        stats["DURATION PAST THE LAST FRAME"] += 1

    worst = 0.0
    for frames in clip.keyframe_tracks().values():
        previous = -1
        for frame, quat in frames:
            stats["keys"] += 1
            if quat is None:
                stats["KEY FAILED TO DECODE"] += 1
                continue
            if frame <= previous:
                stats["KEYS OUT OF ORDER"] += 1
            previous = frame
            worst = max(worst, abs(sum(c * c for c in quat) ** 0.5 - 1.0))
    return worst


if __name__ == "__main__":
    sys.exit(main())
