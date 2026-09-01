# Write an animation back out of Blender and check what landed in the bank.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_write.py
#
# Two things have to hold, and the first is the one that would ruin somebody's
# game without ever crashing.
#
# A bank holds the character's clip as well as the weapon's. Rewriting the
# weapon's must leave every other clip in the chain **byte for byte** what it
# was - otherwise re-saving a model quietly re-encodes the arms holding it, and
# the damage compounds every time the file is opened.
#
# The second is that the motion survives: pose the rig, write, apply, load the
# result back, and require the bones to land where Blender had them.

import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of the add-on an installed extension left.
from _corpus import CLI, find, pack, require_pack

import bpy
from mathutils import Quaternion, Vector

from addon import export_mab, import_mab, import_xbg
from addon.pack import Pack
from addon.import_mab import FIRST_FRAME, clip_for, timing

AK47 = "graphics/weapons/primary/ak47/ak47.xbg"
RELOAD = ("graphics/characters/_common/animations/weapons/primary/ak47/"
          "1stge_uppb_reload_+000fw_prak4_i1.mab")

TOLERANCE = 1e-3


def fail(message):
    print("FAIL %s" % message)
    return 1


def load(model=AK47, rig=None):
    path = pack(model, clips=[RELOAD], rig=rig)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    result["loaded"] = import_mab.load(result["pack"], RELOAD, result["armature"])
    return result


