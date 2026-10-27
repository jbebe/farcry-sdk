# Build a Blender Action from a pack's animation bank.
#
# A bank is not one animation: it holds a clip per skeleton taking part, so a
# weapon's motion rides behind the character's. Which one fits a given rig is
# re-derived from the tag table and the rig's own bone ids rather than trusted
# from an index - a stale index would silently mispose the model.
#
# Rotations and offsets are local and replace the bone's own rest transform, so
# what the pose bone carries is the rest undone and the clip's applied.

import bpy
from mathutils import Matrix, Quaternion, Vector

from . import convert, import_xbg, rig
from .pack import stem, timing

# Blender counts frames from one, the file from zero.
FIRST_FRAME = 1

PROP_DURATION = "fc2_duration"
PROP_RATE = "fc2_rate"
# The bank an Action was loaded from, by game path.
PROP_BANK = "fc2_bank"

# The bone a character's eye rides on; an aim bank hangs the weapon off it.
EYE_BONE = "Camera"

# A prop a clip attaches is its own sourced collection, and export would then
# find two and refuse to pick. Marked so it can be told apart from the model.
PROP_PROP_OF = "fc2_prop_of"


def _quaternion(xyzw):
    return Quaternion((xyzw[3], xyzw[0], xyzw[1], xyzw[2]))


def _rest_local(bone):
    """A bone's rest node relative to its parent's."""
    return convert.node_local(bone.matrix_local, bone.parent.matrix_local if bone.parent else None)


def bone_ids(clip):
    """Every skeleton bone id a clip addresses, in ascending order."""
    ids = set()
    for key in ("constant_rotations", "constant_translations"):
        ids.update(entry["bone"] for entry in clip.get(key) or ())
    for key in ("keyframe_rotations", "animated_translations"):
        ids.update(entry["bone"] for entry in clip.get(key) or ())
    return sorted(ids)


def clip_for(bank, skeleton):
    """The clip a bank holds for this rig: the one its tag record names after the
    rig's root bone, which carries the model's name, else the first whose ids fit.

    A bank carries one clip per skeleton taking part, character first and then
    the weapon or vehicle it handles, and two small props can both fit one rig.
    """
    count = len(skeleton["bones"])

    def fits(clip):
        ids = bone_ids(clip)
        return not ids or ids[-1] < count

    root = skeleton["bones"][0]["name"].lower() if count else None
    for record in bank.get("participants") or ():
        clip = bank["clips"][record["clip"]]
        if not record.get("reference") and record["name"].lower() == root and fits(clip):
            return clip
    return next(filter(fits, bank["clips"]), None)


def load(pack, bank_path, armature, with_props=False, lod=0, actor=None, still=False):
    """Put one of a pack's banks on `armature` as its active Action.

    A bank holds a clip per skeleton taking part. Given the body the pack carries,
    the character's clip goes on it and the model hangs off the bone the bank's
    own tag record names - which is the scene the bank describes, rather than the
    model alone with a marker where the hands should be. `still` holds every
    clip at its first frame, so a bone left alone writes back still.
    """
    import_xbg.require_current(armature)
    if actor is not None:
        import_xbg.require_current(actor["armature"])
    bank = pack.clip(bank_path)
    if bank is None:
        raise ValueError("this pack carries no %s" % bank_path)
    if still:
        bank = dict(bank, clips=[_first_frame(clip) for clip in bank["clips"]])
    skeleton = pack.rig()
    if skeleton is None:
        raise ValueError("this pack carries no rig, so nothing maps bone ids to names")

    clip = clip_for(bank, skeleton)
    if clip is None:
        raise ValueError("%s holds no clip for a %d-bone rig"
                         % (stem(bank_path), len(skeleton["bones"])))

    result = pose(clip, armature, skeleton, bank_path)
    result["actor"] = _pose_actor(pack, bank, clip, armature, actor, bank_path)
    result["props"] = attach_participants(bank, clip, armature) if with_props else []
    result.update(bank=bank, clip_path=bank_path)
    return result


