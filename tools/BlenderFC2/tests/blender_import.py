# Import the AK-47 inside Blender and check the result is what the pack says.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_import.py
#
# Runs headless, so it works as a regression gate without opening the UI.
#
# The pack is built by JackAll rather than checked in: it is a contract between
# two codebases, and a fixture written by hand would only ever test this side's
# idea of it.

import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

import bpy
import mathutils

# _corpus first: it evicts any copy of the add-on that an installed extension
# already put in sys.modules.
from _corpus import pack, require_pack

import addon
from addon import import_xbg
from addon.pack import Pack

AK47 = "graphics/weapons/primary/ak47/ak47.xbg"
CHARACTER = "graphics/actors/buddy_andrehyppolite/andrehyppolite.xbg"


def fail(message):
    print("FAIL %s" % message)
    return 1


def check_bounds(mesh, parts):
    """Placed objects must fill the bounds the pack ships, and not exceed them.

    This is what catches a part being transformed twice: bone-parenting an
    object whose vertices already carry the placement moves it off the model,
    which shows up here long before anyone looks at a render. Slack comes from
    the whole diagonal, because a flat model has an axis of span zero and a
    per-axis tolerance would be blind along it.
    """
    box = mesh["box"]
    corners = [obj.matrix_world @ mathutils.Vector(corner)
               for obj in parts for corner in obj.bound_box]
    diagonal = (mathutils.Vector(box[3:]) - mathutils.Vector(box[:3])).length
    slack = diagonal * 0.02 + 1e-3
    errors = 0
    for axis in range(3):
        low = min(c[axis] for c in corners)
        high = max(c[axis] for c in corners)
        if abs(low - box[axis]) > slack or abs(high - box[axis + 3]) > slack:
            errors += fail("axis %d spans %.3f..%.3f, pack says %.3f..%.3f"
                           % (axis, low, high, box[axis], box[axis + 3]))
    return errors


def check_textures(parts):
    """Every part should end up with a material backed by a real image."""
    missing = [obj.name for obj in parts
               if not any(node.type == "TEX_IMAGE" and node.image
                          for slot in obj.data.materials if slot and slot.node_tree
                          for node in slot.node_tree.nodes)]
    return fail("no image on %d parts: %s" % (len(missing), missing[:3])) if missing else 0


