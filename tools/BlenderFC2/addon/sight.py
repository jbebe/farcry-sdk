# What a zoomed player sees, and the eye that sees it.
#
# `SCOPE_HI` is drawn instead of the rest of the model while the player looks
# through the sight, not on top of it, so it carries its own tube, lens and
# reticle and nothing else is on screen. Gutting it leaves the zoomed view empty
# without making the file invalid, which is why this is a view and not a rule.

import math

import bpy
from mathutils import Matrix, Vector

from . import convert, import_mab, import_xbg
from .transform import trs_matrix

# The part drawn while zoomed. It exists at LOD0 only.
PART = "SCOPE_HI"

# What this hid on the way in, so leaving restores only that.
PROP_HIDDEN = "fc2_sight_hidden"

# How much wider than the part itself to frame.
MARGIN = 1.15

# Blender looks down a camera's -Z, a weapon and the eye bone down their +Y.
LOOK = Matrix.Rotation(math.radians(90.0), 4, "X")

# Camera.First's fFOV, across the screen at the default gfx_widescreenmode.
EYE_FOV = math.radians(75.0)
# The gun sits a few centimetres from the eye, inside Blender's default near clip.
EYE_NEAR = 0.01
# The bone the face hangs off.
HEAD_BONE = "Head"


def parts(collection):
    return [obj for obj in collection.objects
            if obj.get(import_xbg.PROP_PART) == PART]


def sighting(collection):
    return PROP_HIDDEN in collection


def enter(collection, pose, aspect):
    """Isolate the zoomed part and put a camera at the eye looking through it."""
    shown = parts(collection)
    camera = _camera(collection)
    _place(camera, pose, shown, aspect)
    keep = set(shown) | {camera}
    _hide(collection, [obj for obj in collection.objects if obj not in keep])
    return camera


def enter_eye(armature):
    """Look out of the body's eye bone, with the face that wraps it hidden."""
    camera = _eye_camera(armature)
    _hide(armature.users_collection[0], _head_parts(armature))
    return camera


def leave(collection):
    for name in collection.get(PROP_HIDDEN) or ():
        obj = bpy.data.objects.get(name)
        if obj is not None:
            obj.hide_set(False)
            obj.hide_render = False
    del collection[PROP_HIDDEN]


def _hide(collection, objects):
    """Hide from the viewport and from renders, which `hide_set` alone does not reach."""
    collection[PROP_HIDDEN] = [obj.name for obj in objects if not obj.hide_get()]
    for obj in objects:
        obj.hide_set(True)
        obj.hide_render = True


def _camera(collection):
    """The collection's own sight camera, so a second pack gets a second one."""
    found = next((obj for obj in collection.objects
                  if obj.type == "CAMERA" and obj.parent is None), None)
    if found is None:
        found = bpy.data.objects.new("FC2 Sight", bpy.data.cameras.new("FC2 Sight"))
        collection.objects.link(found)
    return found


def _eye_camera(armature):
    """The camera riding the eye bone at the game's first-person angle, made once."""
    found = next((obj for obj in armature.children
                  if obj.type == "CAMERA" and obj.parent_bone == import_mab.EYE_BONE), None)
    if found is None:
        found = bpy.data.objects.new("FC2 Eye", bpy.data.cameras.new("FC2 Eye"))
        armature.users_collection[0].objects.link(found)
        import_mab.attach(found, armature, import_mab.EYE_BONE)
        found.matrix_basis = LOOK
        found.data.sensor_fit = "HORIZONTAL"
        found.data.angle = EYE_FOV
        found.data.clip_start = EYE_NEAR
    return found


def _head_parts(armature):
    """The parts carried mostly by the head and the bones below it."""
    head = armature.data.bones.get(HEAD_BONE)
    if head is None:
        return []
    names = {head.name} | {bone.name for bone in head.children_recursive}
    found = []
    for obj in armature.children:
        if obj.type != "MESH":
            continue
        on_head = {group.index for group in obj.vertex_groups if group.name in names}
        if not on_head:
            continue
        carried = total = 0.0
        for vertex in obj.data.vertices:
            for group in vertex.groups:
                total += group.weight
                if group.group in on_head:
                    carried += group.weight
        if carried > total / 2.0:
            found.append(obj)
    return found


def aimed_eye(pose):
    """The eye in the weapon's own space, from where the aim pose holds the weapon."""
    weapon = convert.matrix(trs_matrix(pose["rotation"] or (0.0, 0.0, 0.0, 1.0),
                                       pose["translation"] or (0.0, 0.0, 0.0)))
    return weapon.inverted() @ LOOK


def _place(camera, pose, fit, aspect):
    """Stand the camera where the pose says the eye is, framing `fit`."""
    camera.matrix_world = aimed_eye(pose)
    _frame(camera, fit, aspect)


def _frame(camera, objects, aspect):
    """Open the lens until everything given fits, from where the camera stands.

    Both axes are measured, because Blender's `angle` covers only the longer one
    and the other falls short by the aspect.
    """
    into_camera = camera.matrix_world.inverted()
    wide = tall = 0.0
    for obj in objects:
        to_camera = into_camera @ obj.matrix_world
        for corner in obj.bound_box:
            # A camera looks down its own -Z, so anything in front has z < 0.
            local = to_camera @ Vector(corner)
            if local.z < 0.0:
                wide = max(wide, abs(local.x / local.z))
                tall = max(tall, abs(local.y / local.z))
    if not wide and not tall:
        return
    half = max(wide, tall * aspect) if aspect >= 1.0 else max(tall, wide / aspect)
    camera.data.sensor_fit = "AUTO"
    camera.data.angle = 2.0 * math.atan(half * MARGIN)
