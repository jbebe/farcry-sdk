# Import the AK-47 inside Blender and check the result is what the file says.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_import.py
#
# Runs headless, so it works as a regression gate without opening the UI.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

import bpy
import mathutils

from _corpus import GRAPHICS, present
from addon import import_xbg
from fc2fmt.xbg import XbgFile

AK47 = os.path.join(GRAPHICS, "weapons", "primary", "ak47", "ak47.xbg")


def fail(message):
    print("FAIL %s" % message)
    return 1


def check_bounds(model, parts):
    """Placed objects must fill the bounds the file ships, and not exceed them.

    This is what catches a part being transformed twice: bone-parenting an
    object whose vertices already carry the placement moves it off the model,
    which shows up here long before anyone looks at a render.
    """
    corners = [obj.matrix_world @ mathutils.Vector(corner)
               for obj in parts for corner in obj.bound_box]
    errors = 0
    for axis in range(3):
        low = min(c[axis] for c in corners)
        high = max(c[axis] for c in corners)
        slack = abs(model.bbox[axis + 3] - model.bbox[axis]) * 0.05 + 1e-3
        if abs(low - model.bbox[axis]) > slack or abs(high - model.bbox[axis + 3]) > slack:
            errors += fail("axis %d spans %.3f..%.3f, file says %.3f..%.3f"
                           % (axis, low, high, model.bbox[axis], model.bbox[axis + 3]))
    return errors


def main():
    if not present():
        print("corpus not present, skipping")
        return 0
    bpy.ops.wm.read_factory_settings(use_empty=True)

    result = import_xbg.load(AK47, lod=0)
    model, parts, armature = result["model"], result["parts"], result["armature"]
    errors = 0

    if len(parts) != 6:
        errors += fail("expected 6 LOD0 parts, got %d" % len(parts))
    if armature is None or len(armature.data.bones) != len(model.nodes):
        errors += fail("armature should carry %d bones" % len(model.nodes))

    names = {obj.name.split(".")[0] for obj in parts}
    for expected in ("FRAME_LOD0", "CLIP_LOD0", "SLIDE_LOD0", "ACCESSORY_LOD0"):
        if expected not in names:
            errors += fail("missing part %s" % expected)

    triangles = sum(len(obj.data.polygons) for obj in parts)
    expected_triangles = sum(c.face_count for d in model.skin_descs if d.lod == 0
                             for c in d.clusters)
    if triangles != expected_triangles:
        errors += fail("built %d triangles, file says %d" % (triangles, expected_triangles))

    if not all(obj.data.uv_layers for obj in parts):
        errors += fail("every AK-47 part should carry UVs")

    errors += check_bounds(model, parts)

    # The muzzle bone must sit at the far end of the barrel, which is the
    # model's maximum on the forward axis.
    muzzle = armature.data.bones.get("FX_FIRE")
    if muzzle is None:
        errors += fail("FX_FIRE bone missing")
    elif abs(muzzle.head_local.y - model.bbox[4]) > 1e-3:
        errors += fail("FX_FIRE at y=%.3f, model maximum is %.3f"
                       % (muzzle.head_local.y, model.bbox[4]))

    # Outward-facing geometry: a correctly wound gun has more polygons facing
    # away from its centre than towards it.
    outward = 0
    for obj in parts:
        for polygon in obj.data.polygons:
            if polygon.normal.dot(polygon.center - obj.data.polygons[0].center) >= 0:
                outward += 1
    if outward * 2 < triangles:
        errors += fail("winding looks inverted: %d of %d faces outward" % (outward, triangles))

    print("ak47: parts %d  triangles %d  bones %d  materials %d"
          % (len(parts), triangles, len(armature.data.bones), len(model.materials)))

    errors += check_character()
    print("blender import: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


def check_character():
    """A skinned character exercises the palette to vertex-group chain."""
    path = os.path.join(GRAPHICS, "actors", "buddy_andrehyppolite", "andrehyppolite.xbg")
    if not os.path.exists(path):
        return 0
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0)
    model, parts, armature = result["model"], result["parts"], result["armature"]
    errors = 0

    skinned = [obj for obj in parts if obj.vertex_groups]
    if len(skinned) != len(parts):
        errors += fail("%d of %d character parts have no vertex groups"
                       % (len(parts) - len(skinned), len(parts)))
    if not all(obj.modifiers for obj in parts):
        errors += fail("character parts should carry an Armature modifier")

    bones = {bone.name for bone in armature.data.bones}
    if len(bones) != len(model.nodes):
        errors += fail("armature has %d bones for %d nodes" % (len(bones), len(model.nodes)))
    unknown = {group.name for obj in parts for group in obj.vertex_groups} - bones
    if unknown:
        errors += fail("vertex groups name no bone: %s" % sorted(unknown)[:4])

    # Weights come from bytes, so every vertex should sum to roughly one.
    off = 0
    for obj in parts:
        for vertex in obj.data.vertices:
            total = sum(g.weight for g in vertex.groups)
            if abs(total - 1.0) > 0.02:
                off += 1
    if off:
        errors += fail("%d vertices do not have weights summing to 1" % off)

    errors += check_bounds(model, parts)

    groups = len({g.name for obj in parts for g in obj.vertex_groups})
    print("character: parts %d  bones %d  weighted groups %d" % (len(parts), len(bones), groups))
    return errors


if __name__ == "__main__":
    sys.exit(main())
