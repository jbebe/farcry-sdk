# What a zoomed player sees, and the eye that sees it.
#
# `SCOPE_HI` is drawn instead of the rest of the model while the player looks
# through the sight, not on top of it, so it carries its own tube, lens and
# reticle and nothing else is on screen. Gutting it leaves the zoomed view empty
# without making the file invalid, which is why this is a view and not a rule.

import math

import bpy
from mathutils import Matrix, Vector

from . import convert, import_xbg
from .transform import trs_matrix

# The part drawn while zoomed. It exists at LOD0 only.
PART = "SCOPE_HI"

# What this hid on the way in, so leaving restores only that.
PROP_HIDDEN = "fc2_sight_hidden"

# How much wider than the part itself to frame.
MARGIN = 1.15


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
    collection[PROP_HIDDEN] = [obj.name for obj in collection.objects
                               if obj not in keep and not obj.hide_get()]
    for obj in collection.objects:
        if obj not in keep:
            obj.hide_set(True)
    return camera


def leave(collection):
    for name in collection.get(PROP_HIDDEN) or ():
        obj = bpy.data.objects.get(name)
        if obj is not None:
            obj.hide_set(False)
    del collection[PROP_HIDDEN]


def _camera(collection):
    """The collection's own sight camera, so a second pack gets a second one."""
    found = next((obj for obj in collection.objects if obj.type == "CAMERA"), None)
    if found is None:
        found = bpy.data.objects.new("FC2 Sight", bpy.data.cameras.new("FC2 Sight"))
        collection.objects.link(found)
    return found


def _place(camera, pose, fit, aspect):
    """Stand the camera where the pose says the eye is, framing `fit`.

    The pose holds the weapon relative to the eye, so the eye is that inverted.
    Orientation then takes a quarter turn, because Blender aims a camera down
    its own -Z while a Far Cry 2 weapon is authored pointing +Y.
    """
    weapon = convert.matrix(trs_matrix(pose["rotation"] or (0.0, 0.0, 0.0, 1.0),
                                       pose["translation"] or (0.0, 0.0, 0.0)))
    camera.matrix_world = weapon.inverted() @ Matrix.Rotation(math.radians(90.0), 4, "X")
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
