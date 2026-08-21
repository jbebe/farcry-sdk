# Put a clip on an imported character and check the bones actually land there.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_anim.py
#
# The Action is built by undoing each bone's rest transform and applying the
# clip's, so the check is the other direction: evaluate the posed armature and
# read each bone's rotation and offset relative to its parent back out. Both have
# to be what the file stores, or the rest composition is wrong.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of these packages an installed extension left.
from _corpus import GRAPHICS, PELVIS_REF, present

import bpy
from mathutils import Quaternion, Vector

from addon import import_mab, import_xbg
from fc2fmt.skeleton import SkeletonFile

CHARACTER = os.path.join(GRAPHICS, "actors", "buddy_andrehyppolite", "andrehyppolite.xbg")
AK47 = os.path.join(GRAPHICS, "weapons", "primary", "ak47", "ak47.xbg")
AK47_REF = os.path.join(GRAPHICS, "weapons", "primary", "ak47", "ak47_ref.skeleton")
CLIPS = os.path.join(GRAPHICS, "characters", "_common", "animations")
AK47_CLIPS = os.path.join(CLIPS, "weapons", "primary", "ak47")

# An upper-body clip, which holds its offsets constant; a full-body jump, which
# drives the Pelvis along a translation track; and the same reload read twice,
# once for the character and once for the weapon clip chained behind it.
CASES = (
    (CHARACTER, PELVIS_REF, os.path.join(AK47_CLIPS, "1stge_uppb_aimcycle_+000fw_prak4_i1.mab")),
    (CHARACTER, PELVIS_REF,
     os.path.join(CLIPS, "locomotion", "stand", "jump", "3rdge_fulb_jump_+000fw_nowep_i1.mab")),
    (CHARACTER, PELVIS_REF, os.path.join(AK47_CLIPS, "1stge_uppb_reload_+000fw_prak4_i1.mab")),
    (AK47, AK47_REF, os.path.join(AK47_CLIPS, "1stge_uppb_reload_+000fw_prak4_i1.mab")),
)

TOLERANCE = 1e-4


def fail(message):
    print("FAIL %s" % message)
    return 1


def posed_local(pose_bone):
    """The bone's transform relative to its parent, as posed."""
    if pose_bone.parent:
        return pose_bone.parent.matrix.inverted() @ pose_bone.matrix
    return pose_bone.matrix.copy()


def check(model, skeleton_path, path):
    skeleton = SkeletonFile.parse(open(skeleton_path, "rb").read())
    names = {bone.id: bone.name for bone in skeleton.bones}

    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(model, lod=0, with_textures=False)
    armature = result["armature"]
    loaded = import_mab.load(path, armature, skeleton_path)
    clip = loaded["clip"]
    errors = 0

    print("%s on %s: %d bones posed, %d moved, %d unmatched, %d keys, duration %.3f"
          % (os.path.basename(path), os.path.basename(skeleton_path), loaded["bones"],
             loaded["moved"], loaded["unmatched"], loaded["keys"], clip.duration))
    if loaded["bones"] < min(len(skeleton.bones), 20) // 2:
        errors += fail("only %d bones matched the armature" % loaded["bones"])
    if not loaded["keys"]:
        return fail("no keys were inserted")
    if clip.bone_ids() and clip.bone_ids()[-1] >= len(skeleton.bones):
        errors += fail("the chosen clip addresses bone %d, past the %d-bone skeleton"
                       % (clip.bone_ids()[-1], len(skeleton.bones)))

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

    rotations = {(bone, frame): quat
                 for bone, frames in clip.keyframe_tracks().items()
                 for frame, quat in frames}
    offsets = {(bone, frame): value
               for bone, frames in clip.translation_tracks().items()
               for frame, value in frames}
    offsets.update({(bone, 0): value
                    for bone, value in clip.constant_translations().items()})
    if not offsets and skeleton.translation_bone_ids:
        errors += fail("the clip carries no translation to check")

    # Sample every frame and compare what the rig evaluates to.
    checked = worst = worst_offset = 0
    for frame in range(clip.keyframe_header()[1] + 1):
        bpy.context.scene.frame_set(frame + import_mab.FIRST_FRAME)
        for bone_id, name in names.items():
            pose_bone = armature.pose.bones.get(name)
            if pose_bone is None:
                continue
            local = posed_local(pose_bone)
            wanted = rotations.get((bone_id, frame))
            if wanted is not None:
                got = local.to_quaternion()
                want = Quaternion((wanted[3], wanted[0], wanted[1], wanted[2]))
                # A quaternion and its negation are the same rotation.
                worst = max(worst, min((got - want).magnitude, (got + want).magnitude))
                checked += 1
            moved = offsets.get((bone_id, frame))
            if moved is not None:
                worst_offset = max(
                    worst_offset, (local.to_translation() - Vector(moved)).length)
                checked += 1

    if not checked:
        errors += fail("no frame carried a key to compare")
    elif worst > TOLERANCE or worst_offset > TOLERANCE:
        errors += fail("differs from the file by %.3e rotation / %.3e offset over %d samples"
                       % (worst, worst_offset, checked))
    else:
        print("  matches the file: %d samples, worst %.2e rotation, %.2e offset"
              % (checked, worst, worst_offset))
    return errors


def check_skeleton_discovery():
    """With no skeleton named, the clip has to find the one the rig belongs to.

    This is the path the operator takes, and getting it wrong on a weapon means
    silently posing nothing: the character's clip names ids no gun rig has.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(AK47, lod=0, with_textures=False)
    loaded = import_mab.load(CASES[-1][2], result["armature"])
    found = os.path.basename(loaded["skeleton"])
    print("no skeleton named: found %s, posed %d bones, %d moved"
          % (found, loaded["bones"], loaded["moved"]))
    if found != os.path.basename(AK47_REF):
        return fail("found %s for a weapon rig" % found)
    return 0 if loaded["bones"] == 8 and loaded["moved"] == 4 else fail(
        "posed %d bones, %d moved" % (loaded["bones"], loaded["moved"]))


def main():
    if not present() or not all(os.path.exists(p) for case in CASES for p in case):
        print("corpus not present, skipping")
        return 0

    errors = sum(check(*case) for case in CASES) + check_skeleton_discovery()
    print("blender anim: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
