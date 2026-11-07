"""The map's banks on foot remade for the sheet held open in both hands. In every held bank the sheet stands
DISTANCE from the eye and PITCH below the level view, facing it, its side columns bowed BOW toward the eye,
so looked at, at an 87 degree field of view, it fills a 4:3 screen and a 16:9 one shows the hands on its
edges; SWAY of the
bank's own sway of the map against the idle is kept, as a move of the sheet in its own frame. The left hand holds the
left edge as the idle's held the map's, and the right one the right edge, mirrored. Banks that carry the map
lowered hang the sheet from the left hand where the bank has it. The columns flex a little with each bank's
rhythm, each to its own beat. Where a hand would be past REACH of its arm the sheet comes toward the body.

    blender -b --python hold.py -- <in.fc2model> <out.fc2model>
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools", "BlenderFC2",
                                "tests"))
# Importing it evicts an installed copy of the add-on.
from _corpus import PACKAGE_ROOT  # noqa: F401

import bpy
from mathutils import Matrix, Quaternion, Vector

from addon import convert, export_mab, import_mab, import_xbg
from addon.import_mab import FIRST_FRAME, clip_for, timing
from addon.pack import Pack

argv = sys.argv[sys.argv.index("--") + 1:]
SOURCE, TARGET = argv[0], argv[1]
# How far the sheet's middle is from the eye, in metres, and how far below the level view, in degrees: its
# top edge near the horizon, so the sheet reaches from there down toward the legs and the view looks down to
# read it, held out on all but straight arms; the sheet's printed scale, render_map.py's SCALE, sizes it to
# fill the same view there. How far its side columns bow toward the eye on top of their rest, in degrees;
# sheet.py shapes the paper's rounded folds for the two together, its HELD.
DISTANCE, PITCH = 0.625, 40.0
BOW = 24.0
# How far below the middle row's centre line the hands hold the side edges, in metres.
GRIP_HEIGHT = -0.03
# The idle's grip on the map: the point of the map's left edge its left hand holds, in the map's frame.
MAP_EDGE = Vector((-0.135, -0.127, 0.061))
# How much of the arm's length a hand may be from its shoulder: the arm all but straight, its elbow bent a
# little under 30 degrees.
REACH = 0.97
# How much of a bank's sway of the map, against the idle, the sheet keeps: two hands hold it steadier than
# one held the map; but all of it where the bank brings the map up or puts it away.
SWAY = 0.5
WHOLE_SWAY = ("_draw_", "_holster_")
# How far each column flexes either way in a bank, in degrees, more where the map is carried lowered.
FLEX, LOWERED_FLEX = 1.2, 3.0
# Banks whose map is carried lowered, and of those the ones whose right arm stays the map's own: running,
# it swings against the left.
LOWERED = ("_run_", "_slide")
OWN_RIGHT = ("_run_",)
# The bank the grip and the sheet's resting place are taken from.
IDLE = "_idle_"
FINGERS = ["%s0%d" % (finger, joint) for finger in ("Index", "Middle", "Ring", "Pinky", "Thumb") for joint in (1, 2, 3)]
ARM = ["Clavicle", "UpperArm", "Forearm"]
MIRRORED = ARM + ["Hand"] + FINGERS
HELD = ["Hand"] + FINGERS
WRITTEN = ["R " + name for name in MIRRORED] + ["L " + name for name in ARM + ["Hand"]]
# Each column's panels, which side they bow to, and where in the bank's rhythm they flex.
COLUMNS = {"PanelL": (1, 0.0), "PanelTL": (1, 0.0), "PanelBL": (1, 0.0),
           "PanelR": (-1, 0.35), "PanelTR": (-1, 0.35), "PanelBR": (-1, 0.35)}
# The middle column's panels, which keep their rest but are written too.
STILL = ("PanelC", "PanelT", "PanelB")
# The body's rest pose is symmetric across world x, and so is the sheet across its own middle.
SIDES = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))
# The sheet below the eye, in the camera's frame (x right, y ahead, z up): its x along the camera's, its y up
# and leaning away as far as it is below, its front toward the eye.
_DOWN = math.radians(PITCH)
FACING = Matrix(((1, 0, 0, 0),
                 (0, math.sin(_DOWN), -math.cos(_DOWN), DISTANCE * math.cos(_DOWN)),
                 (0, math.cos(_DOWN), math.sin(_DOWN), -DISTANCE * math.sin(_DOWN)),
                 (0, 0, 0, 1)))


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


def load(pack, bank, count=None):
    """The sheet and body posed by the bank, its last frame, and its first `count` frames, or all."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    built = import_xbg.build(pack, "sheet", lod=0, with_textures=False)
    model, body = built["armature"], built["actor"]["armature"]
    loaded = import_mab.load(pack, bank, model, actor=built["actor"])
    last = timing(clip_for(loaded["bank"], pack.rig(actor=True)))[0]
    chest = rest(body, "Spine2").inverted() @ SIDES @ rest(body, "Spine2")
    frames = []
    for frame in range(last + 1 if count is None else count):
        bpy.context.scene.frame_set(frame + FIRST_FRAME)
        spine = node(body, "Spine2")
        frames.append({"map": node(model, model.pose.bones[0].name), "camera": node(body, "Camera"),
                       "across_chest": spine @ chest @ spine.inverted(),
                       "shoulders": {side: node(body, side + " UpperArm").translation.copy() for side in "LR"},
                       "left": {name: node(body, "L " + name) for name in MIRRORED}})
    return model, body, last, frames


