# Edit a material in Blender and require the game file JackAll writes from it.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_material.py
#
# The values held on a Blender material are what export writes, so three things
# are held here: an untouched material goes back unwritten, an edited one comes
# out of JackAll as the .xbm it describes, and a material the model shares is
# refused rather than quietly re-skinning every other model that uses it.

import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of the add-on an installed extension left.
from _corpus import CLI, pack, require_pack

import bpy

from addon import export_xbg, import_xbg, material_data, validate
from addon.pack import Pack
from addon.rules import ERROR

RIFLE = "graphics/weapons/primary/ak47/ak47.xbg"
# The rifle's wood, a Weapon material with condition-driven colours.
WOOD = "GRAPHICS\\_MATERIALS\\JPCORMIER-M-2007100950589039.xbm"


def fail(message):
    print("FAIL %s" % message)
    return 1


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0)
    layer = bpy.context.view_layer.layer_collection.children[result["collection"].name]
    bpy.context.view_layer.active_layer_collection = layer
    return result


def material(result, game_path):
    return next(m for obj in result["parts"] for m in obj.data.materials
                if m and m.get(material_data.PROP_MATERIAL_PATH) == game_path)


def owning(path, game_path, directory):
    """A copy of the pack in which the model owns one of its materials, as an xref index would say."""
    copy = Pack.load(path)
    copy.entry(game_path).record["role"] = "owned"
    out = os.path.join(directory, "owned.fc2model")
    copy.save(out)
    return out


def untouched_goes_back_unwritten(path, directory):
    result = load(path)
    source = Pack.load(path)
    errors = 0
    for held in material_data.used(result["collection"]):
        game_path = held[material_data.PROP_MATERIAL_PATH]
        if not material_data.same(material_data.document(held), source.material(game_path)):
            errors += fail("%s does not hold the document it was imported from" % game_path)

    out = os.path.join(directory, "untouched.fc2model")
    written = export_xbg.save(out, result["collection"])
    if written["materials"]:
        errors += fail("an untouched export wrote %d material(s)" % written["materials"])
    if any(entry.modified for entry in Pack.load(out).entries):
        errors += fail("an untouched export marked an entry modified")
    if not errors:
        print("untouched: every material holds its document and none is written")
    return errors


def edit_reaches_the_game_file(path, directory):
    result = load(owning(path, WOOD, directory))
    wood = material(result, WOOD)
    values = {value.key: value for value in wood.fc2.values}
    broken = tuple(values["DiffuseColor1Broken"].number)

    values["SpecularPower"].number[0] = 150.0
    values["DiffuseColor1"].colour = (0.5, 0.25, 0.125)

    errors = 0
    if tuple(values["DiffuseColor1Clean"].number[:3]) != tuple(values["DiffuseColor1"].number[:3]):
        errors += fail("setting DiffuseColor1 left its Clean twin behind")
    if tuple(values["DiffuseColor1Broken"].number) != broken:
        errors += fail("setting DiffuseColor1 moved its Broken twin, the worn look")
    tint = [tuple(node.inputs["Color2"].default_value)[:3] for node in wood.node_tree.nodes
            if node.type == "MIX_RGB"]
    if (0.5, 0.25, 0.125) not in tint:
        errors += fail("the preview did not take the new DiffuseColor1: %s" % tint)

    out = os.path.join(directory, "edited.fc2model")
    if export_xbg.save(out, result["collection"])["materials"] != 1:
        return errors + fail("the edit did not write exactly one material")
    if not Pack.load(out).entry(WOOD).modified:
        errors += fail("the edited material was not marked modified")

    layer = os.path.join(directory, "layer")
    subprocess.run([CLI, "fc2model", "extract", out, "-o", layer], capture_output=True, check=True)
    xbm = next(os.path.join(base, name) for base, _dirs, names in os.walk(layer)
               for name in names if name.lower().endswith(".xbm"))
    shown = json.loads(subprocess.run([CLI, "xbm", "show", xbm, "--json"],
                                      capture_output=True, text=True, check=True).stdout)
    written = {entry["key"]: entry["value"] for entry in shown["properties"]}
    if written.get("SpecularPower") != "150":
        errors += fail("the game file says SpecularPower %s" % written.get("SpecularPower"))
    if written.get("DiffuseColor1Clean") != "0.5, 0.25, 0.125":
        errors += fail("the game file says DiffuseColor1Clean %s" % written.get("DiffuseColor1Clean"))
    if not errors:
        print("edit: SpecularPower and DiffuseColor1 with its Clean twin reach the .xbm")
    return errors


def shared_edit_is_refused(path, directory):
    result = load(path)
    wood = material(result, WOOD)
    next(v for v in wood.fc2.values if v.key == "SpecularPower").number[0] = 150.0

    errors = 0
    found = validate.check_scene(bpy.context)
    if not any(f.code == "entry.shared-edited" and f.severity == ERROR for f in found):
        errors += fail("Check did not block an edit to a shared material: %s"
                       % [f.code for f in found])
    try:
        export_xbg.save(os.path.join(directory, "shared.fc2model"), result["collection"])
        errors += fail("export wrote an edit to a shared material")
    except ValueError:
        pass
    if not errors:
        print("shared: Check blocks and export refuses an edit to a shared material")
    return errors


def panel_operators(path, directory):
    """Add and remove a property through the operators the panel's buttons run."""
    import addon

    result = load(owning(path, WOOD, directory))
    wood = material(result, WOOD)
    addon.register()
    errors = 0
    try:
        with bpy.context.temp_override(material=wood, object=result["parts"][0]):
            if bpy.ops.material.fc2_add_value(key="ReflectionPower") != {"FINISHED"}:
                return fail("adding ReflectionPower did not finish")
            added = material_data.find(wood.fc2, "ReflectionPower")
            if added is None or added.width != 1:
                errors += fail("ReflectionPower was not added as one float")
            index = list(wood.fc2.values).index(added)
            if bpy.ops.material.fc2_remove_value(index=index) != {"FINISHED"}:
                errors += fail("removing ReflectionPower did not finish")
            if material_data.find(wood.fc2, "ReflectionPower") is not None:
                errors += fail("ReflectionPower is still there after removing it")
    finally:
        addon.unregister()
    if not errors:
        print("panel: a property is added at the shader's default and removed")
    return errors


def main():
    if not require_pack():
        return 0
    path = pack(RIFLE)
    if not Pack.load(path).manifest.get("shaders"):
        return fail("the pack carries no shader list; rebuild JackAll")
    directory = tempfile.mkdtemp(prefix="fc2material-")
    errors = untouched_goes_back_unwritten(path, directory)
    errors += edit_reaches_the_game_file(path, directory)
    errors += shared_edit_is_refused(path, directory)
    errors += panel_operators(path, directory)
    print("blender material: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
