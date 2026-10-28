"""Both arms of every bank the pack carries, around a notebook that stays where the map was, or comes
within the right arm's reach: the left hand moved off the pages, the right one holding the other edge
in the idle's mirrored grip, and the right page bent at its hinges with it. The design log's notebook
section says how.

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
# edge along them and toward the eye, and the right hand further out, clear of the page's edge, and
# toward the eye, its fingers and thumb as far from the page, all in metres.
LIFT, OUT, CLOSER, RIGHT_OUT, RIGHT_CLOSER = 0.03, 0.05, 0.005, 0.02, 0.0015
# The most the right hand moves against the page in a bank, bending it, in metres.
SWING = 0.003
# How far the right hand may be from where its shoulder would be as the left one's mirror: beyond it
# the notebook comes toward that shoulder, rather than the shoulder going forward to the hand.
REACH = 0.485
# A page's hinges lean this far toward the eye before the hand bends them, so a page pushed together
# bows out toward the eye rather than in.
BIAS = 2.0
# Banks whose notebook is lowered, so the hand has no edge to hold, and of those the ones whose right
# arm stays the map's own: running, it swings against the left.
LOWERED = ("_run_", "_slide")
OWN_RIGHT = ("_run_",)
# The bank whose first frame gives the right hand's grip on the page in every bank that holds it.
GRIP = "_idle_"
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


def swaps(body):
    """What turns each left bone's node into its right twin's once both are reflected."""
    return {name: rest(body, "L " + name).inverted() @ SIDES @ rest(body, "R " + name) for name in MIRRORED}


def flat_edge(model):
    """The right page's edge strip against the notebook, flat."""
    return rest(model, model.pose.bones[0].name).inverted() @ rest(model, PAGE[-1])


