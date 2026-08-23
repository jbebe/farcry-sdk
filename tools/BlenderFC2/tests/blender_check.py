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


def one_violation(name, expected, prepare, game_path=MODELS[0][1]):
    """Introduce one violation and require exactly the codes it should raise.

    A set rather than a single code, because some violations genuinely imply
    another: deleting every face of a part leaves every one of its vertices
    loose, and both of those are worth saying. What the assertion still refuses
    is anything the violation does not entail.
    """
    expected = sorted(expected if isinstance(expected, (list, tuple, set)) else [expected])
    result = load(game_path)
    prepare(result)
    found = validate.check_scene(bpy.context)
    codes = sorted({finding.code for finding in found})

    if codes == expected:
        message = next(f.message for f in found if f.code == expected[0])
        print("%-30s %s" % (name, message[:92]))
        return 0
    return fail("%s: expected %s, got %s" % (name, expected, codes or "nothing"))


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


def split_uv(result):
    """One corner of a shared vertex moved, which the format cannot store.

    It has to be a vertex several faces touch: moving the only corner of a
    vertex moves the vertex, which is a perfectly representable edit.
    """
    obj = result["parts"][0]
    layer = obj.data.uv_layers[0]
    corners = {}
    for loop in obj.data.loops:
        corners.setdefault(loop.vertex_index, []).append(loop.index)
    shared = next(loops for loops in corners.values() if len(loops) > 1)
    uv = layer.data[shared[0]].uv
    layer.data[shared[0]].uv = (uv[0] + 0.25, uv[1])


def loose_vertex(result):
    """A vertex no triangle uses. Every shipped vertex is referenced."""
    obj = result["parts"][0]
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    mesh.verts.new((0.0, 0.0, 0.0))
    mesh.to_mesh(obj.data)
    mesh.free()
    obj.data.update()


def reassigned_material(result):
    """A part pointed at another part's material, which the file ignores."""
    parts = result["parts"]
    other = next(o for o in parts[1:] if o.data.materials
                 and o.data.materials[0] is not parts[0].data.materials[0])
    parts[0].data.materials[0] = other.data.materials[0]


def too_many_influences(result):
    """A vertex in more groups than the buffer addresses; the light ones go."""
    obj = next(o for o in result["parts"] if o.vertex_groups)
    weights = {group.name: 0.0 for group in obj.vertex_groups}
    vertex = obj.data.vertices[0]
    for item in vertex.groups:
        weights[obj.vertex_groups[item.group].name] = item.weight
    # Spread the vertex over one more group than the format can carry, keeping
    # the sum at one so only the influence rule has anything to say.
    spare = [g for g in obj.vertex_groups if weights[g.name] == 0.0][:9]
    for group in spare:
        group.add([vertex.index], 1.0 / len(spare), "REPLACE")
    for group in obj.vertex_groups:
        if weights[group.name]:
            group.remove([vertex.index])


def viewport_shows_what_ships():
    """The channels a material carries have to reach the viewport.

    Warning that Metallic is unsupported is only honest if the channels that
    *are* supported are visible - otherwise a modeler editing a specular map or
    a normal map sees nothing change and has no instrument but the game.

    Asserted per material against the slots it actually holds, not against a
    fixed list: the AK-47's three materials carry a specular map and no normal
    map, so requiring a normal map would be requiring something that is not
    there.
    """
    errors = 0
    wired = {}
    for label, game_path in (MODELS[0], MODELS[2]):
        rig = "graphics/characters/_common/pelvis_ref.skeleton" if label == "character" else None
        result = load(game_path, rig)
        pack = result["pack"]
        for obj in result["parts"]:
            for material in obj.data.materials:
                errors += _material_channels(material, pack, wired)
    print("wired: %s" % {key: len(value) for key, value in sorted(wired.items())})

    # Between the rifle and the character, everything the format carries should
    # have been exercised at least once.
    for name in ("Base Color", "Normal", "Roughness"):
        if name not in wired:
            errors += fail("nothing drove %s across either model" % name)
    return errors


# Which slot has to end up driving which Principled input, when the material
# carries it at all.
CHANNELS = (("DiffuseTexture1", "Base Color"),
            ("NormalTexture1", "Normal"),
            ("SpecularTexture1", "Roughness"))


def _material_channels(material, pack, wired):
    from addon import materials as fc2materials

    if material is None or not material.use_nodes:
        return 0
    path = material.get("fc2_material_path", "")
    definition = pack.material(path) if path else None
    if definition is None:
        return 0

    principled = next((n for n in material.node_tree.nodes
                       if n.type == "BSDF_PRINCIPLED"), None)
    if principled is None:
        return fail("%s has no Principled node" % material.name)

    errors = 0
    slots = fc2materials.textures(definition)
    for slot, socket_name in CHANNELS:
        if slot not in slots or pack.texture(slots[slot]) is None:
            continue
        socket = principled.inputs.get(socket_name)
        if socket is None or not socket.is_linked:
            errors += fail("%s carries %s and nothing drives %s"
                           % (material.name, slot, socket_name))
        else:
            wired.setdefault(socket_name, set()).add(material.name)

    # Every image node has to say which slot it stands for, or export cannot
    # match one back and a rule cannot tell an edited chain from a rebuilt one.
    untagged = [n.name for n in material.node_tree.nodes
                if n.type == "TEX_IMAGE" and fc2materials.PROP_SLOT not in n]
    if untagged:
        errors += fail("%s has untagged image nodes: %s" % (material.name, untagged))
    return errors


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
    errors += one_violation("cluster.zero-triangles",
                            ["cluster.zero-triangles", "mesh.loose-vertex"], zero_triangles)
    errors += one_violation("object.moved", "object.moved", moved_object)
    errors += one_violation("skin.unweighted-vertex",
                            ["skin.unweighted-vertex", "skin.weights-unnormalised"],
                            unweighted_vertex, MODELS[2][1])
    errors += one_violation("channel.metallic", "channel.metallic", metallic)
    errors += one_violation("uv.split", "uv.split", split_uv)
    errors += one_violation("mesh.loose-vertex", "mesh.loose-vertex", loose_vertex)
    errors += one_violation("material.assignment-ignored", "material.assignment-ignored",
                            reassigned_material)
    errors += one_violation("skin.influences-truncated", "skin.influences-truncated",
                            too_many_influences, MODELS[2][1])

    errors += blocks_only_on_errors()
    errors += viewport_shows_what_ships()
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
