# Build a Blender Action from a `.mab`.
#
# A clip names bones by their id in the `.skeleton` it was authored against, so
# that file is what turns ids into names; the armature is then matched by name.
# Rotations are local and replace the bone's own rest rotation, so what the pose
# bone carries is the rest rotation undone and the clip's applied.

import os

import bpy
from mathutils import Quaternion

from fc2fmt.mab import MabFile
from fc2fmt.skeleton import SkeletonFile

from . import rig

# Blender counts frames from one, the file from zero.
FIRST_FRAME = 1

PROP_DURATION = "fc2_duration"
PROP_RATE = "fc2_rate"


def find_skeleton(clip_path, model_path=None):
    """The .skeleton a clip is authored against, if it sits where they usually do.

    Character clips live under characters/_common/animations, and the skeleton
    they share is characters/_common/pelvis_ref.skeleton.
    """
    if model_path:
        beside = os.path.splitext(model_path)[0] + "_ref.skeleton"
        if os.path.exists(beside):
            return beside
    directory = os.path.dirname(os.path.abspath(clip_path))
    while True:
        candidate = os.path.join(directory, "pelvis_ref.skeleton")
        if os.path.exists(candidate):
            return candidate
        parent = os.path.dirname(directory)
        if parent == directory:
            return None
        directory = parent


def _quaternion(xyzw):
    return Quaternion((xyzw[3], xyzw[0], xyzw[1], xyzw[2]))


def _rest_local(bone):
    """A bone's rest transform relative to its parent, as Blender holds it."""
    if bone.parent:
        return bone.parent.matrix_local.inverted() @ bone.matrix_local
    return bone.matrix_local.copy()


def load(path, armature, skeleton_path=None, model_path=None):
    """Put one clip on `armature` as its active Action."""
    clip = MabFile.parse(open(path, "rb").read())
    skeleton_path = skeleton_path or find_skeleton(path, model_path)
    if not skeleton_path:
        raise ValueError("no .skeleton found for %s; name one to map bone ids"
                         % os.path.basename(path))
    skeleton = SkeletonFile.parse(open(skeleton_path, "rb").read())
    names = {bone.id: bone.name for bone in skeleton.bones}
    # The .xbg tree and the constraint bones both need reconciling first, or
    # the knees, elbows and arm twists lag behind everything around them.
    adjusted = rig.apply(armature, skeleton)

    name = os.path.splitext(os.path.basename(path))[0]
    action = bpy.data.actions.new(name)
    action[PROP_DURATION] = clip.duration
    header = clip.keyframe_header()
    if header:
        action[PROP_RATE] = header[2]
    if armature.animation_data is None:
        armature.animation_data_create()
    armature.animation_data.action = action

    tracks = dict(clip.keyframe_tracks())
    for bone_id, quat in clip.constant_rotations().items():
        tracks.setdefault(bone_id, [(0, quat)])

    posed = missing = keys = 0
    for bone_id, frames in sorted(tracks.items()):
        pose_bone = armature.pose.bones.get(names.get(bone_id, ""))
        if pose_bone is None:
            missing += 1
            continue
        posed += 1
        rest = _rest_local(pose_bone.bone).to_quaternion().inverted()
        pose_bone.rotation_mode = "QUATERNION"
        for frame, rotation in frames:
            if rotation is None:
                continue
            pose_bone.rotation_quaternion = rest @ _quaternion(rotation)
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame + FIRST_FRAME)
            keys += 1

    return {"clip": clip, "action": action, "bones": posed, "unmatched": missing,
            "keys": keys, "skeleton": skeleton_path, "rig": adjusted}


def apply_to_scene(scene, clip):
    """Point the scene's frame range and rate at the clip that was just loaded."""
    header = clip.keyframe_header()
    if header and header[2]:
        scene.render.fps = header[2]
    scene.frame_start = FIRST_FRAME
    scene.frame_end = FIRST_FRAME + (header[1] if header else 0)