def _pose_actor(pack, bank, clip, armature, actor, bank_path):
    """Pose the carried body and hang the model off the bone the bank names."""
    if actor is None:
        return None
    skeleton = pack.rig(actor=True)
    theirs = clip_for(bank, skeleton) if skeleton else None
    if theirs is None or theirs is clip:
        return None

    posed = pose(theirs, actor["armature"], skeleton, bank_path, "_actor")
    # A bank names the model once with no reference - that record's clip drives
    # the whole rig - and again per moving piece with one. Only the first says
    # where the model itself hangs.
    records = [p for p in bank.get("participants") or ()
               if bank["clips"][p["clip"]] is clip and p.get("bone")]
    mine = next((p for p in records if not p.get("reference")), None)
    if mine and mine["bone"] in actor["armature"].pose.bones:
        import_xbg.attach(armature, actor["armature"], mine["bone"])
        posed["bone"] = mine["bone"]
    return posed


def pose(clip, armature, skeleton, bank_path, suffix=""):
    """Build an Action from one clip and make it the armature's active one."""
    names = {bone["id"]: bone["name"] for bone in skeleton["bones"]}
    action = bpy.data.actions.new(stem(bank_path) + suffix)
    action[PROP_BANK] = bank_path
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

    # Every rig bone starts keyed at rest, and the clip's keys replace that. Blender
    # auto-keys only channels that already exist, so a bone the clip leaves alone
    # would otherwise hold any pose given it on every frame, and into the next load.
    for bone in skeleton["bones"]:
        pose_bone = armature.pose.bones.get(bone["name"])
        if pose_bone is None:
            continue
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.matrix_basis = Matrix()
        pose_bone.keyframe_insert("rotation_quaternion", frame=FIRST_FRAME)
        if rig.translates(bone):
            pose_bone.keyframe_insert("location", frame=FIRST_FRAME)

    posed = missing = keys = 0
    for bone_id in sorted(set(tracks) | set(offsets)):
        pose_bone = armature.pose.bones.get(names.get(bone_id, ""))
        if pose_bone is None:
            missing += 1
            continue
        posed += 1
        rest_offset, rest_rotation, _scale = _rest_local(pose_bone.bone).decompose()
        undo = rest_rotation.inverted()
        for frame, rotation in tracks.get(bone_id, ()):
            pose_bone.rotation_quaternion = convert.to_bone(undo @ _quaternion(rotation))
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame + FIRST_FRAME)
            keys += 1
        # A pose bone's location is measured in its own rest frame, so the
        # offset the clip replaces the rest one with has to be rotated into it.
        for frame, offset in offsets.get(bone_id, ()):
            pose_bone.location = convert.offset_to_bone(undo @ (Vector(offset) - rest_offset))
            pose_bone.keyframe_insert("location", frame=frame + FIRST_FRAME)
            keys += 1

    return {"clip": clip, "action": action, "bones": posed, "unmatched": missing,
            "keys": keys,
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


def _first_frame(clip):
    """The clip with every keyed track cut to its first key, which is always frame 0."""
    def cut(entries, width):
        return [dict(entry, frames=entry["frames"][:1], values=entry["values"][:width])
                for entry in entries or ()]
    return dict(clip, keyframe_rotations=cut(clip.get("keyframe_rotations"), 4),
                animated_translations=cut(clip.get("animated_translations"), 3))


def _constants(entries):
    return {entry["bone"]: entry["value"] for entry in entries or ()}


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
        import_xbg.attach(marker, armature, bone)
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


def loaded_bank(armature):
    """The bank the armature's Action was loaded from, if one was."""
    action = armature.animation_data.action if armature and armature.animation_data else None
    return action.get(PROP_BANK) if action else None


def model_of(armature):
    """The pack this armature was imported from, if the import recorded one."""
    for collection in armature.users_collection:
        origin = collection.get(import_xbg.PROP_SOURCE)
        if origin:
            return origin
    return None
