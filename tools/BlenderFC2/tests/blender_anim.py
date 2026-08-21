# Put a clip on an imported character and check the bones actually land there.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_anim.py
#
# The Action is built by undoing each bone's rest rotation and applying the
# clip's, so the check is the other direction: evaluate the posed armature and
# read each bone's rotation relative to its parent back out. It has to be the
# quaternion the file stores, or the rest composition is wrong.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of these packages an installed extension left.
from _corpus import GRAPHICS, PELVIS_REF, present

import bpy
from mathutils import Quaternion

from addon import import_mab, import_xbg
from fc2fmt.mab import MabFile
from fc2fmt.skeleton import SkeletonFile

CHARACTER = os.path.join(GRAPHICS, "actors", "buddy_andrehyppolite", "andrehyppolite.xbg")
CLIPS = os.path.join(GRAPHICS, "characters", "_common", "animations")
CLIP = os.path.join(CLIPS, "weapons", "primary", "ak47", "1stge_uppb_aimcycle_+000fw_prak4_i1.mab")

TOLERANCE = 1e-4


def fail(message):
    print("FAIL %s" % message)
    return 1


def rest_local(bone):
    if bone.parent:
        return bone.parent.matrix_local.inverted() @ bone.matrix_local
    return bone.matrix_local.copy()


def posed_local(pose_bone):
    """The bone's transform relative to its parent, as posed."""
    if pose_bone.parent:
        return pose_bone.parent.matrix.inverted() @ pose_bone.matrix
    return pose_bone.matrix.copy()


def main():
    if not present() or not os.path.exists(CLIP):
        print("corpus not present, skipping")
        return 0

    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(CHARACTER, lod=0, with_textures=False)
    armature = result["armature"]
    loaded = import_mab.load(CLIP, armature, PELVIS_REF)
    clip = loaded["clip"]
    errors = 0

    print("%s: %d bones posed, %d unmatched, %d keys, duration %.3f"
          % (os.path.basename(CLIP), loaded["bones"], loaded["unmatched"],
             loaded["keys"], clip.duration))
    if loaded["bones"] < 20:
        errors += fail("only %d bones matched the armature" % loaded["bones"])
    if not loaded["keys"]:
        return fail("no keys were inserted")

    skeleton = SkeletonFile.parse(open(PELVIS_REF, "rb").read())
    names = {bone.id: bone.name for bone in skeleton.bones}
    tracks = clip.keyframe_tracks()

    # The .xbg hangs the knee and elbow helpers off the Pelvis while the
    # .skeleton hangs them off the limb. Posed on the .xbg tree they stay by the
    # hip and tear the mesh, so the armature has to be on the skeleton's tree.
    for bone in skeleton.bones:
        pose_bone = armature.pose.bones.get(bone.name)
        wanted = names.get(bone.parent)
        if pose_bone is None or wanted is None or wanted not in armature.pose.bones:
            continue
        got = pose_bone.parent.name if pose_bone.parent else None
        if got != wanted:
            errors += fail("%s hangs off %s, the skeleton says %s"
                           % (bone.name, got, wanted))
    if loaded["rig"]["reparented"]:
        print("reparented onto the skeleton's tree: %s" % loaded["rig"]["reparented"])

    # Sample frames that carry a key, and compare what the rig evaluates to.
    checked = worst = 0
    last = clip.keyframe_header()[1]
    for frame in range(last + 1):
        bpy.context.scene.frame_set(frame + import_mab.FIRST_FRAME)
        for bone_id, frames in tracks.items():
            wanted = next((q for f, q in frames if f == frame), None)
            pose_bone = armature.pose.bones.get(names.get(bone_id, ""))
            if wanted is None or pose_bone is None:
                continue
            got = posed_local(pose_bone).to_quaternion()
            want = Quaternion((wanted[3], wanted[0], wanted[1], wanted[2]))
            # A quaternion and its negation are the same rotation.
            difference = min((got - want).magnitude, (got + want).magnitude)
            worst = max(worst, difference)
            checked += 1
    if not checked:
        errors += fail("no frame carried a key to compare")
    elif worst > TOLERANCE:
        errors += fail("worst bone differs from the file by %.3e over %d samples"
                       % (worst, checked))
    else:
        print("posed rig matches the file: %d samples, worst %.2e" % (checked, worst))

    print("blender anim: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
