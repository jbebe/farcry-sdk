# Check the validator: silent on retail, and loud about exactly one thing.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_check.py
#
# Two halves, and the first is the one that matters. **Retail is the definition
# of valid**, so a rule that fires on a shipped model, warning or not, is a wrong
# rule - and one wrong rule blocking a legitimate export destroys trust in the
# whole feature. That is what makes "errors block" safe to ship.
#
# The second half introduces one violation at a time and requires that exact
# code and no other. A validator nobody has watched fail is not a validator.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of the add-on an installed extension left.
from _corpus import pack, require_pack

import bmesh
import bpy

from addon import import_xbg, validate
from addon.rules import ERROR

RELOAD = "graphics/characters/_common/animations/weapons/primary/ak47/1stge_uppb_reload_+000fw_prak4_i1.mab"

MODELS = (
    ("ak47", "graphics/weapons/primary/ak47/ak47.xbg"),
    ("buggy", "graphics/vehicles/land/buggy/buggy.xbg"),
    ("character", "graphics/actors/buddy_andrehyppolite/andrehyppolite.xbg"),
)


def fail(message):
    print("FAIL %s" % message)
    return 1


def load(game_path, rig=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(pack(game_path, rig=rig), lod=0)
    # The rules read the active collection, which a fresh scene leaves at the
    # scene root rather than at what was just imported.
    layer = bpy.context.view_layer.layer_collection.children[result["collection"].name]
    bpy.context.view_layer.active_layer_collection = layer
    return result


def silent_on_retail():
    """Every rule has to say nothing about a model exactly as it shipped."""
    errors = 0
    for label, game_path in MODELS:
        rig = "graphics/characters/_common/pelvis_ref.skeleton" if label == "character" else None
        try:
            load(game_path, rig)
        except RuntimeError as error:
            print("%s: %s" % (label, error))
            continue
        found = validate.check_scene(bpy.context)
        if found:
            errors += fail("%s: %d findings on an untouched shipped model: %s"
                           % (label, len(found),
                              [(f.severity, f.code, f.message) for f in found[:3]]))
        else:
            print("%s: silent" % label)
    return errors


def one_violation(name, code, prepare, game_path=MODELS[0][1]):
    """Introduce one violation and require that code, and nothing else."""
    result = load(game_path)
    prepare(result)
    found = validate.check_scene(bpy.context)
    codes = sorted({finding.code for finding in found})

    if codes == [code]:
        message = next(f.message for f in found if f.code == code)
        print("%-28s %s" % (name, message[:96]))
        return 0
    return fail("%s: expected only %s, got %s" % (name, code, codes or "nothing"))


def unknown_object(result):
    """A new mesh object, which export would silently skip."""
    mesh = bpy.data.meshes.new("newpart")
    mesh.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    result["collection"].objects.link(bpy.data.objects.new("newpart", mesh))


def duplicate_part(result):
    """A second object claiming a part, whose write the first one loses to."""
    original = result["parts"][0]
    copy = original.copy()
    copy.data = original.data.copy()
    copy.name = original.name + "_copy"
    result["collection"].objects.link(copy)


def zero_triangles(result):
    """A part that draws nothing. None of the 32,170 shipped clusters do."""
    obj = result["parts"][0]
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    bmesh.ops.delete(mesh, geom=mesh.faces[:], context="FACES_ONLY")
    mesh.to_mesh(obj.data)
    mesh.free()
    obj.data.update()


def moved_object(result):
    """A part dragged in object mode, whose move export discards."""
    result["parts"][0].location = (0.0, 0.0, 0.5)


def unweighted_vertex(result):
    """A skinned vertex in no group, which the engine puts at the origin."""
    obj = next(o for o in result["parts"] if o.vertex_groups)
    for group in obj.vertex_groups:
        group.remove([0])


def metallic(result):
    """A PBR material dropped straight in, which reads as black plastic."""
    material = result["parts"][0].data.materials[0]
    principled = next(node for node in material.node_tree.nodes
                      if node.type == "BSDF_PRINCIPLED")
    principled.inputs["Metallic"].default_value = 1.0


def panel_operators():
    """Drive the panel the way a click does: register, check, select, measure.

    A panel is the one part of an add-on that a headless gate normally cannot
    see, and the failures are all registration-order and poll mistakes rather
    than logic - which is exactly what running the operators catches.
    """
    import addon

    errors = 0
    result = load(MODELS[0][1])
    addon.register()
    try:
        unknown_object(result)
        if bpy.ops.object.fc2_check() != {"FINISHED"}:
            return fail("the Check operator did not finish")

        state = bpy.context.scene.fc2
        if not state.checked or not len(state.findings):
            errors += fail("Check found nothing after an object was added")
        elif state.findings[0].code != "part.unknown-object":
            errors += fail("Check reported %s first" % state.findings[0].code)

        # The row names an object, so selecting it has to reach that object.
        if bpy.ops.object.fc2_select_finding() != {"FINISHED"}:
            errors += fail("the Select operator did not finish")
        elif bpy.context.view_layer.objects.active.name != state.findings[0].target_object:
            errors += fail("Select did not make %s active" % state.findings[0].target_object)

        errors += motion_table()
    finally:
        addon.unregister()
    if not errors:
        print("panel: check, select and the motion table all ran")
    return errors


def motion_table():
    """The motion table has to name the moving parts of the rifle.

    This is the thing a weapon modeler cannot get from the mesh: which bone the
    body of the gun belongs on, and which ones swing.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(pack(MODELS[0][1], clips=[RELOAD]), lod=0)
    layer = bpy.context.view_layer.layer_collection.children[result["collection"].name]
    bpy.context.view_layer.active_layer_collection = layer

    if bpy.ops.object.fc2_motion_table() != {"FINISHED"}:
        return fail("the motion table operator did not finish")

    rows = {row.name: row for row in bpy.context.scene.fc2.bones}
    print("motion: %s" % ["%s %.0fdeg %.2fm" % (r.name, r.rotation, r.translation)
                          for r in bpy.context.scene.fc2.bones])
    if not rows:
        return fail("the reload moves the rifle, and the table is empty")
    if "CLIP" not in rows:
        return fail("the magazine is not in the table; a reload moves it")
    if rows["CLIP"].translation < 0.05:
        return fail("the magazine travels %.3f m over a reload" % rows["CLIP"].translation)
    if "FRAME" in rows and rows["FRAME"].translation > rows["CLIP"].translation:
        return fail("the frame moves further than the magazine")
    return 0


def main():
    if not require_pack():
        return 0

    errors = silent_on_retail()
    print("--- one violation at a time ---")
    errors += one_violation("part.unknown-object", "part.unknown-object", unknown_object)
    errors += one_violation("part.duplicate", "part.duplicate", duplicate_part)
    errors += one_violation("cluster.zero-triangles", "cluster.zero-triangles", zero_triangles)
    errors += one_violation("object.moved", "object.moved", moved_object)
    errors += one_violation("skin.unweighted-vertex", "skin.unweighted-vertex",
                            unweighted_vertex, MODELS[2][1])
    errors += one_violation("channel.metallic", "channel.metallic", metallic)

    errors += blocks_only_on_errors()
    errors += panel_operators()
    print("blender check: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


def blocks_only_on_errors():
    """A warning must not block, or "warn about the rest" means nothing."""
    result = load(MODELS[0][1])
    metallic(result)
    found = validate.check_scene(bpy.context)
    if validate.blocking(found):
        return fail("a metallic warning blocked the export")

    unknown_object(result)
    found = validate.check_scene(bpy.context)
    if not validate.blocking(found):
        return fail("an unknown object did not block the export")
    print("blocking: warnings pass, errors stop")
    return 0


if __name__ == "__main__":
    sys.exit(main())
