# Build a Blender Action from a pack's animation bank.
#
# A bank is not one animation: it holds a clip per skeleton taking part, so a
# weapon's motion rides behind the character's. Which one fits a given rig is
# re-derived from the rig's own bone ids rather than trusted from an index -
# a stale index would silently mispose the model.
#
# Rotations and offsets are local and replace the bone's own rest transform, so
# what the pose bone carries is the rest undone and the clip's applied.

import bpy
from mathutils import Matrix, Quaternion, Vector

from . import import_xbg, rig
from .pack import stem

# Blender counts frames from one, the file from zero.
FIRST_FRAME = 1

PROP_DURATION = "fc2_duration"
PROP_RATE = "fc2_rate"

# A prop a clip attaches is its own sourced collection, and export would then
# find two and refuse to pick. Marked so it can be told apart from the model.
PROP_PROP_OF = "fc2_prop_of"


def _quaternion(xyzw):
    return Quaternion((xyzw[3], xyzw[0], xyzw[1], xyzw[2]))


def _rest_local(bone):
    """A bone's rest transform relative to its parent, as Blender holds it."""
    if bone.parent:
        return bone.parent.matrix_local.inverted() @ bone.matrix_local
    return bone.matrix_local.copy()


def bone_ids(clip):
    """Every skeleton bone id a clip addresses, in ascending order."""
    ids = set()
    for key in ("constant_rotations", "constant_translations"):
        ids.update(entry["bone"] for entry in clip.get(key) or ())
    for key in ("keyframe_rotations", "animated_translations"):
        ids.update(entry["bone"] for entry in clip.get(key) or ())
    return sorted(ids)


def clip_for(bank, skeleton):
    """The clip a bank holds for this rig: the first whose ids fit it.

    A bank carries one clip per skeleton taking part, character first and then
    the weapon or vehicle it handles, so a weapon rig has to skip past the
    character's clip to reach its own.
    """
    count = len(skeleton["bones"])
    for clip in bank["clips"]:
        ids = bone_ids(clip)
        if not ids or ids[-1] < count:
            return clip
    return None


def timing(clip):
    """The clip's own last frame and rate, from whichever section carries them."""
    for key in ("keyframe_timing", "translation_timing",
                "root_translation_timing", "root_rotation_timing"):
        found = clip.get(key)
        if found:
            return found["last_frame"], found["rate"]
    return 0, 0


def load(pack, bank_path, armature, with_props=False, lod=0, actor=None):
    """Put one of a pack's banks on `armature` as its active Action.

    A bank holds a clip per skeleton taking part. Given the body the pack carries,
    the character's clip goes on it and the model hangs off the bone the bank's
    own tag record names - which is the scene the bank describes, rather than the
    model alone with a marker where the hands should be.
    """
    bank = pack.clip(bank_path)
    if bank is None:
        raise ValueError("this pack carries no %s" % bank_path)
    skeleton = pack.rig()
    if skeleton is None:
        raise ValueError("this pack carries no rig, so nothing maps bone ids to names")

    clip = clip_for(bank, skeleton)
    if clip is None:
        raise ValueError("%s holds no clip for a %d-bone rig"
                         % (stem(bank_path), len(skeleton["bones"])))

    result = pose(clip, armature, skeleton, stem(bank_path))
    result["actor"] = _pose_actor(pack, bank, clip, armature, actor, stem(bank_path))
    result["props"] = attach_participants(bank, clip, armature) if with_props else []
    result.update(bank=bank, clip_path=bank_path)
    return result


def _pose_actor(pack, bank, clip, armature, actor, name):
    """Pose the carried body and hang the model off the bone the bank names."""
    if actor is None:
        return None
    skeleton = pack.rig(actor=True)
    theirs = clip_for(bank, skeleton) if skeleton else None
    if theirs is None or theirs is clip:
        return None

    posed = pose(theirs, actor["armature"], skeleton, name + "_actor")
    # A bank names the model once with no reference - that record's clip drives
    # the whole rig - and again per moving piece with one. Only the first says
    # where the model itself hangs.
    records = [p for p in bank.get("participants") or ()
               if bank["clips"][p["clip"]] is clip and p.get("bone")]
    mine = next((p for p in records if not p.get("reference")), None)
    if mine and mine["bone"] in actor["armature"].pose.bones:
        attach(armature, actor["armature"], mine["bone"])
        posed["bone"] = mine["bone"]
    return posed


