# Write a Blender Action back into one of the pack's animation banks.
#
# A bank holds one clip per skeleton taking part, so this only ever rewrites the
# one that fits this model's rig - every other clip in the chain goes back byte
# for byte. That is not a promise this file keeps by being careful; it is what
# the pack's own format does, because an untouched clip carries its sections
# verbatim and only a cleared one is re-encoded.
#
# The conversion is the inverse of `import_mab.pose`: a clip stores a bone's
# transform relative to its parent and *replaces* the rest transform rather than
# adding to it, so what goes back is the rest re-applied to the posed bone.
#
# Two layout rules the encoder cannot work around, and this has to respect:
#
#   - Rotations are stored in groups of eight frames and every group's first
#     frame carries a key, so frames 0, 8, 16 ... are always written.
#   - Translations are dense and frame-major, so every frame carries a value.

import bpy

from .import_mab import FIRST_FRAME, _rest_local, clip_for, timing
from .import_xbg import PROP_ACTOR_OF

# Which sections a rewrite touches. The tags, the events and the trajectory are
# left exactly as they were: nothing here edits what a bank attaches or where it
# travels, and the tag block is mostly undecoded anyway.
SECTION_CONSTANT_ROTATION = 2
SECTION_KEYFRAME_ROTATION = 3
SECTION_CONSTANT_TRANSLATION = 4
SECTION_ANIMATED_TRANSLATION = 5

# Frames per rotation group, from the format. A group's first frame must be keyed.
GROUP = 8

# Below this a bone is treated as holding still for the whole clip, and goes in
# the constant section instead of being keyed. Roughly a twentieth of a degree,
# and about a hundredth of a millimetre.
STILL_ROTATION = 1e-5
STILL_TRANSLATION = 1e-5


def write(pack, bank_path, armature, lossless=False, tolerance=0.002, bones=None):
    """Rewrite this model's clip in one bank from the armature's Action.

    `bones` names the bones to rewrite; every other one keeps the entry the clip
    already held, at the frames the clip itself keyed. Leave it out to rewrite
    the whole clip, which re-chooses every bone's key frames and adds an entry
    for every bone on the rig.

    Returns what changed, so a caller can say it rather than guess.
    """
    bank = pack.clip(bank_path)
    if bank is None:
        raise ValueError("this pack carries no %s" % bank_path)
    # Which rig decides which clip, so writing the carried body's Action writes
    # the character's clip rather than the model's.
    skeleton = pack.rig(actor=bool(armature.get(PROP_ACTOR_OF)))
    if skeleton is None:
        raise ValueError("this pack carries no rig, so nothing maps bone names to ids")
    if armature.animation_data is None or armature.animation_data.action is None:
        raise ValueError("%s carries no Action to write" % armature.name)

    clip = clip_for(bank, skeleton)
    if clip is None:
        raise ValueError("no clip in %s fits a %d-bone rig"
                         % (bank_path, len(skeleton["bones"])))

    last, rate = timing(clip)
    sampled = _sample(armature, skeleton, last, bones)
    written = _fill(clip, skeleton, sampled, last, rate, lossless, tolerance)

    pack.replace_clip(bank_path, bank)
    return dict(written, clip=bank["clips"].index(clip), clips=len(bank["clips"]),
                frames=last + 1, rate=rate)


def _sample(armature, skeleton, last, wanted=None):
    """Each named rig bone's clip-space transform at every frame.

    Sampled off the evaluated pose rather than read out of the F-curves, so a
    constraint, an NLA strip or a driver all land in the file the way they look
    in the viewport - which is the whole point of the plugin.
    """
    scene = bpy.context.scene
    before = scene.frame_current
    # A bone's rest transform does not depend on the frame, so it is decomposed
    # once rather than inside a sweep that is bones times frames.
    bones = [(bone["id"], armature.pose.bones.get(bone["name"]))
             for bone in skeleton["bones"] if wanted is None or bone["name"] in wanted]
    bones = [(bone_id, bone) + _rest_local(bone.bone).decompose()[:2]
             for bone_id, bone in bones if bone is not None]

    rotations = {bone_id: [] for bone_id, _bone, _offset, _rotation in bones}
    offsets = {bone_id: [] for bone_id, _bone, _offset, _rotation in bones}
    try:
        for frame in range(last + 1):
            scene.frame_set(frame + FIRST_FRAME)
            for bone_id, bone, rest_offset, rest_rotation in bones:
                # import_mab poses with `rest^-1 . clip`; this is that undone.
                rotations[bone_id].append(rest_rotation @ bone.rotation_quaternion)
                offsets[bone_id].append(rest_offset + (rest_rotation @ bone.location))
    finally:
        scene.frame_set(before)
    return rotations, offsets


