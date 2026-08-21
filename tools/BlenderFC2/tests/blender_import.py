# Import the AK-47 inside Blender and check the result is what the file says.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_import.py
#
# Runs headless, so it works as a regression gate without opening the UI.

import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

import bpy
import mathutils

# _corpus first: it evicts any copy of these packages that an installed
# extension already put in sys.modules.
from _corpus import GRAPHICS, present

import addon
from addon import import_xbg
from fc2fmt.assets import InstallAssets, find_root, normalise
from fc2fmt.bundle import Bundle
from fc2fmt.xbg import XbgFile

AK47 = os.path.join(GRAPHICS, "weapons", "primary", "ak47", "ak47.xbg")


def fail(message):
    print("FAIL %s" % message)
    return 1


def check_bounds(model, parts):
    """Placed objects must fill the bounds the file ships, and not exceed them.

    This is what catches a part being transformed twice: bone-parenting an
    object whose vertices already carry the placement moves it off the model,
    which shows up here long before anyone looks at a render. Slack comes from
    the whole diagonal, because a flat model has an axis of span zero and a
    per-axis tolerance would be blind along it.
    """
    corners = [obj.matrix_world @ mathutils.Vector(corner)
               for obj in parts for corner in obj.bound_box]
    diagonal = (mathutils.Vector(model.bbox[3:]) - mathutils.Vector(model.bbox[:3])).length
    slack = diagonal * 0.02 + 1e-3
    errors = 0
    for axis in range(3):
        low = min(c[axis] for c in corners)
        high = max(c[axis] for c in corners)
        if abs(low - model.bbox[axis]) > slack or abs(high - model.bbox[axis + 3]) > slack:
            errors += fail("axis %d spans %.3f..%.3f, file says %.3f..%.3f"
                           % (axis, low, high, model.bbox[axis], model.bbox[axis + 3]))
    return errors


def check_textures(parts, source):
    """Every part should end up with a material backed by a real image."""
    if not source:
        return fail("no asset source, so no textures were resolved")
    missing = [obj.name for obj in parts
               if not any(node.type == "TEX_IMAGE" and node.image
                          for slot in obj.data.materials if slot and slot.node_tree
                          for node in slot.node_tree.nodes)]
    return fail("no image on %d parts: %s" % (len(missing), missing[:3])) if missing else 0


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
    errors += check_textures(parts, result["source"])

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
    errors += check_bundle()
    errors += check_operators()
    print("blender import: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


def check_operators():
    """Register the add-on and drive the importers the way the File menu does.

    Both operators take their options from a shared mixin, so passing one that
    Blender never collected as a property is what this is here to catch.
    """
    errors = 0
    with tempfile.TemporaryDirectory() as directory:
        path = os.path.join(directory, "ak47.fc2model")
        make_bundle(AK47).write(path)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        addon.register()
        try:
            for operator, target in ((bpy.ops.import_scene.fc2_bundle, path),
                                     (bpy.ops.import_scene.fc2_xbg, AK47)):
                bpy.ops.wm.read_factory_settings(use_empty=True)
                status = operator(filepath=target, lod=0, with_armature=True,
                                  with_textures=True)
                meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
                if status != {"FINISHED"} or len(meshes) != 6:
                    errors += fail("%s returned %s with %d meshes"
                                   % (target, status, len(meshes)))
        finally:
            addon.unregister()
    print("operators: registered and ran both importers")
    return errors


def make_bundle(model_path):
    root = find_root(model_path)
    return Bundle.build(normalise(os.path.relpath(model_path, root)), InstallAssets(root))


def check_bundle():
    """A bundle must import to the same thing, without touching the install."""
    bundle = make_bundle(AK47)
    with tempfile.TemporaryDirectory() as directory:
        path = os.path.join(directory, "ak47.fc2model")
        bundle.write(path)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        result = import_xbg.load_bundle(path, lod=0)
    parts = result["parts"]
    errors = 0
    if len(parts) != 6:
        errors += fail("bundle gave %d LOD0 parts, the install gives 6" % len(parts))
    errors += check_bounds(result["model"], parts)
    errors += check_textures(parts, result["source"])
    print("bundle: parts %d  files %d" % (len(parts), len(bundle.entries)))
    return errors


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
    errors += check_textures(parts, result["source"])

    groups = len({g.name for obj in parts for g in obj.vertex_groups})
    print("character: parts %d  bones %d  weighted groups %d" % (len(parts), len(bones), groups))
    return errors


if __name__ == "__main__":
    sys.exit(main())