def load(pack, bank, count=None):
    """The notebook and body posed by the bank, its last frame, and its first `count` frames, or all,
    as the source has them."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    built = import_xbg.build(pack, "notebook", lod=0, with_textures=False)
    model, body = built["armature"], built["actor"]["armature"]
    loaded = import_mab.load(pack, bank, model, actor=built["actor"])
    last = timing(clip_for(loaded["bank"], pack.rig(actor=True)))[0]
    root = model.pose.bones[0]
    chest = rest(body, "Spine2").inverted() @ SIDES @ rest(body, "Spine2")
    frames = []
    for frame in range(last + 1 if count is None else count):
        bpy.context.scene.frame_set(frame + FIRST_FRAME)
        spine = node(body, "Spine2")
        across_chest = spine @ chest @ spine.inverted()
        frames.append({"page": node(model, root.name), "across_chest": across_chest,
                       "camera": node(body, "Camera"), "notebook": model.matrix_world @ root.matrix,
                       "pull": node(model, PAGE[-1]).translation.copy(),
                       "shoulder": across_chest @ node(body, "L UpperArm").translation,
                       "left": {name: node(body, "L " + name) for name in MIRRORED}})
    return model, body, last, frames


def grip(pack):
    """The right hand and fingers against the flat edge strip in the first frame of GRIP: the left ones
    mirrored across the pages, past the edge, lifted and toward the eye. Every held bank keeps it."""
    model, body, _last, frames = load(pack, next(c["path"] for c in pack.clips if GRIP in c["path"]), 1)
    f, swap = frames[0], swaps(body)
    page = f["page"]
    across_page = page @ ACROSS_PAGE @ page.inverted()
    out = Matrix.Translation(-page.col[0].xyz.normalized() * OUT)
    right_out = Matrix.Translation(page.col[0].xyz.normalized() * RIGHT_OUT)
    lift = Matrix.Translation(f["camera"].col[2].xyz.normalized() * LIFT)
    hand = (across_page @ out @ f["left"]["Hand"]).translation
    closer = Matrix.Translation((f["camera"].translation - hand).normalized() * RIGHT_CLOSER)
    edge = (page @ flat_edge(model)).inverted()
    return {name: edge @ closer @ right_out @ lift @ across_page @ out @ f["left"][name] @ swap[name]
            for name in MIRRORED if name not in ARM}


def mirror(pack, bank, held):
    model, body, last, frames = load(pack, bank)
    lowered = any(part in bank for part in LOWERED)
    own_right = any(part in bank for part in OWN_RIGHT)
    root = model.pose.bones[0]
    swap, edge = swaps(body), flat_edge(model)
    scene = bpy.context.scene
    # The right hand's own movement against the page, scaled to at most SWING: the left hand's mirrored
    # across the chest, along the page and toward the eye only, since a hinge cannot follow it up and
    # down. Paper does not stretch, so the page is flat where the hands are furthest apart and bows as
    # they come together.
    own = [f["page"].inverted() @ (f["across_chest"] @ f["left"]["Hand"] @ swap["Hand"]).translation for f in frames]
    widest = max(o.x for o in own)
    mean_z = sum(o.z for o in own) / len(own)
    drift = [Vector() if lowered else Vector((o.x - widest, 0.0, o.z - mean_z)) for o in own]
    swing = max((d.length for d in drift), default=0.0)
    drift = [d * min(1.0, SWING / swing) for d in drift] if swing > 0.0 else drift

    # The right bones the left ones mirror across the chest: the arm, and the hand and fingers too where
    # the notebook is lowered; none where the right arm is the source's.
    chested = [] if own_right else MIRRORED if lowered else ARM
    rights = {} if own_right else {name: target("R " + name) for name in MIRRORED}
    left, notebook, pull = target("L Hand"), target("notebook"), target("pull")
    goals, wanted, brought = [], {}, 0.0
    for frame, f in enumerate(frames):
        page = f["page"]
        hand = f["left"]["Hand"]
        out = Matrix.Translation(-page.col[0].xyz.normalized() * OUT)
        closer = Matrix.Translation((f["camera"].translation - hand.translation).normalized() * CLOSER)
        toward = Vector()
        if not lowered:
            to_shoulder = f["shoulder"] - (page @ edge @ held["Hand"]).translation
            toward = to_shoulder.normalized() * max(0.0, to_shoulder.length - REACH)
            brought = max(brought, toward.length)
        toward = Matrix.Translation(toward)
        pulled = toward @ Matrix.Translation(f["pull"] + page.to_3x3() @ drift[frame])
        key(notebook, toward @ f["notebook"], frame + FIRST_FRAME)
        key(left, toward @ closer @ out @ hand @ convert.BONE_AXES, frame + FIRST_FRAME)
        key(pull, pulled, frame + FIRST_FRAME)
        goals.append({name: toward @ f["across_chest"] @ out @ f["left"][name] @ swap[name] for name in chested})
        wanted[frame] = {"left": (toward @ closer @ out @ hand).translation, "pull": pulled.translation}

    root.constraints.new("COPY_TRANSFORMS").target = notebook
    # The right page bends about its hinges alone: each bone turns about its X, the page's up.
    for name in PAGE:
        bone = model.pose.bones[name]
        bone.lock_ik_y = bone.lock_ik_z = True
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = Quaternion((1.0, 0.0, 0.0), math.radians(BIAS))
    reach(model, PAGE[-2], pull, len(PAGE) - 1)
    # The right hand keeps its grip on the edge strip, so a bend turns the page with the fingers, not
    # under them.
    for frame, goal in enumerate(goals if rights else []):
        if not lowered:
            scene.frame_set(frame + FIRST_FRAME)
            goal.update({name: node(model, PAGE[-1]) @ g for name, g in held.items()})
        wanted[frame]["right"] = goal["Hand"].translation.copy()
        for name, empty in rights.items():
            key(empty, goal[name] @ convert.BONE_AXES, frame + FIRST_FRAME)

    # A held notebook is within reach, so each arm reaches it with its elbow and shoulder and its
    # collarbone keeps the source's pose; a lowered one is far down, and the collarbone helps.
    chain = 3 if lowered else 2
    body.pose.bones["L Hand"].constraints.new("COPY_ROTATION").target = left
    reach(body, "L Forearm", left, chain)
    for name, empty in rights.items():
        body.pose.bones["R " + name].constraints.new("COPY_ROTATION").target = empty
    if rights:
        reach(body, "R Forearm", rights["Hand"], chain)

    worst = {"left": 0.0, "right": 0.0, "pull": 0.0}
    for frame in range(last + 1):
        scene.frame_set(frame + FIRST_FRAME)
        reached = {"left": node(body, "L Hand").translation, "right": node(body, "R Hand").translation,
                   "pull": node(model, PAGE[-1]).translation}
        for part in wanted[frame]:
            worst[part] = max(worst[part], (reached[part] - wanted[frame][part]).length)
    # Every frame kept: thinning keys along the arm moves the notebook by up to 1.5 cm.
    export_mab.write(pack, bank, body, bones=[b for b in WRITTEN if not (own_right and b.startswith("R "))],
                     lossless=True)
    export_mab.write(pack, bank, model, bones=[root.name] + PAGE, lossless=True)
    print("%-55s %-7s %3d frames, hands swing %.1f cm, notebook brought in %.1f cm; off target by at most: "
          "left %.1f mm, right %.1f mm, page %.1f mm"
          % (bank.split("\\")[-1], "lowered" if lowered else "held", last + 1, min(swing, SWING) * 100,
             brought * 100, worst["left"] * 1000.0, worst["right"] * 1000.0, worst["pull"] * 1000.0))


pack = Pack.load(SOURCE)
held = grip(pack)
for clip in pack.clips:
    mirror(pack, clip["path"], held)
pack.save(TARGET)
