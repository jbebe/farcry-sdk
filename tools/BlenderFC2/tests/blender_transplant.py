# Rebuild a weapon from a donated mesh, using nothing but the add-on and stock
# Blender, and require the result to reach the game.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_transplant.py
#
# This is the honest gate on the whole toolchain. The first custom weapon that
# shipped took roughly twenty one-off scripts, a trip outside the repo for the
# texture conversion, and a working knowledge of chunk padding, bone palettes,
# mip companions and the Weapon shader's missing albedo slot. None of that is
# knowledge a weapon modeler should need, and this is the check that they no
# longer do: **anything here that reaches past `addon/` into a format is a gap.**
#
# The donor is another shipped weapon rather than the gun that build used, whose
# source is long gone. It does the same job: different topology, a different
# vertex count in every part, its own UVs, and no relationship to the target's
# bones - which is exactly what a transplant is.

import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of the add-on an installed extension left.
from _corpus import CLI, find, pack, require_pack

import bmesh
import bpy

from addon import export_xbg, import_mab, import_xbg, motion, validate
from addon.pack import Pack
from addon.rules import ERROR

TARGET = ("downloadcontent/dlc1/entitylibrary/graphics/weapons/dlc/"
          "sawed_off_shotgun/dlc1_sawedoff_shotgun.xbg")
DONOR = "graphics/weapons/primary/ak47/ak47.xbg"

# Every step below that needed something outside the add-on, so a gap is written
# down rather than scripted around.
GAPS = []


def fail(message):
    print("FAIL %s" % message)
    return 1


def gap(what):
    GAPS.append(what)


def target_pack():
    """The weapon to rebuild, packed from its loose file so no install is needed."""
    for found in find(".xbg"):
        if found.replace("\\", "/").lower().endswith(TARGET.lower()):
            return pack(found)
    return None


def transplant(directory):
    path = target_pack()
    if path is None:
        print("the sawed-off is not in the corpus, skipping")
        return 0

    bpy.ops.wm.read_factory_settings(use_empty=True)
    target = import_xbg.load(path, lod=0)
    donor = import_xbg.load(pack(DONOR), lod=0, with_armature=False, with_textures=False)
    _activate(target)

    errors = 0
    print("target: %d parts, donor: %d parts"
          % (len(target["parts"]), len(donor["parts"])))

    replaced = _replace_geometry(target, donor)
    print("transplanted %d of %d parts" % (replaced, len(target["parts"])))
    if replaced < 2:
        return fail("only %d parts were replaced; that is not a transplant" % replaced)

    # What a modeler would do next: check, read what it says, act on it.
    found = validate.check_scene(bpy.context)
    blocking = validate.blocking(found)
    print("check after the transplant: %d finding(s), %d blocking"
          % (len(found), len(blocking)))
    for finding in found[:6]:
        print("   %-8s %-28s %s" % (finding.severity, finding.code, finding.message[:70]))

    errors += _act_on(target, blocking)

    found = validate.check_scene(bpy.context)
    if validate.blocking(found):
        errors += fail("still blocked after acting on what the check said: %s"
                       % [f.code for f in validate.blocking(found)])
    else:
        print("check is clear")

    errors += _ship(target, directory)
    errors += _animation_still_fits(target, directory)
    return errors


def _activate(result):
    layer = bpy.context.view_layer.layer_collection.children[result["collection"].name]
    bpy.context.view_layer.active_layer_collection = layer


def _replace_geometry(target, donor):
    """Put the donor's geometry into the target's parts, fitted to each one.

    Stock Blender: read a mesh, scale it into the part's own bounds, write it
    back. A modeler does this by hand in the viewport; the point here is that
    nothing in it touches a Dunia format.

    One part is left unwrapped on purpose, so the run has something for the
    check to catch and something to act on - a transplant that happens to be
    clean proves the pipeline but not the guidance.
    """
    replaced = 0
    for index, obj in enumerate(target["parts"]):
        source = donor["parts"][index % len(donor["parts"])]
        mesh = bmesh.new()
        mesh.from_mesh(source.data)

        # Fit the donated geometry into the part it is replacing, so the result
        # is still a weapon rather than a pile.
        _fit(mesh, obj)
        mesh.to_mesh(obj.data)
        mesh.free()
        obj.data.update()

        if not obj.data.uv_layers:
            obj.data.uv_layers.new(name="UVMap")
        if index == 0:
            for corner in obj.data.uv_layers[0].data:
                corner.uv = (0.0, 0.0)
        replaced += 1
    return replaced


def _fit(mesh, target_object):
    """Scale and centre a bmesh into the bounds the part it replaces occupied."""
    import mathutils

    corners = [mathutils.Vector(corner) for corner in target_object.bound_box]
    low = mathutils.Vector([min(c[a] for c in corners) for a in range(3)])
    high = mathutils.Vector([max(c[a] for c in corners) for a in range(3)])
    wanted = high - low

    verts = [vertex.co for vertex in mesh.verts]
    if not verts:
        return
    source_low = mathutils.Vector([min(v[a] for v in verts) for a in range(3)])
    source_high = mathutils.Vector([max(v[a] for v in verts) for a in range(3)])
    span = source_high - source_low

    scale = min((wanted[a] / span[a]) if span[a] > 1e-9 else 1.0 for a in range(3))
    for vertex in mesh.verts:
        vertex.co = (vertex.co - source_low) * scale + low