def applied(pack_object, directory, name):
    """Write the pack, hand it to JackAll, and read the .mab it produced."""
    out = os.path.join(directory, name + ".fc2model")
    pack_object.save(out)
    layer = os.path.join(directory, name)
    result = subprocess.run([CLI, "fc2model", "extract", out, "-o", layer, "--all"],
                            capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("jackall could not apply the pack:\n%s\n%s"
                           % (result.stdout, result.stderr))
    written = [f for f in find(".mab", layer)]
    if len(written) != 1:
        raise RuntimeError("expected one bank, got %d" % len(written))
    return open(written[0], "rb").read()


def source_bank():
    name = os.path.basename(RELOAD).lower()
    for found in find(".mab"):
        if os.path.basename(found).lower() == name:
            return open(found, "rb").read()
    return None


def other_clips_survive(directory):
    """Every clip but the model's own has to come back byte for byte.

    Checked on the bytes of the file, by splitting the chain where the rewritten
    clip starts: everything before it belongs to clips a modeler never asked to
    touch, and the character's arms are in there.

    One thing in that prefix is allowed to move. A tag record carries the clip
    it names as a delta from the record's own position, so every one of them
    shifts when any clip changes size - that is bookkeeping, and getting it
    wrong misbehaves the animation without crashing. Those four bytes per record
    are masked out here and checked by JackAll's own gate instead.
    """
    result = load()
    armature = result["armature"]

    # Move one bone by a lot, so the edited clip is unmistakably different.
    bpy.context.scene.frame_set(FIRST_FRAME + 10)
    bone = armature.pose.bones["CLIP"]
    bone.rotation_mode = "QUATERNION"
    bone.rotation_quaternion = Quaternion((0.0, 0.0, 1.0), 1.2)
    bone.keyframe_insert("rotation_quaternion", frame=FIRST_FRAME + 10)

    written = export_mab.write(result["pack"], RELOAD, armature)
    produced = applied(result["pack"], directory, "edited")
    original = source_bank()
    if original is None:
        print("the bank is not in the corpus, skipped")
        return 0

    errors = 0
    print("wrote clip %d of %d: %d keyed rotations, %d constant, %d translations, %d keys"
          % (written["clip"], written["clips"], written["keyed_rotations"],
             written["constant_rotations"], written["animated_translations"],
             written["keys"]))
    if written["clip"] == 0:
        errors += fail("the weapon's clip is the first in the chain; that is the character's")
    if produced == original:
        errors += fail("the edit did not reach the file")

    prefix = _clip_start(original, written["clip"])
    if prefix <= 0:
        return fail("could not find where the rewritten clip starts")

    skip = _tag_deltas(original, prefix) | _tag_deltas(produced, prefix)
    differing = [index for index in range(min(prefix, len(produced)))
                 if produced[index] != original[index] and index not in skip]
    if differing:
        errors += fail("%d bytes of untouched clips changed, first at 0x%X"
                       % (len(differing), differing[0]))
    else:
        print("  %d bytes of untouched clips are byte-identical, %d tag-delta bytes aside"
              % (prefix - len(skip), len(skip)))
    return errors


def _tag_deltas(data, limit):
    """Byte positions of every tag record's clip delta, in the clips before `limit`.

    A record is 0xAC bytes and holds its clip's delta at +0x0C; the table starts
    with a count.
    """
    import struct

    positions = set()
    for start in _parse(data):
        if start >= limit:
            break
        offsets = struct.unpack_from("<9i", data, start + 0x78)
        tags = offsets[6]
        if tags <= 0:
            continue
        at = start + tags
        count = struct.unpack_from("<i", data, at)[0]
        for record in range(count):
            delta = at + 4 + (record * 0xAC) + 0x0C
            positions.update(range(delta, delta + 4))
    return positions


def _clip_start(data, index):
    """Where clip `index` begins, walking the chain the way a reader does."""
    starts = _parse(data)
    return starts[index] if index < len(starts) else -1


def _parse(data):
    """Each clip's start offset, from the next-clip section of the one before.

    A tiny reader rather than a call into JackAll: the point of the check is to
    look at the bytes independently of the code that wrote them.
    """
    import struct

    # A 16-byte file header, then the first clip. Inside a clip the nine section
    # offsets sit at 0x78, each measured from the clip's own start, and the last
    # of them is the chained clip.
    header = 16
    sections_at = 0x78
    starts = [header]
    at = header
    while True:
        offsets = struct.unpack_from("<9i", data, at + sections_at)
        nxt = offsets[8]
        if nxt <= 0 or at + nxt >= len(data):
            break
        at += nxt
        starts.append(at)
    return starts


def motion_survives(directory):
    """Pose, write, apply, load back, and require the bones where they were."""
    result = load()
    armature = result["armature"]
    before = _sample(armature, result["pack"])

    export_mab.write(result["pack"], RELOAD, armature)
    produced = applied(result["pack"], directory, "roundtrip")

    # Re-pack the written bank so it can be read back the same way it went out.
    reloaded = _repack(produced, directory)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    again = import_xbg.load(reloaded, lod=0, with_textures=False)
    carried = again["pack"].clips[0]["path"]
    import_mab.load(again["pack"], carried, again["armature"])
    after = _sample(again["armature"], again["pack"], carried)

    errors = 0
    worst = worst_offset = 0.0
    for key in before:
        if key not in after:
            errors += fail("%s is not posed after the round trip" % (key,))
            continue
        worst = max(worst, 1.0 - abs(before[key][0].dot(after[key][0])))
        worst_offset = max(worst_offset, (before[key][1] - after[key][1]).length)

    if worst > TOLERANCE or worst_offset > TOLERANCE:
        errors += fail("motion moved by %.3e rotation / %.3e m over %d samples"
                       % (worst, worst_offset, len(before)))
    else:
        print("motion survives: %d samples, worst %.2e rotation, %.2e m"
              % (len(before), worst, worst_offset))
    return errors


def _repack(bank_bytes, directory):
    """Put a written .mab back into a pack, so it can be imported again.

    The written bank is named by where it sits on disk rather than by its game
    path, so the pack that comes back indexes it under that - which is why the
    caller reads the clip out of the manifest instead of asking for RELOAD.
    """
    beside = os.path.join(directory, "reimport")
    os.makedirs(beside, exist_ok=True)
    written = os.path.join(beside, os.path.basename(RELOAD))
    with open(written, "wb") as handle:
        handle.write(bank_bytes)

    out = os.path.join(directory, "reimported.fc2model")
    result = subprocess.run(
        [CLI, "fc2model", "export", _loose_model(), "-o", out, "--clip", written],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("jackall could not re-pack the written bank:\n%s\n%s"
                           % (result.stdout, result.stderr))
    return out


def _loose_model():
    for found in find(".xbg"):
        if os.path.basename(found).lower() == "ak47.xbg":
            return found
    raise RuntimeError("the rifle is not in the corpus")


def _sample(armature, pack_object, bank_path=RELOAD):
    """Each posed bone's local rotation and offset at a spread of frames."""
    bank = pack_object.clip(bank_path)
    clip = clip_for(bank, pack_object.rig())
    last = timing(clip)[0]
    names = [bone["name"] for bone in pack_object.rig()["bones"]]

    out = {}
    for frame in range(0, last + 1, max(1, last // 12)):
        bpy.context.scene.frame_set(frame + FIRST_FRAME)
        for name in names:
            bone = armature.pose.bones.get(name)
            if bone is None:
                continue
            local = (bone.parent.matrix.inverted() @ bone.matrix) if bone.parent \
                else bone.matrix.copy()
            out[(name, frame)] = (local.to_quaternion(), local.to_translation())
    return out


CHARACTER = "graphics/actors/buddy_andrehyppolite/andrehyppolite.xbg"
PELVIS_REF = "graphics/characters/_common/pelvis_ref.skeleton"
EDITED = "L Hand"
SECTIONS = ("constant_rotations", "keyframe_rotations",
            "constant_translations", "animated_translations")


def _by_bone(clip):
    return {section: {entry["bone"]: entry for entry in clip.get(section) or ()}
            for section in SECTIONS}


def named_bones_only():
    """A clip rewritten for one bone has to leave every other bone's entry alone.

    Rigged to pelvis_ref the clip that fits is the character's, so this is the
    arms - which the README explains is not free to re-encode.
    """
    result = load(CHARACTER, PELVIS_REF)
    armature, rig = result["armature"], result["pack"].rig()
    before = _by_bone(clip_for(result["pack"].clip(RELOAD), rig))

    bone = armature.pose.bones[EDITED]
    bone.rotation_mode = "QUATERNION"
    bone.rotation_quaternion = Quaternion((0.0, 0.0, 1.0), 0.6)
    bone.keyframe_insert("rotation_quaternion", frame=FIRST_FRAME + 40)

    written = export_mab.write(result["pack"], RELOAD, armature, bones={EDITED})
    if written["clip"] != 0:
        return fail("the character's clip is not the one that fits pelvis_ref")

    after = _by_bone(clip_for(result["pack"].clip(RELOAD), rig))
    edited = next(b["id"] for b in rig["bones"] if b["name"] == EDITED)
    checked = [(section, bone_id, entry)
               for section, entries in before.items()
               for bone_id, entry in entries.items() if bone_id != edited]

    errors = 0
    for section, entries in before.items():
        added = set(after[section]) - set(entries)
        if added:
            errors += fail("%s gained %d bone(s) the clip never addressed"
                           % (section, len(added)))
    moved = [name for section, name, entry in checked
             if after[section].get(name) != entry]
    if moved:
        errors += fail("%d bone(s) other than %s were re-encoded" % (len(moved), EDITED))
    else:
        print("named bones only: %d entries carried, %s rewritten" % (len(checked), EDITED))
    return errors


def main():
    if not require_pack():
        return 0
    errors = 0
    with tempfile.TemporaryDirectory() as directory:
        errors += other_clips_survive(directory)
        errors += motion_survives(directory)
        errors += named_bones_only()
        errors += panel_write_operator(directory)
    print("blender write: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


def panel_write_operator(directory):
    """Drive the write from the operator, the way the panel's button does.

    Against a copy of the pack: the operator saves over the file it opened, so
    running it on the shared test pack would leave every later run starting from
    an already-edited bank.
    """
    import addon
    import shutil

    copy = os.path.join(directory, "operator.fc2model")
    shutil.copyfile(pack(AK47, clips=[RELOAD]), copy)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(copy, lod=0, with_textures=False)
    import_mab.load(result["pack"], RELOAD, result["armature"])
    bpy.context.view_layer.objects.active = result["armature"]

    addon.register()
    try:
        status = bpy.ops.object.fc2_write_clip(clip=RELOAD, lossless=True)
        if status != {"FINISHED"}:
            return fail("the write operator returned %s" % status)

        # And again through the only-selected path, which reads a flag that does
        # not live where an armature's bones do.
        for bone in result["armature"].pose.bones:
            bone.select = bone.name == "CLIP"
        status = bpy.ops.object.fc2_write_clip(clip=RELOAD, only_selected=True)
        if status != {"FINISHED"}:
            return fail("the write operator returned %s for only-selected" % status)
    finally:
        addon.unregister()

    # The operator saves over the pack it opened, so re-reading it has to show
    # the bank marked edited - that is what makes an apply write it.
    entry = Pack.load(copy).entry(RELOAD)
    if entry is None or not entry.modified:
        return fail("the operator did not mark the bank edited")

    untouched = Pack.load(pack(AK47, clips=[RELOAD])).entry(RELOAD)
    if untouched is not None and untouched.modified:
        return fail("the operator wrote over the pack it was given a copy of")
    print("operator: wrote lossless and marked %s edited" % os.path.basename(RELOAD))
    return 0


if __name__ == "__main__":
    sys.exit(main())
