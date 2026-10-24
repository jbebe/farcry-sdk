# Put a clip on an imported rig and check the bones actually land there.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_anim.py
#
# The Action is built by undoing each bone's rest transform and applying the
# clip's, so the check is the other direction: evaluate the posed armature and
# read each bone's rotation and offset relative to its parent back out. Both have
# to be what the pack stores, or the rest composition is wrong.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of the add-on an installed extension left.
from _corpus import pack, require_pack

import bpy
from bpy_extras import anim_utils
from mathutils import Quaternion, Vector

from addon import import_mab, import_xbg, motion, rig, sight
from addon.import_mab import FIRST_FRAME, bone_ids, timing
from addon.pack import bank_label, bank_order, stem

CHARACTER = "graphics/actors/buddy_andrehyppolite/andrehyppolite.xbg"
AK47 = "graphics/weapons/primary/ak47/ak47.xbg"
PELVIS_REF = "graphics/characters/_common/pelvis_ref.skeleton"
CLIPS = "graphics/characters/_common/animations"
AK47_CLIPS = CLIPS + "/weapons/primary/ak47"
RELOAD = AK47_CLIPS + "/1stge_uppb_reload_+000fw_prak4_i1.mab"
THIRD_PERSON_RELOAD = AK47_CLIPS + "/3rdge_uppb_reload_nodir_prak4_i1.mab"
AIMIRON = AK47_CLIPS + "/1stge_uppb_aimironcycle_+000fw_prak4_i1.mab"