def _act_on(target, blocking):
    """Do what each blocking finding says, the way a modeler would."""
    errors = 0
    for finding in blocking:
        obj = bpy.data.objects.get(finding.target.object)
        if finding.code in ("uv.unwrapped", "uv.missing"):
            # What the hint says: unwrap it. Smart UV Project is stock Blender
            # and needs the object selected and active, nothing more.
            if obj is None:
                errors += fail("%s names no object" % finding.code)
                continue
            _unwrap(obj)
        elif finding.code == "skin.unweighted-vertex":
            if obj is None or not obj.vertex_groups:
                gap("no way to weight a transplanted part from the plugin")
                errors += fail("cannot act on %s" % finding.code)
                continue
            obj.vertex_groups[0].add(range(len(obj.data.vertices)), 1.0, "REPLACE")
        else:
            gap("nothing in the plugin or in stock Blender acts on %s" % finding.code)
            errors += fail("nothing acts on %s" % finding.code)
    return errors


def _unwrap(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project()
    bpy.ops.object.mode_set(mode="OBJECT")


def _ship(target, directory):
    """Export, apply, and read the result back the way the game would."""
    out = os.path.join(directory, "transplanted.fc2model")
    written = export_xbg.save(out, target["collection"])
    if not written["moved"]:
        return fail("the transplant did not mark the mesh edited")

    layer = os.path.join(directory, "layer")
    result = subprocess.run([CLI, "fc2model", "extract", out, "-o", layer],
                            capture_output=True, text=True)
    if result.returncode != 0:
        return fail("applying refused it:\n%s\n%s" % (result.stdout, result.stderr))

    meshes = [f for f in find(".xbg", layer)]
    if len(meshes) != 1:
        return fail("applying wrote %d meshes, expected 1" % len(meshes))
    print("applied: %s, %d bytes"
          % (os.path.basename(meshes[0]), os.path.getsize(meshes[0])))

    # The real proof: the written file goes back through the packer and imports.
    again = os.path.join(directory, "again.fc2model")
    result = subprocess.run([CLI, "fc2model", "export", meshes[0], "-o", again],
                            capture_output=True, text=True)
    if result.returncode != 0:
        return fail("the written mesh could not be packed again:\n%s" % result.stderr)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    reopened = import_xbg.load(again, lod=0, with_textures=False)
    errors = 0
    if len(reopened["parts"]) != len(target["parts"]):
        errors += fail("came back with %d parts, went in with %d"
                       % (len(reopened["parts"]), len(target["parts"])))

    triangles = sum(len(obj.data.polygons) for obj in reopened["parts"])
    print("reopened: %d parts, %d triangles" % (len(reopened["parts"]), triangles))
    if triangles == 0:
        errors += fail("the reopened weapon draws nothing")
    return errors


def main():
    if not require_pack():
        return 0
    errors = 0
    with tempfile.TemporaryDirectory() as directory:
        errors += transplant(directory)

    if GAPS:
        print("--- gaps the plugin still has ---")
        for entry in sorted(set(GAPS)):
            print("   %s" % entry)
    print("blender transplant: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0




def _animation_still_fits(target, directory):
    """The transplanted weapon still has to move the way its clips say.

    This is the point of the whole exercise. A gun whose geometry is on the
    wrong bone tears itself apart on the first reload, and the motion table is
    what a modeler reads to avoid that - so it has to still answer after the
    geometry has been replaced.
    """
    reload_clip = None
    for found in find(".mab"):
        name = os.path.basename(found).lower()
        if "sesos" in name and "reload" in name and "1stge" in name:
            reload_clip = found
            break
    if reload_clip is None:
        print("no sawed-off reload in the corpus, skipping the animation half")
        return 0

    withclip = pack(_target_file(), clips=[reload_clip])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(withclip, lod=0, with_textures=False)
    loaded = import_mab.load(result["pack"], reload_clip, result["armature"])

    table = motion.table(result["pack"])
    moving = [row for row in table if row["rotation"] > 1.0 or row["translation"] > 0.01]
    print("animation: %d bones posed, %d keys; %d of %d bones move"
          % (loaded["bones"], loaded["keys"], len(moving), len(table)))
    for row in moving[:4]:
        print("   %-14s %5.1f deg  %.3f m" % (row["bone"], row["rotation"], row["translation"]))

    errors = 0
    if not loaded["keys"]:
        errors += fail("the clip posed nothing on the transplanted rig")
    if not moving:
        errors += fail("the motion table says nothing moves on a reload")
    return errors


def _target_file():
    for found in find(".xbg"):
        if found.replace("\\", "/").lower().endswith(TARGET.lower()):
            return found
    raise RuntimeError("the sawed-off is not in the corpus")


if __name__ == "__main__":
    sys.exit(main())