def _fill(clip, skeleton, sampled, last, rate, lossless, tolerance):
    """Replace the clip's four motion sections with what was sampled."""
    rotations, offsets = sampled
    moves = {bone["id"] for bone in skeleton["bones"]
             if bone.get("animated_translation")}

    def rotation(bone_id, keys):
        if _still_rotation(keys):
            return {"bone": bone_id, "value": _xyzw(keys[0])}, True
        frames = _frames(keys, last, lossless, tolerance, _rotation_error)
        return {"bone": bone_id, "frames": frames,
                "values": [c for frame in frames for c in _xyzw(keys[frame])]}, False

    def translation(bone_id, keys):
        if bone_id not in moves:
            # The rig holds this bone at a fixed offset, and every shipped
            # translation lands on a bone marked otherwise. Writing one here
            # would be inventing motion the engine will not play.
            return None, False
        if _still_translation(keys):
            return {"bone": bone_id, "value": [keys[0].x, keys[0].y, keys[0].z]}, True
        # Dense and frame-major: every frame carries a value, so there is no
        # keying decision to make here.
        return {"bone": bone_id, "frames": list(range(last + 1)),
                "values": [c for key in keys for c in (key.x, key.y, key.z)]}, False

    constant_rotations, keyed_rotations = _section(
        clip, "constant_rotations", "keyframe_rotations", rotations, rotation)
    constant_translations, animated_translations = _section(
        clip, "constant_translations", "animated_translations", offsets, translation)

    clip["constant_rotations"] = constant_rotations
    clip["keyframe_rotations"] = keyed_rotations
    clip["constant_translations"] = constant_translations
    clip["animated_translations"] = animated_translations
    _timing(clip, "keyframe_timing", last, rate, bool(keyed_rotations))
    _timing(clip, "translation_timing", last, rate, bool(animated_translations))
    _declare(clip, SECTION_CONSTANT_ROTATION, bool(constant_rotations))
    _declare(clip, SECTION_KEYFRAME_ROTATION, bool(keyed_rotations))
    _declare(clip, SECTION_CONSTANT_TRANSLATION, bool(constant_translations))
    _declare(clip, SECTION_ANIMATED_TRANSLATION, bool(animated_translations))

    # Clearing these is how the format is told the clip changed: a section still
    # in `raw` goes back verbatim, and the masks are re-derived from which bones
    # now carry what.
    for slot in (SECTION_CONSTANT_ROTATION, SECTION_KEYFRAME_ROTATION,
                 SECTION_CONSTANT_TRANSLATION, SECTION_ANIMATED_TRANSLATION):
        clip.get("raw", {}).pop(str(slot), None)
        clip.get("raw", {}).pop(slot, None)
    clip["masks"] = []

    return {"constant_rotations": len(constant_rotations),
            "keyed_rotations": len(keyed_rotations),
            "constant_translations": len(constant_translations),
            "animated_translations": len(animated_translations),
            "keys": sum(len(track["frames"]) for track in keyed_rotations)}


def _section(clip, constant, keyed, sampled, encode):
    """One section pair: encoded where the bone was sampled, held where it was not.

    Bone order has to stay ascending. The mask JackAll derives is a bitset and a
    bone's slot inside a section is the popcount below it, so a carried entry
    appended out of order would pair the mask with the wrong values.
    """
    held = {section: {entry["bone"]: entry for entry in clip.get(section) or ()}
            for section in (constant, keyed)}
    out = {constant: [], keyed: []}
    for bone_id in sorted(set(sampled) | set(held[constant]) | set(held[keyed])):
        if bone_id not in sampled:
            section = constant if bone_id in held[constant] else keyed
            out[section].append(held[section][bone_id])
            continue
        entry, still = encode(bone_id, sampled[bone_id])
        if entry is not None:
            out[constant if still else keyed].append(entry)
    return out[constant], out[keyed]


def _declare(clip, slot, present):
    """Say whether the clip carries a section at all, which is not derivable."""
    sections = [value for value in clip.get("sections", []) if value != slot]
    if present:
        sections.append(slot)
    clip["sections"] = sorted(sections)


def _timing(clip, key, last, rate, present):
    clip[key] = {"last_frame": last, "rate": rate} if present else None


def _frames(keys, last, lossless, tolerance, error):
    """Which frames to key.

    Every group's first frame always, because the format's rotation groups start
    with one. Between those, a frame is kept only when dropping it would move
    the bone further than the tolerance from what interpolating its neighbours
    would give - which is the same question a viewer asks when it plays back.
    """
    required = sorted({frame for frame in range(0, last + 1, GROUP)} | {last})
    if lossless:
        return list(range(last + 1))

    kept = list(required)
    for frame in range(last + 1):
        if frame in required:
            continue
        low = max(f for f in kept if f < frame)
        high = min((f for f in kept if f > frame), default=None)
        if high is None:
            kept.append(frame)
            kept.sort()
            continue
        span = (frame - low) / float(high - low)
        if error(keys[frame], keys[low], keys[high], span) > tolerance:
            kept.append(frame)
            kept.sort()
    return kept


def _rotation_error(actual, low, high, span):
    """How far the interpolated rotation sits from the real one, as an angle."""
    guess = low.slerp(high, span)
    return 1.0 - abs(guess.dot(actual))


def _still_rotation(keys):
    first = keys[0]
    return all(1.0 - abs(first.dot(key)) <= STILL_ROTATION for key in keys[1:])


def _still_translation(keys):
    first = keys[0]
    return all((key - first).length <= STILL_TRANSLATION for key in keys[1:])


def _xyzw(quaternion):
    """Blender orders a quaternion w first; the file orders it last."""
    return [quaternion.x, quaternion.y, quaternion.z, quaternion.w]