# An upper-body clip, which holds its offsets constant; a full-body jump, which
# drives the Pelvis along a translation track; and the same reload read twice,
# once for the character and once for the weapon clip chained behind it.
CASES = (
    (CHARACTER, PELVIS_REF, AK47_CLIPS + "/1stge_uppb_aimcycle_+000fw_prak4_i1.mab"),
    (CHARACTER, PELVIS_REF, CLIPS + "/locomotion/stand/jump/3rdge_fulb_jump_+000fw_nowep_i1.mab"),
    (CHARACTER, PELVIS_REF, RELOAD),
    (AK47, None, RELOAD),
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


def check(model, rig, clip_path):
    path = pack(model, clips=[clip_path], rig=rig)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    armature = result["armature"]

    loaded = import_mab.load(result["pack"], clip_path, armature)
    clip, skeleton = loaded["clip"], result["pack"].rig()
    names = {bone["id"]: bone["name"] for bone in skeleton["bones"]}
    errors = 0

    print("%s on %s: %d bones posed, %d moved, %d unmatched, %d keys, duration %.3f"
          % (os.path.basename(clip_path), os.path.basename(rig or model),
             loaded["bones"], loaded["moved"], loaded["unmatched"], loaded["keys"],
             clip.get("duration", 0.0)))
    if loaded["bones"] < min(len(skeleton["bones"]), 20) // 2:
        errors += fail("only %d bones matched the armature" % loaded["bones"])
    if not loaded["keys"]:
        return fail("no keys were inserted")
    ids = bone_ids(clip)
    if ids and ids[-1] >= len(skeleton["bones"]):
        errors += fail("the chosen clip addresses bone %d, past the %d-bone rig"
                       % (ids[-1], len(skeleton["bones"])))

    # The mesh hangs the knee and elbow helpers off the Pelvis while the rig
    # hangs them off the limb. Posed on the mesh's tree they stay by the hip and
    # tear the mesh, so the armature has to be on the rig's tree.
    for bone in skeleton["bones"]:
        pose_bone = armature.pose.bones.get(bone["name"])
        wanted = names.get(bone["parent"])
        if pose_bone is None or wanted is None or wanted not in armature.pose.bones:
            continue
        got = pose_bone.parent.name if pose_bone.parent else None
        if got != wanted:
            errors += fail("%s hangs off %s, the rig says %s" % (bone["name"], got, wanted))

    rotations = {(bone, frame): value
                 for bone, keys in _tracks(clip, "keyframe_rotations", 4).items()
                 for frame, value in keys}
    offsets = {(bone, frame): value
               for bone, keys in _tracks(clip, "animated_translations", 3).items()
               for frame, value in keys}
    offsets.update({(entry["bone"], 0): entry["value"]
                    for entry in clip.get("constant_translations") or ()})

    # Sample every frame and compare what the rig evaluates to.
    checked = worst = worst_offset = 0
    for frame in range(timing(clip)[0] + 1):
        bpy.context.scene.frame_set(frame + FIRST_FRAME)
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
        errors += fail("differs from the pack by %.3e rotation / %.3e offset over %d samples"
                       % (worst, worst_offset, checked))
    else:
        print("  matches the pack: %d samples, worst %.2e rotation, %.2e offset"
              % (checked, worst, worst_offset))
    return errors


def _tracks(clip, key, width):
    return {entry["bone"]: [(frame, entry["values"][i * width:(i + 1) * width])
                            for i, frame in enumerate(entry["frames"])]
            for entry in clip.get(key) or ()}


def check_attachments():
    """A bank has to say what it attaches, and on which bone.

    That is the fact a weapon modeler actually needs from an animation - put the
    geometry on the wrong bone and the gun tears itself apart on the first
    reload - and it is unreadable outside JackAll unless the pack carries it
    decoded.
    """
    path = pack(CHARACTER, clips=[THIRD_PERSON_RELOAD], rig=PELVIS_REF)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    armature = result["armature"]
    loaded = import_mab.load(result["pack"], THIRD_PERSON_RELOAD, armature, with_props=True)

    props = loaded["props"]
    print("attached: %s" % ["%s on %s" % (p["participant"]["name"], p["participant"]["bone"])
                            for p in props])
    errors = 0
    if not props:
        return fail("the reload attaches a rifle, and nothing was marked")
    if not any(p["participant"]["name"] == "ak47" and p["participant"]["bone"] == "R Hand"
               for p in props):
        errors += fail("no rifle on the right hand")

    # Each marker sits on its bone with its own track applied on top.
    worst = 0.0
    for frame in range(FIRST_FRAME, FIRST_FRAME + 40):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for prop in props:
            bone = armature.pose.bones[prop["participant"]["bone"]]
            want = armature.matrix_world @ bone.matrix @ prop["object"].matrix_basis
            got = prop["object"].matrix_world
            worst = max(worst, max(abs(got[r][c] - want[r][c])
                                   for r in range(4) for c in range(4)))
    if worst > TOLERANCE:
        errors += fail("an attached marker is %.2e off its bone" % worst)
    else:
        print("  on the bone to %.2e" % worst)
    return errors


def _ak47_with_reload():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return import_xbg.load(pack(AK47, clips=[RELOAD]), lod=0, with_textures=False)


def check_weapon_clip_is_chosen():
    """A weapon rig has to reach past the character's clip to its own.

    The character's clip names bone ids no gun rig has, so taking the first clip
    in the bank silently poses nothing.
    """
    result = _ak47_with_reload()
    loaded = import_mab.load(result["pack"], RELOAD, result["armature"])
    print("weapon rig: posed %d bones, %d moved" % (loaded["bones"], loaded["moved"]))
    return 0 if loaded["bones"] == 8 and loaded["moved"] == 4 else fail(
        "posed %d bones, %d moved" % (loaded["bones"], loaded["moved"]))


def check_actor_holds_the_model():
    """A pack with clips carries the body they pose, and the model hangs off it.

    Both clips in the bank are then live at once - the character's on the body,
    the weapon's on the weapon - which is the scene a weapon animator has to fit
    the gun to, and what makes clipping visible at all.
    """
    result = _ak47_with_reload()
    if not result["pack"].actor:
        return fail("a pack carrying clips carries no body to play them on")

    actor = result["actor"]
    armature = result["armature"]
    loaded = import_mab.load(result["pack"], RELOAD, armature, with_props=True, actor=actor)
    posed = loaded["actor"]
    print("actor %s: %d bones posed, holding the model at %s"
          % (os.path.basename(result["pack"].actor), posed["bones"] if posed else 0,
             (posed or {}).get("bone")))

    errors = 0
    if posed is None:
        return fail("the body was carried but never posed")
    if posed["clip"] is loaded["clip"]:
        errors += fail("the body and the model were posed from the same clip")
    if armature.parent is not actor["armature"] or armature.parent_bone != posed.get("bone"):
        errors += fail("the model does not hang off the bone the bank names")
    if any(p["participant"]["name"] == "ak47" for p in loaded["props"]):
        errors += fail("the model was marked with an empty as well as being held")
    if not actor["parts"]:
        errors += fail("the body came with no mesh, so nothing can show a clip through it")
    return errors


def check_load_keeps_the_mode():
    """Loading a bank leaves a modeler posing the body in Pose Mode, on the body."""
    result = _ak47_with_reload()
    body = result["actor"]["armature"]
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="POSE")

    import_mab.load(result["pack"], RELOAD, result["armature"], actor=result["actor"])
    active = bpy.context.view_layer.objects.active
    print("after a load: %s in %s" % (active.name, active.mode))
    return 0 if active is body and active.mode == "POSE" else fail(
        "a load left %s active in %s" % (active.name, active.mode))


