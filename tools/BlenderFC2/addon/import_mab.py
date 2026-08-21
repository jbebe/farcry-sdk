# Build a Blender Action from a `.mab`.
#
# A clip names bones by their id in the `.skeleton` it was authored against, so
# that file is what turns ids into names; the armature is then matched by name.
# Rotations and offsets are local and replace the bone's own rest transform, so
# what the pose bone carries is the rest undone and the clip's applied.
#
# A bank holds a clip per skeleton taking part, and its tag records say which
# bone each of the others hangs from, so the props are imported and attached
# from the same file.

import os

import bpy
from mathutils import Matrix, Quaternion, Vector

from fc2fmt.assets import find_named, find_root, install_assets
from fc2fmt.bundle import EXTENSION, SKELETON_SUFFIX, Bundle
from fc2fmt.mab import MabFile
from fc2fmt.skeleton import SkeletonFile

from . import import_xbg, rig
from .import_xbg import PROP_SOURCE

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


def model_of(armature):
    """The .xbg this armature was imported from, if the import recorded one."""
    for collection in armature.users_collection:
        origin = collection.get(PROP_SOURCE)
        if origin:
            return origin
    return None


def clip_for(bank, skeleton):
    """The clip a bank holds for this skeleton: the first whose ids fit it.

    A bank carries one clip per skeleton taking part, character first and then
    the weapon or vehicle it handles, so a weapon rig has to skip past the
    character's clip to reach its own.
    """
    for clip in bank.clips():
        ids = clip.bone_ids()
        if not ids or ids[-1] < len(skeleton.bones):
            return clip
    return None


def load(path, armature, skeleton_path=None, model_path=None, with_props=False,
         lod=0):
    """Put one clip on `armature` as its active Action."""
    bank = MabFile.parse(open(path, "rb").read())
    model_path = model_path or model_of(armature)
    skeleton_path = skeleton_path or find_skeleton(path, model_path)
    if not skeleton_path:
        raise ValueError("no .skeleton found for %s; name one to map bone ids"
                         % os.path.basename(path))
    skeleton = SkeletonFile.parse(open(skeleton_path, "rb").read())
    clip = clip_for(bank, skeleton)
    if clip is None:
        raise ValueError("%s holds no clip for a %d-bone skeleton"
                         % (os.path.basename(path), len(skeleton.bones)))

    name = os.path.splitext(os.path.basename(path))[0]
    result = pose(clip, armature, skeleton, name)
    source = _source_of(model_path) if with_props else None
    result["props"] = load_participants(clip, armature, source, lod) if source else []
    result.update(bank=bank, skeleton=skeleton_path)
    return result


def _source_of(model_path):
    """Where to find the props a clip attaches: the bundle, or the install."""
    if not model_path:
        return None
    if model_path.lower().endswith(EXTENSION):
        return Bundle.load(model_path)
    root = find_root(model_path)
    return install_assets(root) if root else None


def pose(clip, armature, skeleton, name):
    """Build an Action from one clip and make it the armature's active one."""
    names = {bone.id: bone.name for bone in skeleton.bones}
    # The .xbg tree and the constraint bones both need reconciling first, or
    # the knees, elbows and arm twists lag behind everything around them.
    adjusted = rig.apply(armature, skeleton)

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
    offsets = dict(clip.translation_tracks())
    for bone_id, offset in clip.constant_translations().items():
        offsets.setdefault(bone_id, [(0, offset)])

    posed = missing = keys = 0
    for bone_id in sorted(set(tracks) | set(offsets)):
        pose_bone = armature.pose.bones.get(names.get(bone_id, ""))
        if pose_bone is None:
            missing += 1
            continue
        posed += 1
        rest_offset, rest_rotation, _scale = _rest_local(pose_bone.bone).decompose()
        undo = rest_rotation.inverted()
        pose_bone.rotation_mode = "QUATERNION"
        for frame, rotation in tracks.get(bone_id, ()):
            if rotation is None:
                continue
            pose_bone.rotation_quaternion = undo @ _quaternion(rotation)
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame + FIRST_FRAME)
            keys += 1
        # A pose bone's location is measured in its own rest frame, so the
        # offset the clip replaces the rest one with has to be rotated into it.
        for frame, offset in offsets.get(bone_id, ()):
            pose_bone.location = undo @ (Vector(offset) - rest_offset)
            pose_bone.keyframe_insert("location", frame=frame + FIRST_FRAME)
            keys += 1

    return {"clip": clip, "action": action, "bones": posed, "unmatched": missing,
            "keys": keys, "rig": adjusted,
            "moved": sum(1 for b in offsets if names.get(b, "") in armature.pose.bones)}