def main():
    if not require_pack():
        return 0
    bpy.ops.wm.read_factory_settings(use_empty=True)

    path = pack(AK47)
    result = import_xbg.load(path, lod=0)
    mesh, parts, armature = result["mesh"], result["parts"], result["armature"]
    errors = 0

    if len(parts) != 6:
        errors += fail("expected 6 LOD0 parts, got %d" % len(parts))
    if armature is None or len(armature.data.bones) != len(mesh["nodes"]):
        errors += fail("armature should carry %d bones" % len(mesh["nodes"]))

    names = {obj.name.split(".")[0] for obj in parts}
    for expected in ("FRAME_LOD0", "CLIP_LOD0", "SLIDE_LOD0", "ACCESSORY_LOD0"):
        if expected not in names:
            errors += fail("missing part %s" % expected)

    triangles = sum(len(obj.data.polygons) for obj in parts)
    expected_triangles = sum(len(g["indices"]) // 3 for g in mesh["lods"][0]["geometry"])
    if triangles != expected_triangles:
        errors += fail("built %d triangles, pack says %d" % (triangles, expected_triangles))

    if not all(obj.data.uv_layers for obj in parts):
        errors += fail("every AK-47 part should carry UVs")

    errors += check_bounds(mesh, parts)
    errors += check_textures(parts)

    # The muzzle bone must sit at the far end of the barrel, which is the
    # model's maximum on the forward axis.
    muzzle = armature.data.bones.get("FX_FIRE")
    if muzzle is None:
        errors += fail("FX_FIRE bone missing")
    elif abs(muzzle.head_local.y - mesh["box"][4]) > 1e-3:
        errors += fail("FX_FIRE at y=%.3f, model maximum is %.3f"
                       % (muzzle.head_local.y, mesh["box"][4]))

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
          % (len(parts), triangles, len(armature.data.bones), len(mesh["materials"])))

    errors += check_ownership(path)
    errors += check_character()
    errors += check_operators(path)
    print("blender import: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


def check_ownership(path):
    """A pack must say what may be edited, or the plugin has nothing to enforce."""
    loaded = Pack.load(path)
    errors = 0
    model = loaded.entry(loaded.model)
    if model is None or not model.owned:
        errors += fail("the model itself is not owned by its own pack")
    if not any(not entry.owned for entry in loaded.entries):
        errors += fail("nothing in the rifle's pack is shared; its detail maps are")
    if any(entry.modified for entry in loaded.entries):
        errors += fail("a freshly built pack claims something was edited")

    # The declared ceilings are what a validator reads instead of hardcoding.
    for key in ("max_cluster_triangles", "max_buffer_vertices", "max_palette_slots"):
        if not loaded.limits.get(key):
            errors += fail("the pack declares no %s" % key)
    print("pack: %d entries, %d shared"
          % (len(loaded.entries), sum(1 for e in loaded.entries if not e.owned)))
    return errors


def check_operators(path):
    """Register the add-on and drive the importer the way the File menu does.

    An operator takes its options from properties Blender collected, so passing
    one that was never declared is what this is here to catch.
    """
    errors = 0
    with tempfile.TemporaryDirectory() as directory:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        addon.register()
        try:
            status = bpy.ops.import_scene.fc2_pack(
                filepath=path, lod=0, with_armature=True, with_textures=True)
            meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
            if status != {"FINISHED"} or len(meshes) != 6:
                errors += fail("the import operator returned %s with %d meshes"
                               % (status, len(meshes)))

            out = os.path.join(directory, "exported.fc2model")
            if bpy.ops.export_scene.fc2_pack(filepath=out) != {"FINISHED"}:
                errors += fail("the export operator did not finish")
            else:
                errors += check_untouched(path, out)
        finally:
            addon.unregister()
    print("operators: registered and ran the importer and the exporter")
    return errors


def check_untouched(source, exported):
    """An untouched round trip must ask for nothing to be written.

    This is the property the whole pipeline rests on: an entry is written back
    only once an editor changed it, so opening a model and saving it cannot
    quietly recompress its textures or move its vertices by a quantisation step.
    """
    before, after = Pack.load(source), Pack.load(exported)
    changed = [entry.path for entry in after.entries if entry.modified]
    if changed:
        return fail("an untouched export marked %d entries edited: %s"
                    % (len(changed), changed[:3]))
    if before.files.keys() != after.files.keys():
        return fail("an untouched export changed which files the pack holds")
    differing = [name for name in before.files if before.files[name] != after.files[name]]
    return fail("an untouched export rewrote %s" % differing[:3]) if differing else 0


def check_character():
    """A skinned character exercises the palette to vertex-group chain."""
    try:
        path = pack(CHARACTER)
    except RuntimeError as error:
        print("character: %s" % error)
        return 0

    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0)
    mesh, parts, armature = result["mesh"], result["parts"], result["armature"]
    errors = 0

    skinned = [obj for obj in parts if obj.vertex_groups]
    if len(skinned) != len(parts):
        errors += fail("%d of %d character parts have no vertex groups"
                       % (len(parts) - len(skinned), len(parts)))
    if not all(obj.modifiers for obj in parts):
        errors += fail("character parts should carry an Armature modifier")

    # Every group must name a bone the armature actually has, or the mesh
    # collapses to the origin where a group resolves to nothing.
    bones = {bone.name for bone in armature.data.bones}
    if len(bones) != len(mesh["nodes"]):
        errors += fail("armature has %d bones for %d nodes" % (len(bones), len(mesh["nodes"])))
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

    errors += check_bounds(mesh, parts)
    errors += check_textures(parts)

    groups = len({g.name for obj in parts for g in obj.vertex_groups})
    print("character: parts %d  bones %d  weighted groups %d" % (len(parts), len(bones), groups))
    return errors


if __name__ == "__main__":
    sys.exit(main())
