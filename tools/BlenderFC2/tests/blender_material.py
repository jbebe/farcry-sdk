# Edit a material in Blender and require the game file JackAll writes from it.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_material.py
#
# The values held on a Blender material are what export writes, so three things
# are held here: an untouched material goes back unwritten, an edited one comes
# out of JackAll as the .xbm it describes, and a material the model shares is
# refused rather than quietly re-skinning every other model that uses it.

import contextlib
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of the add-on an installed extension left.
from _corpus import CLI, extract, find, pack, require_pack

import bpy
import numpy

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


@contextlib.contextmanager
def registered():
    """The add-on registered, for the operators a panel's buttons run."""
    import addon

    addon.register()
    try:
        yield
    finally:
        addon.unregister()


def shown(xbm):
    """A written material's own name and its properties as `xbm show` prints them."""
    out = json.loads(subprocess.run([CLI, "xbm", "show", xbm, "--json"],
                                    capture_output=True, text=True, check=True).stdout)
    return out["name"], {entry["key"]: entry["value"] for entry in out["properties"]}


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

    _name, written = shown(next(find(".xbm", extract(out, os.path.join(directory, "layer")))))
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
    material_data.find(wood.fc2, "SpecularPower").number[0] = 150.0

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


def copy_reaches_the_game_file(path, directory):
    """A shared material copied to one of the model's own, edited, and drawn by the parts it drew."""
    result = load(path)
    wood = material(result, WOOD)
    users = [obj for obj in result["parts"] if obj.data.materials[0] is wood]
    added = "graphics/weapons/primary/ak47/ak47_body.xbm"
    with registered():
        return _copy(result, wood, users, added, directory)


def _copy(result, wood, users, added, directory):
    with bpy.context.temp_override(material=wood, object=users[0], selected_objects=users):
        if bpy.ops.material.fc2_copy(name="AK47_BODY", path=added) != {"FINISHED"}:
            return fail("copying the wood did not finish")

    copy = users[0].data.materials[0]
    errors = 0
    if copy is wood or any(obj.data.materials[0] is not copy for obj in users):
        errors += fail("the parts the wood drew do not all draw with the copy")
    material_data.find(copy.fc2, "SpecularPower").number[0] = 150.0

    out = os.path.join(directory, "copied.fc2model")
    written = export_xbg.save(out, result["collection"])
    if written["reassigned"] != len(users) or written["materials"] != 1:
        errors += fail("export repointed %d of %d parts and wrote %d material(s)"
                       % (written["reassigned"], len(users), written["materials"]))
    exported = Pack.load(out)
    entry = exported.entry(added)
    if entry is None or entry.record.get("origin_sha256") != "" or not entry.owned:
        errors += fail("the copy is not an owned entry with an empty origin: %s"
                       % (entry.record if entry else None))
    if exported.entry(WOOD).modified:
        errors += fail("copying the wood modified the wood")
    mesh = exported.mesh()
    index = next(i for i, name in enumerate(mesh["materials"]) if name == added)
    drawing = {part["name"] for part in mesh["parts"]
               for cluster in part["clusters"] if cluster["material_index"] == index}
    # The scene holds LOD0 only, and the part has to change material at every distance.
    if drawing != {"FRAME_LOD%d" % lod for lod in range(len(mesh["lods"]))}:
        errors += fail("the copy draws %s, not FRAME at every LOD" % sorted(drawing))

    xbm = os.path.join(extract(out, os.path.join(directory, "copied")), "mods", *added.split("/"))
    if not os.path.exists(xbm):
        return errors + fail("JackAll wrote no %s" % added)
    name, properties = shown(xbm)
    if name != "AK47_BODY" or properties.get("SpecularPower") != "150":
        errors += fail("the game file is %s with SpecularPower %s"
                       % (name, properties.get("SpecularPower")))
    if not errors:
        print("copy: the wood copied, edited and drawn by %s reaches the game files"
              % ", ".join(sorted(drawing)))
    return errors


def textures_reach_the_game_files(path, directory):
    """A texture of the model's own painted and halved, and a new image in a slot of an owned material."""
    result = load(owning(path, WOOD, directory))
    wood = material(result, WOOD)
    values = {value.key: value for value in wood.fc2.values}

    mask = values["MaskTexture1"].image
    width = mask.size[0]
    mask.scale(width // 2, mask.size[1] // 2)
    painted = numpy.ones(len(mask.pixels), dtype=numpy.float32)
    mask.pixels.foreach_set(painted)
    mask.update()

    albedo = bpy.data.images.new("New Albedo", 256, 256)
    values["DiffuseTexture1"].image = albedo
    added = "graphics/weapons/primary/ak47/new_albedo.xbt"

    errors = 0
    if values["DiffuseTexture1"].path != added:
        errors += fail("a new image was given %s, not %s" % (values["DiffuseTexture1"].path, added))

    out = os.path.join(directory, "textures.fc2model")
    written = export_xbg.save(out, result["collection"])
    if written["textures"] != 2:
        errors += fail("export wrote %d textures, not the mask and the new albedo" % written["textures"])
    exported = Pack.load(out)
    new = exported.entry(added)
    if new is None or new.record.get("origin_sha256") != "" or new.record.get("codec") != "DXT1" \
            or new.record.get("levels") != 9:
        errors += fail("the new albedo's entry is %s" % (new.record if new else None))
    old = exported.entry(values["MaskTexture1"].path)
    if not old.modified or old.record["levels"] != Pack.load(path).entry(old.path).record["levels"] - 1:
        errors += fail("the halved mask is %s" % old.record)

    layer = extract(out, os.path.join(directory, "textures"))
    written_files = {os.path.basename(name).lower() for name in find(".xbt", layer)}
    for name in ("new_albedo.xbt", os.path.basename(old.path.replace("\\", "/")).lower()):
        if name not in written_files:
            errors += fail("JackAll wrote no %s: %s" % (name, sorted(written_files)))
    if not errors:
        print("textures: a halved, painted mask and a new albedo reach the game files")
    return errors


def cube_map_is_refused(path, directory):
    result = load(owning(path, WOOD, directory))
    wood = material(result, WOOD)
    reflection = material_data.add(wood.fc2, material_data.TEXTURE, "ReflectionTexture", {"path": ""})
    reflection.image = bpy.data.images.new("Flat", 64, 64)
    found = validate.check_scene(bpy.context)
    if not any(f.code == "texture.not-writable" and f.severity == ERROR for f in found):
        return fail("Check let a flat image into a cube-map slot: %s" % [f.code for f in found])
    print("cube map: a flat image in ReflectionTexture blocks the export")
    return 0


def panel_operators(path, directory):
    """Add and remove a property through the operators the panel's buttons run."""
    result = load(owning(path, WOOD, directory))
    wood = material(result, WOOD)
    errors = 0
    with registered(), bpy.context.temp_override(material=wood, object=result["parts"][0]):
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
    errors += copy_reaches_the_game_file(path, directory)
    errors += textures_reach_the_game_files(path, directory)
    errors += cube_map_is_refused(path, directory)
    errors += panel_operators(path, directory)
    print("blender material: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