def attach(child, armature, bone_name):
    """Hang an object off a bone at the bone's head, where a clip attaches.

    Blender parents to the tail, so the inverse cancels the bone's length.
    """
    child.parent = armature
    child.parent_type = "BONE"
    child.parent_bone = bone_name
    child.matrix_parent_inverse = Matrix.Translation(
        (0.0, -armature.data.bones[bone_name].length, 0.0))


def load_participants(clip, armature, source, lod=0):
    """Import what a clip attaches to this rig, posed and parented.

    A participant's clip is expressed in the frame of the bone its tag record
    names, so its rig is hung off that bone and its own root carries the rest.
    One that only references a prop already in the scene gets an empty carrying
    its track instead of a second copy of the geometry.
    """
    loaded = []
    for participant, sub in clip.participant_clips():
        if participant.parent not in armature.pose.bones:
            continue
        model = (_participant_model(source, participant, sub)
                 if participant.is_primary else None)
        entry = {"participant": participant, "clip": sub, "model": model}
        if model is None:
            entry["object"] = _empty(participant.name, armature)
            _key_root(sub, entry["object"])
        else:
            result = import_xbg.build(source.read(model), participant.name,
                                      source, lod, True, model)
            entry["object"] = result["armature"]
            entry["collection"] = result["collection"]
            skeleton = source.read(_beside(model, SKELETON_SUFFIX))
            if skeleton:
                pose(sub, result["armature"], SkeletonFile.parse(skeleton),
                     participant.name)
            else:
                # No rig to name the bones by, so drive the whole thing instead.
                _key_root(sub, result["armature"])
        attach(entry["object"], armature, participant.parent)
        loaded.append(entry)
    return loaded


def _empty(name, armature):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "ARROWS"
    armature.users_collection[0].objects.link(empty)
    return empty


def _key_root(clip, obj, root=0):
    """Key a participant's root track straight onto an object."""
    obj.rotation_mode = "QUATERNION"
    rotations = clip.keyframe_tracks().get(root) or [
        (0, clip.constant_rotations().get(root))]
    offsets = clip.translation_tracks().get(root) or [
        (0, clip.constant_translations().get(root))]
    for frame, rotation in rotations:
        if rotation is not None:
            obj.rotation_quaternion = _quaternion(rotation)
            obj.keyframe_insert("rotation_quaternion", frame=frame + FIRST_FRAME)
    for frame, offset in offsets:
        if offset is not None:
            obj.location = Vector(offset)
            obj.keyframe_insert("location", frame=frame + FIRST_FRAME)


def _beside(model, suffix):
    return os.path.splitext(model)[0] + suffix


def _participant_model(source, participant, clip):
    """The model a participant names, or None when the source has no such file.

    Names are not unique across the retail tree — `mortar` is both a weapon and
    a kitchen prop — so a candidate whose rig fits the participant's clip wins.
    """
    candidates = find_named(source, participant.name)
    for model in candidates:
        data = source.read(_beside(model, SKELETON_SUFFIX))
        if data is None:
            continue
        ids = clip.bone_ids()
        if not ids or ids[-1] < len(SkeletonFile.parse(data).bones):
            return model
    return candidates[0] if candidates else None


def apply_to_scene(scene, clip):
    """Point the scene's frame range and rate at the clip that was just loaded."""
    header = clip.keyframe_header()
    if header and header[2]:
        scene.render.fps = header[2]
    scene.frame_start = FIRST_FRAME
    scene.frame_end = FIRST_FRAME + (header[1] if header else 0)