def pose(clip, armature, skeleton, name):
    """Build an Action from one clip and make it the armature's active one."""
    names = {bone["id"]: bone["name"] for bone in skeleton["bones"]}
    # The mesh's node tree and the rig's constraint bones both need reconciling
    # first, or the knees, elbows and arm twists lag behind everything around
    # them.
    adjusted = rig.apply(armature, skeleton)

    action = bpy.data.actions.new(name)
    action[PROP_DURATION] = clip.get("duration", 0.0)
    action[PROP_RATE] = timing(clip)[1]
    if armature.animation_data is None:
        armature.animation_data_create()
    armature.animation_data.action = action

    tracks = _tracks(clip.get("keyframe_rotations"), 4)
    for bone_id, value in _constants(clip.get("constant_rotations")).items():
        tracks.setdefault(bone_id, [(0, value)])
    offsets = _tracks(clip.get("animated_translations"), 3)
    for bone_id, value in _constants(clip.get("constant_translations")).items():
        offsets.setdefault(bone_id, [(0, value)])

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


def _tracks(entries, width):
    """Each bone's keys as (frame, value) pairs, from the pack's flat arrays."""
    tracks = {}
    for entry in entries or ():
        values = entry["values"]
        tracks[entry["bone"]] = [
            (frame, values[index * width:(index + 1) * width])
            for index, frame in enumerate(entry["frames"])]
    return tracks


def _constants(entries):
    return {entry["bone"]: entry["value"] for entry in entries or ()}


def attach(child, armature, bone_name):
    """Hang an object off a bone at the bone's head, where a clip attaches.

    Blender parents to the tail, so the inverse cancels the bone's length.
    """
    child.parent = armature
    child.parent_type = "BONE"
    child.parent_bone = bone_name
    child.matrix_parent_inverse = Matrix.Translation(
        (0.0, -armature.data.bones[bone_name].length, 0.0))


def attach_participants(bank, posed, armature):
    """Hang an empty on each bone the bank attaches something to.

    The bank says what it moves besides its own skeleton and which bone each
    hangs from. Only the models the pack itself carries could be built here, and
    a pack holds one - so what a participant gets is a marker carrying its own
    track, which is what makes the attachment visible without inventing a mesh.
    """
    loaded = []
    for participant in bank.get("participants") or ():
        bone = participant.get("bone")
        if not bone or bone not in armature.pose.bones:
            continue
        if bank["clips"][participant["clip"]] is posed:
            # The rig already carries this one's motion as its own Action.
            continue
        marker = _empty(participant["name"], armature)
        _key_root(bank["clips"][participant["clip"]], marker)
        attach(marker, armature, bone)
        loaded.append({"participant": participant, "object": marker})
    return loaded


def _empty(name, armature):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "ARROWS"
    empty[PROP_PROP_OF] = armature.name
    armature.users_collection[0].objects.link(empty)
    return empty


def _key_root(clip, obj, root=0):
    """Key a participant's root track straight onto an object."""
    obj.rotation_mode = "QUATERNION"
    rotations = _tracks(clip.get("keyframe_rotations"), 4).get(root)
    if rotations is None:
        constant = _constants(clip.get("constant_rotations")).get(root)
        rotations = [(0, constant)] if constant else []
    offsets = _tracks(clip.get("animated_translations"), 3).get(root)
    if offsets is None:
        constant = _constants(clip.get("constant_translations")).get(root)
        offsets = [(0, constant)] if constant else []

    for frame, rotation in rotations:
        obj.rotation_quaternion = _quaternion(rotation)
        obj.keyframe_insert("rotation_quaternion", frame=frame + FIRST_FRAME)
    for frame, offset in offsets:
        obj.location = Vector(offset)
        obj.keyframe_insert("location", frame=frame + FIRST_FRAME)


def apply_to_scene(scene, clip):
    """Point the scene's frame range and rate at the clip that was just loaded."""
    last, rate = timing(clip)
    if rate:
        scene.render.fps = rate
    scene.frame_start = FIRST_FRAME
    scene.frame_end = FIRST_FRAME + last


def model_of(armature):
    """The pack this armature was imported from, if the import recorded one."""
    for collection in armature.users_collection:
        origin = collection.get(import_xbg.PROP_SOURCE)
        if origin:
            return origin
    return None