def check_every_bone_keyable():
    """Every rig bone has a channel to auto-key, and only a mover is free to translate."""
    result = _ak47_with_reload()
    body = result["actor"]["armature"]
    skeleton = result["pack"].rig(actor=True)
    movers = {bone["name"] for bone in skeleton["bones"] if rig.translates(bone)}
    body.pose.bones["Jaw"].rotation_quaternion = Quaternion((0.0, 1.0, 0.0, 0.0))
    import_mab.load(result["pack"], RELOAD, result["armature"], actor=result["actor"])

    action = body.animation_data.action
    curves = {fc.data_path for fc in anim_utils.action_get_channelbag_for_slot(
        action, body.animation_data.action_slot).fcurves}
    errors = 0
    for bone in skeleton["bones"]:
        pose_bone = body.pose.bones.get(bone["name"])
        if pose_bone is None:
            continue
        moves = bone["name"] in movers
        path = 'pose.bones["%s"].' % bone["name"]
        if path + "rotation_quaternion" not in curves:
            errors += fail("%s has no rotation channel" % bone["name"])
        if moves and path + "location" not in curves:
            errors += fail("%s may translate but has no location channel" % bone["name"])
        if any(pose_bone.lock_location) == moves:
            errors += fail("%s is %slocked" % (bone["name"], "" if moves else "not "))
    if body.pose.bones["Jaw"].rotation_quaternion.angle > 1e-6:
        errors += fail("a pose the clip does not key survived the load")
    print("keyable: %d bones, %d free to translate" % (len(skeleton["bones"]), len(movers)))
    return errors


def check_bank_order():
    """A weapon's own banks list first, ahead of a scene's that merely sort earlier."""
    folder = "graphics/characters/_common/animations/"
    paths = [folder + "special_actions/buddy_down/1stge_syns_cradlegunawayexecute_+000fw_sedea_i1.mab",
             folder + "weapons/secondary/mac10/1stge_uppb_reload_+000fw_semac_i1.mab",
             folder + "weapons/secondary/mac10/1stge_uppb_draw_+000fw_semac_i1.mab"]
    clips = [{"path": path, "label": stem(path)} for path in paths]
    listed = [bank_label(clip) for clip in bank_order(clips)]
    print("bank order: %s" % ", ".join(listed))
    expected = ["mac10/1stge_uppb_draw_+000fw_semac_i1",
                "mac10/1stge_uppb_reload_+000fw_semac_i1",
                "buddy_down/1stge_syns_cradlegunawayexecute_+000fw_sedea_i1"]
    if listed != expected:
        return fail("the banks list as %s" % listed)
    return 0


def check_eye_sees_the_sight():
    """The eye view stands where the sight picture, from the weapon alone, puts the eye."""
    import addon

    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(pack(AK47, clips=[AIMIRON]), lod=0, with_textures=False)
    import_mab.load(result["pack"], AIMIRON, result["armature"], actor=result["actor"])
    parts = result["actor"]["parts"]

    addon.register()
    try:
        bpy.ops.object.fc2_eye_view()
        camera = bpy.context.scene.camera
        bpy.context.view_layer.update()
        hidden = [obj for obj in parts if obj.hide_get() and obj.hide_render]
        bpy.ops.object.fc2_eye_view()
        restored = not any(obj.hide_get() or obj.hide_render for obj in parts)
        bpy.ops.object.fc2_eye_view()
        cameras = [obj for obj in bpy.data.objects if obj.type == "CAMERA"]
    finally:
        addon.unregister()
    if camera is None:
        return fail("the eye view looks through no camera")

    expected = sight.aimed_eye(motion.aim_pose(result["pack"]))
    armature = result["armature"]
    eye = (armature.matrix_world @ armature.pose.bones[0].matrix).inverted() @ camera.matrix_world
    apart = (eye.to_translation() - expected.to_translation()).length
    turned = eye.to_quaternion().rotation_difference(expected.to_quaternion()).angle
    print("eye: %.1e m and %.1e rad from the sight picture's, %d of %d body parts hidden"
          % (apart, turned, len(hidden), len(parts)))
    if apart > 1e-5 or turned > 1e-5:
        return fail("the eye camera is not where the sight picture puts the eye")

    # The face wraps the eye, so it is hidden; the hands it watches are not.
    hands = [obj for obj in parts if "L Hand" in obj.vertex_groups]
    if not hidden or any(obj in hidden for obj in hands):
        return fail("hid %s from the eye" % [obj.name for obj in hidden])
    if not restored:
        return fail("leaving the eye view left the face hidden")
    if cameras != [camera]:
        return fail("entering twice made %d cameras" % len(cameras))
    return 0


def main():
    if not require_pack():
        return 0

    errors = (sum(check(*case) for case in CASES)
              + check_weapon_clip_is_chosen() + check_attachments()
              + check_actor_holds_the_model() + check_load_keeps_the_mode()
              + check_every_bone_keyable()
              + check_bank_order() + check_eye_sees_the_sight())
    print("blender anim: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