def idle(pack):
    """The map in the camera's frame in the idle's first frame, the left hand's grip on the left edge in its
    column's frame, and how long an arm is."""
    model, body, _last, frames = load(pack, next(c["path"] for c in pack.clips if IDLE in c["path"]), 1)
    f = frames[0]
    corner = rest(model, model.pose.bones[0].name).inverted() @ rest(model, "UpperLeftCorner")
    panel = -corner.translation.x * 2 / 3
    edge = Vector((-panel, GRIP_HEIGHT, 0.0))
    held = {name: Matrix.Translation(edge - MAP_EDGE) @ f["map"].inverted() @ f["left"][name] for name in HELD}
    arm = sum((node(body, "L " + b).translation - node(body, "L " + a).translation).length
              for a, b in (("UpperArm", "Forearm"), ("Forearm", "Hand")))
    return f["camera"].inverted() @ f["map"], held, arm


def remake(pack, bank, resting, held, arm):
    model, body, last, frames = load(pack, bank)
    lowered = any(part in bank for part in LOWERED)
    own_right = any(part in bank for part in OWN_RIGHT)
    root = model.pose.bones[0]
    swap = swaps(body)
    scene = bpy.context.scene

    # The columns' bow and flex, keyed first so the panels can be read where they are.
    amplitude = math.radians(LOWERED_FLEX if lowered else FLEX)
    for name, (side, phase) in COLUMNS.items():
        bone = model.pose.bones[name]
        bone.rotation_mode = "QUATERNION"
        for frame in range(last + 1):
            angle = side * (math.radians(BOW) + amplitude * math.sin(2.0 * math.pi * (frame / (last + 1) + phase)))
            bone.rotation_quaternion = convert.to_bone(Quaternion((0.0, 1.0, 0.0), angle))
            bone.keyframe_insert("rotation_quaternion", frame=frame + FIRST_FRAME)

    # Where the sheet is: in front of the eye, swayed as the bank moves the map; or, lowered, where the left
    # hand puts its edge.
    share = 1.0 if any(part in bank for part in WHOLE_SWAY) else SWAY
    sheets = []
    for frame, f in enumerate(frames):
        if lowered:
            scene.frame_set(frame + FIRST_FRAME)
            column = node(model, root.name).inverted() @ node(model, "PanelL")
            sheets.append(f["left"]["Hand"] @ held["Hand"].inverted() @ column.inverted())
        else:
            location, rotation, _ = (resting.inverted() @ f["camera"].inverted() @ f["map"]).decompose()
            sway = Matrix.Translation(location * share) @ Quaternion().slerp(rotation, share).to_matrix().to_4x4()
            sheets.append(f["camera"] @ FACING @ sway)
    sheet_target = target("sheet")
    for frame, sheet in enumerate(sheets):
        key(sheet_target, sheet @ convert.BONE_AXES, frame + FIRST_FRAME)
    root.constraints.new("COPY_TRANSFORMS").target = sheet_target

    # Each hand on its edge, the right one mirrored from the left and set on its own column; a held sheet comes
    # toward the body where a hand would be out of reach.
    chested = [] if own_right else MIRRORED if lowered else ARM
    rights = {} if own_right else {name: target("R " + name) for name in MIRRORED}
    left = target("L Hand")
    brought, worst, out = 0.0, {"left": 0.0, "right": 0.0}, 0.0
    goals = []
    for frame, f in enumerate(frames):
        scene.frame_set(frame + FIRST_FRAME)
        sheet = node(model, root.name)
        column_l, column_r = node(model, "PanelL"), node(model, "PanelR")
        lefts = ({name: f["left"][name] for name in HELD} if lowered
                 else {name: column_l @ held[name] for name in HELD})
        across = sheet @ SIDES @ sheet.inverted()
        settle = column_r @ (across @ column_l @ SIDES).inverted()
        goal = {name: f["across_chest"] @ f["left"][name] @ swap[name] for name in chested}
        if not lowered:
            goal.update({name: settle @ across @ lefts[name] @ swap[name] for name in HELD})
        move = Vector()
        if not lowered:
            for side, hand in (("L", lefts["Hand"]), ("R", goal["Hand"])):
                to_shoulder = f["shoulders"][side] - hand.translation
                out = max(out, to_shoulder.length / arm)
                excess = to_shoulder.length - REACH * arm
                if excess > move.length:
                    move = to_shoulder.normalized() * excess
            brought = max(brought, move.length)
            key(sheet_target, Matrix.Translation(move) @ sheet @ convert.BONE_AXES, frame + FIRST_FRAME)
        shift = Matrix.Translation(move)
        key(left, shift @ lefts["Hand"] @ convert.BONE_AXES, frame + FIRST_FRAME)
        for name, empty in rights.items():
            key(empty, (shift if name in HELD else Matrix.Identity(4)) @ goal[name] @ convert.BONE_AXES,
                frame + FIRST_FRAME)
        goals.append({"left": (shift @ lefts["Hand"]).translation,
                      "right": (shift @ goal["Hand"]).translation if rights else None})

    # Each arm reaches its hand with its elbow and shoulder, its collarbone keeping the bank's pose; a lowered
    # sheet is far down, and the collarbone helps.
    chain = 3 if lowered else 2
    body.pose.bones["L Hand"].constraints.new("COPY_ROTATION").target = left
    reach(body, "L Forearm", left, chain)
    for name, empty in rights.items():
        body.pose.bones["R " + name].constraints.new("COPY_ROTATION").target = empty
    if rights:
        reach(body, "R Forearm", rights["Hand"], chain)

    for frame in range(last + 1):
        scene.frame_set(frame + FIRST_FRAME)
        reached = {"left": node(body, "L Hand").translation, "right": node(body, "R Hand").translation}
        for side in worst:
            if goals[frame][side] is not None:
                worst[side] = max(worst[side], (reached[side] - goals[frame][side]).length)
    export_mab.write(pack, bank, body, bones=[b for b in WRITTEN if not (own_right and b.startswith("R "))],
                     lossless=True)
    export_mab.write(pack, bank, model, bones=[root.name] + list(STILL) + list(COLUMNS), lossless=True)
    reached = ("hands out to %3.0f%% of the arm before the sheet is brought in %.1f cm" % (out * 100, brought * 100)
               if not lowered else "hung from the left hand")
    print("%-55s %-7s %3d frames, %s; off target by at most: left %.1f mm, right %.1f mm"
          % (bank.split("/")[-1], "lowered" if lowered else "held", last + 1, reached, worst["left"] * 1000.0,
             worst["right"] * 1000.0))


pack = Pack.load(SOURCE)
resting, held, arm = idle(pack)
for clip in pack.clips:
    remake(pack, clip["path"], resting, held, arm)
pack.save(TARGET)
