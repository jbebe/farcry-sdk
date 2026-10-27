"""Both arms of every bank the pack carries, around a notebook that stays where the map was: the left
hand moved off the pages, the right one its mirror holding the other edge, and the right page bent at
its hinges to stay in it. The design log's notebook section says how.

    blender -b --python arms.py -- <in.fc2model> <out.fc2model>
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "tools",
                                "BlenderFC2", "tests"))
# Importing it evicts an installed copy of the add-on.
from _corpus import PACKAGE_ROOT  # noqa: F401

import bpy
from mathutils import Matrix, Quaternion, Vector

from addon import convert, export_mab, import_mab, import_xbg
from addon.import_mab import FIRST_FRAME, clip_for, timing
from addon.pack import Pack

argv = sys.argv[sys.argv.index("--") + 1:]
SOURCE, TARGET = argv[0], argv[1]
# The middle of the pages along x, in the notebook's frame, which the right hand mirrors across.
CENTRE = 0.1264
# How far the right wrist is lifted up the screen, how far the left hand is moved off the pages'
# edge along them and toward the eye, all in metres, and how much of the left hand's own movement
# the right hand takes.
LIFT, OUT, CLOSER = 0.03, 0.05, 0.005
FLEX = 0.5
# A page's hinges lean this far toward the eye before the hand bends them, so a page pushed together
# bows out toward the eye rather than in.
BIAS = 2.0
# Banks whose notebook is lowered, so the hand has no edge to hold.
LOWERED = ("_run_", "_slide")
FINGERS = ["%s0%d" % (finger, joint) for finger in ("Index", "Middle", "Ring", "Pinky", "Thumb") for joint in (1, 2, 3)]
ARM = ["Clavicle", "UpperArm", "Forearm"]
MIRRORED = ARM + ["Hand"] + FINGERS
WRITTEN = ["R " + name for name in MIRRORED] + ["L " + name for name in ARM + ["Hand"]]
# The right page's hinges, spine out; the last one's head is where the hand's pull is taken.
PAGE = ["PageR1", "PageR2", "PageR3", "PageR4"]
# The body's rest pose is symmetric across world x.
SIDES = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))
# Across the page's centre line, in the notebook's own frame: x is along the page, y up it.
ACROSS_PAGE = Matrix.Translation((2.0 * CENTRE, 0.0, 0.0)) @ SIDES


def node(obj, name):
    return obj.matrix_world @ convert.node_frame(obj.pose.bones[name].matrix)


def rest(obj, name):
    return obj.matrix_world @ convert.node_frame(obj.data.bones[name].matrix_local)


def target(name):
    empty = bpy.data.objects.new("target " + name, None)
    empty.rotation_mode = "QUATERNION"
    bpy.context.scene.collection.objects.link(empty)
    return empty


def key(empty, world, frame):
    location, rotation, _scale = world.decompose()
    empty.location, empty.rotation_quaternion = location, rotation
    empty.keyframe_insert("location", frame=frame)
    empty.keyframe_insert("rotation_quaternion", frame=frame)


def reach(armature, bone, empty, chain):
    ik = armature.pose.bones[bone].constraints.new("IK")
    ik.target, ik.chain_count = empty, chain


def mirror(pack, bank):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    built = import_xbg.build(pack, "notebook", lod=0, with_textures=False)
    model, body = built["armature"], built["actor"]["armature"]
    loaded = import_mab.load(pack, bank, model, actor=built["actor"])
    last = timing(clip_for(loaded["bank"], pack.rig(actor=True)))[0]
    lowered = any(part in bank for part in LOWERED)
    root = model.pose.bones[0]
    # What turns a left bone's node into its right twin's once both are reflected.
    swap = {name: rest(body, "L " + name).inverted() @ SIDES @ rest(body, "R " + name) for name in MIRRORED}
    chest = rest(body, "Spine2").inverted() @ SIDES @ rest(body, "Spine2")

    scene = bpy.context.scene
    frames = []
    for frame in range(last + 1):
        scene.frame_set(frame + FIRST_FRAME)
        spine = node(body, "Spine2")
        frames.append({"page": node(model, root.name), "across_chest": spine @ chest @ spine.inverted(),
                       "camera": node(body, "Camera"), "notebook": model.matrix_world @ root.matrix,
                       "pull": node(model, PAGE[-1]).translation.copy(),
                       "left": {name: node(body, "L " + name) for name in MIRRORED}})
    # The right hand's own movement against the page: the left hand's mirrored across the chest, along
    # the page and toward the eye only, since a hinge cannot follow it up and down. Paper does not
    # stretch, so the page is flat where the hands are furthest apart and bows as they come together.
    own = [f["page"].inverted() @ (f["across_chest"] @ f["left"]["Hand"] @ swap["Hand"]).translation for f in frames]
    widest = max(o.x for o in own)
    mean_z = sum(o.z for o in own) / len(own)
    drift = [Vector() if lowered else Vector(((o.x - widest) * FLEX, 0.0, (o.z - mean_z) * FLEX)) for o in own]

    rights = {name: target("R " + name) for name in MIRRORED}
    left, notebook, pull = target("L Hand"), target("notebook"), target("pull")
    wanted = {}
    for frame, f in enumerate(frames):
        page = f["page"]
        across_page = page @ ACROSS_PAGE @ page.inverted()
        lift = Matrix.Translation(f["camera"].col[2].xyz.normalized() * LIFT)
        hand = f["left"]["Hand"]
        out = Matrix.Translation(-page.col[0].xyz.normalized() * OUT)
        closer = Matrix.Translation((f["camera"].translation - hand.translation).normalized() * CLOSER)
        shift = page.to_3x3() @ drift[frame]
        key(notebook, f["notebook"], frame + FIRST_FRAME)
        key(left, closer @ out @ hand @ convert.BONE_AXES, frame + FIRST_FRAME)
        key(pull, Matrix.Translation(f["pull"] + shift), frame + FIRST_FRAME)
        wanted[frame] = {"left": (closer @ out @ hand).translation, "pull": f["pull"] + shift}
        for name, empty in rights.items():
            reflect = f["across_chest"] if lowered or name in ARM else across_page
            goal = reflect @ out @ f["left"][name] @ swap[name]
            if name == "Hand":
                goal = Matrix.Translation(shift) @ lift @ goal
                wanted[frame]["right"] = goal.translation.copy()
            key(empty, goal @ convert.BONE_AXES, frame + FIRST_FRAME)

    root.constraints.new("COPY_TRANSFORMS").target = notebook
    body.pose.bones["L Hand"].constraints.new("COPY_ROTATION").target = left
    reach(body, "L Forearm", left, 3)
    for name, empty in rights.items():
        body.pose.bones["R " + name].constraints.new("COPY_ROTATION").target = empty
    reach(body, "R Forearm", rights["Hand"], 3)
    # The right page bends about its hinges alone: each bone turns about its X, the page's up.
    for name in PAGE:
        bone = model.pose.bones[name]
        bone.lock_ik_y = bone.lock_ik_z = True
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = Quaternion((1.0, 0.0, 0.0), math.radians(BIAS))
    reach(model, PAGE[-2], pull, len(PAGE) - 1)

    worst = {"left": 0.0, "right": 0.0, "pull": 0.0}
    for frame in range(last + 1):
        scene.frame_set(frame + FIRST_FRAME)
        reached = {"left": node(body, "L Hand").translation, "right": node(body, "R Hand").translation,
                   "pull": node(model, PAGE[-1]).translation}
        for part in worst:
            worst[part] = max(worst[part], (reached[part] - wanted[frame][part]).length)
    # Every frame kept: thinning keys along the arm moves the notebook by up to 1.5 cm.
    written = export_mab.write(pack, bank, body, bones=WRITTEN, lossless=True)
    export_mab.write(pack, bank, model, bones=[root.name] + PAGE, lossless=True)
    swing = max((d.length for d in drift), default=0.0)
    print("%-55s %-7s %3d frames, hands swing %.1f cm; off target by at most: left %.1f mm, right %.1f mm, page %.1f mm"
          % (bank.split("\\")[-1], "lowered" if lowered else "held", last + 1, swing * 100, worst["left"] * 1000.0,
             worst["right"] * 1000.0, worst["pull"] * 1000.0))


pack = Pack.load(SOURCE)
for clip in pack.clips:
    mirror(pack, clip["path"])
pack.save(TARGET)
