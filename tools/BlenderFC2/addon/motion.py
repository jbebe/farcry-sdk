# How far each bone travels across every clip a pack carries.
#
# This is the most useful thing the add-on can tell a weapon modeler, and it is
# not guessable from the mesh. On the sawed-off shotgun `FRAME` moves 0 degrees
# over 0 metres, so the body of the gun belongs there; `CLIP` swings 177 degrees
# over 1.01 m because it is the break-action hinge. Put geometry on the wrong
# bone and the gun tears itself apart on the first reload - which is otherwise a
# playtest discovery.
#
# Rotations are compared against the clip's own reference rotation rather than
# against the rest pose: a clip replaces a bone's rest transform, so the useful
# number is how far the bone swings *within the clip*, not where the clip puts
# it relative to the model.
#
# The same banks also say where the player's eye sits, which is what frames a
# scope's sight picture.
#
# No bpy here, so it can be measured without a scene.

import math

from . import import_mab

# The character bone an aim bank hangs the weapon off, the weapon's own root
# within the clip that bank drives it with, and the zoomed bank of the two.
AIM_BONE = "Camera"
ROOT_BONE = 0
ZOOMED_BANK = "aimironcycle"


def table(pack, rig=None):
    """Every bone the pack's clips move, worst first.

    Only the clip that fits this pack's own rig is measured in each bank - a
    bank also holds the character's clip, whose bones are a different skeleton
    entirely and would fill the table with names the model does not have.
    """
    skeleton = rig or pack.rig()
    if skeleton is None:
        return []

    names = {bone["id"]: bone["name"] for bone in skeleton["bones"]}
    worst = {}
    for index in pack.clips:
        bank = pack.clip(index["path"])
        if bank is None:
            continue
        clip = import_mab.clip_for(bank, skeleton)
        if clip is None:
            continue
        _measure(clip, names, index["label"], worst)

    return sorted(worst.values(),
                  key=lambda row: (-row["rotation"], -row["translation"], row["bone"]))


def _measure(clip, names, label, worst):
    for bone, keys in _rotations(clip).items():
        _record(worst, names, bone, label, rotation=_swing(keys))
    for bone, keys in _translations(clip).items():
        _record(worst, names, bone, label, translation=_span(keys))


def _record(worst, names, bone, label, rotation=0.0, translation=0.0):
    name = names.get(bone)
    if name is None:
        return
    row = worst.setdefault(
        name, {"bone": name, "rotation": 0.0, "translation": 0.0, "clip": label})
    # The named clip is the one that produced the biggest number of the two, so
    # a modeler opening it sees the motion the row is about.
    if rotation > row["rotation"] or translation > row["translation"]:
        row["clip"] = label
    row["rotation"] = max(row["rotation"], rotation)
    row["translation"] = max(row["translation"], translation)


def _rotations(clip):
    keys = {entry["bone"]: [entry["values"][i * 4:(i + 1) * 4]
                            for i in range(len(entry["frames"]))]
            for entry in clip.get("keyframe_rotations") or ()}
    for entry in clip.get("constant_rotations") or ():
        keys.setdefault(entry["bone"], [entry["value"]])
    return keys


def _translations(clip):
    keys = {entry["bone"]: [entry["values"][i * 3:(i + 1) * 3]
                            for i in range(len(entry["frames"]))]
            for entry in clip.get("animated_translations") or ()}
    for entry in clip.get("constant_translations") or ():
        keys.setdefault(entry["bone"], [entry["value"]])
    return keys


def _swing(keys):
    """The widest angle between any two of a bone's rotations, in degrees.

    Against the first key rather than every pair: a bone's motion is a path, so
    the extremes are what it reaches from where it started, and comparing all
    pairs is quadratic for no more information.
    """
    if len(keys) < 2:
        return 0.0
    first = keys[0]
    return max(_angle(first, key) for key in keys[1:])


def _angle(a, b):
    # A quaternion and its negation are the same rotation, so the sign of the
    # dot product carries no information and the absolute value is the answer.
    dot = min(1.0, abs(sum(x * y for x, y in zip(a, b))))
    return math.degrees(2.0 * math.acos(dot))


def _span(keys):
    """The furthest a bone's offset gets from any other, in metres."""
    if len(keys) < 2:
        return 0.0
    low = [min(key[axis] for key in keys) for axis in range(3)]
    high = [max(key[axis] for key in keys) for axis in range(3)]
    return math.sqrt(sum((high[axis] - low[axis]) ** 2 for axis in range(3)))


def aim_pose(pack):
    """Where a bank holds the weapon relative to the player's eye.

    An aim bank hangs the model off the character's `Camera` bone, so the
    weapon's own root track inside it is the weapon measured from the eye.
    """
    # The zoomed stance first, then the shoulder-ready one a few centimetres
    # further back, so a bank is only parsed until one of them answers.
    for index in sorted(pack.clips, key=lambda entry: ZOOMED_BANK not in entry["label"]):
        bank = pack.clip(index["path"])
        clip = _aimed(bank) if bank else None
        if clip is None:
            continue
        return {"bank": index["label"],
                "rotation": (_rotations(clip).get(ROOT_BONE) or [None])[0],
                "translation": (_translations(clip).get(ROOT_BONE) or [None])[0]}
    return None


def _aimed(bank):
    """The clip a bank drives its model with while hanging it off the eye."""
    for participant in bank.get("participants") or ():
        if participant.get("bone") == AIM_BONE:
            return bank["clips"][participant["clip"]]
    return None
