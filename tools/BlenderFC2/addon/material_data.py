# A material's document, held on the Blender material that previews it.
#
# The values are the truth and the node graph is a picture of them: an edit
# rebuilds the graph, and export writes the values back. Nothing here reads a
# Dunia format - the document is the pack's JSON, its entries kept in file order
# so an untouched material goes back exactly as it came.

import json
import posixpath

import bpy
from bpy.props import (BoolProperty, CollectionProperty, EnumProperty, FloatVectorProperty,
                       IntProperty, PointerProperty, StringProperty)

from . import convert, materials

TEXTURE = "Texture"
FLOAT = "Float"
INTEGER = "Integer"

# Each section and the list of the document that holds it, in file order.
SECTIONS = ((TEXTURE, "textures"), (FLOAT, "floats"), (INTEGER, "integers"))

CLEAN = "Clean"
BROKEN = "Broken"

# The material's game path, which is its identity in the pack. Kept apart from
# the material's own internal name - one property used to carry both, so
# resolving textures overwrote the path with the name.
PROP_MATERIAL_PATH = "fc2_material_path"

# The document as the pack held it, for the fields nobody edits.
PROP_DOCUMENT = "fc2_document"

# A material at a path the pack does not carry yet, which export adds.
PROP_ADDED = "fc2_added"

# The model's game path, whose folder a new texture lands in.
PROP_MODEL = "fc2_model"

# Set while values are written in bulk, so the preview is rebuilt once at the end.
_quiet = False


def _changed(self, _context):
    if _quiet:
        return
    material = self.id_data
    _link(material, self)
    refresh(material)


def _picked(self, context):
    """An image picked for a texture slot brings its game path: the pack's, or a new one."""
    image = self.image
    path = (image.get(materials.PROP_IMAGE_PATH) or _new_path(image, self.id_data.get(PROP_MODEL, ""))
            if image else None)
    if path and path != self.path:
        self.path = path
    else:
        _changed(self, context)


def _new_path(image, model):
    """Where an image from outside the pack will live in the game: beside the model, named after
    the image. Stamped on it, so every slot that picks it names the same file."""
    name = posixpath.splitext(image.name)[0].strip().lower().replace(" ", "_")
    folder = posixpath.dirname(model.replace("\\", "/"))
    image[materials.PROP_IMAGE_PATH] = posixpath.join(folder, name + ".xbt")
    return image[materials.PROP_IMAGE_PATH]


def _colour(self):
    return self.number[:3]


def _set_colour(self, value):
    self.number = (value[0], value[1], value[2], self.number[3])


class FC2_PG_value(bpy.types.PropertyGroup):
    """One entry of a material: a texture slot, a group of floats, or an integer."""
    key: StringProperty()
    section: EnumProperty(items=[(name, name, "") for name, _field in SECTIONS])
    width: IntProperty(min=0, max=4)
    path: StringProperty(name="Texture", update=_changed)
    image: PointerProperty(type=bpy.types.Image, update=_picked)
    number: FloatVectorProperty(size=4, update=_changed)
    colour: FloatVectorProperty(size=3, subtype="COLOR", min=0.0, soft_max=1.0,
                                get=_colour, set=_set_colour)
    integer: IntProperty(min=0, update=_changed)


class FC2_PG_material(bpy.types.PropertyGroup):
    name: StringProperty(name="Name", description="The material's own name inside its file")
    shader: StringProperty(name="Shader")
    values: CollectionProperty(type=FC2_PG_value)
    link_clean: BoolProperty(
        name="Set Clean twins along", default=True,
        description="A weapon's or vehicle's condition overwrites a colour from its Clean and "
                    "Broken twins, so setting the colour sets its Clean twin too. Broken stays "
                    "the worn look")
    show_all: BoolProperty(name="Show every property the shader takes", default=False)


def held(material):
    return material is not None and PROP_DOCUMENT in material


def load(material, document, image_for, model=""):
    """Hold a document on the material of `model`, its textures resolved by `image_for`, and
    preview it."""
    global _quiet
    # A script can import a pack without registering the add-on.
    register()
    _quiet = True
    try:
        fc2 = material.fc2
        fc2.values.clear()
        fc2.name = document.get("name", "")
        fc2.shader = document.get("shader", "")
        for section, field in SECTIONS:
            for entry in document.get(field, ()):
                add(fc2, section, entry.get("slot", entry.get("key")), entry, image_for)
        material[PROP_DOCUMENT] = json.dumps(document)
        material[PROP_MODEL] = model
    finally:
        _quiet = False
    refresh(material)


def add(fc2, section, key, entry, image_for=None):
    """Append one value, from a document entry: `path`, or `value` as a list or a number."""
    value = fc2.values.add()
    value.key = key
    value.section = section
    if section == TEXTURE:
        value.path = entry["path"]
        value.image = image_for(entry["path"]) if image_for and entry["path"] else None
    elif section == FLOAT:
        numbers = list(entry["value"])
        value.width = len(numbers)
        value.number = (numbers + [0.0] * 4)[:4]
    else:
        value.integer = entry["value"]
    return value


def document(material):
    """The document export writes: the one the pack held, with the values as they now are."""
    out = json.loads(material[PROP_DOCUMENT])
    fc2 = material.fc2
    out["name"] = fc2.name
    out["shader"] = fc2.shader
    out["textures"] = [{"slot": v.key, "path": v.path} for v in fc2.values if v.section == TEXTURE]
    out["floats"] = [{"key": v.key, "value": list(v.number[:v.width])}
                     for v in fc2.values if v.section == FLOAT]
    out["integers"] = [{"key": v.key, "value": v.integer}
                       for v in fc2.values if v.section == INTEGER]
    return out


def same(a, b):
    """Whether two documents say the same thing, floats compared as the file stores them."""
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, float) or isinstance(b, float):
        return isinstance(a, (int, float)) and isinstance(b, (int, float)) \
            and convert.f32(a) == convert.f32(b)
    return a == b


def find(fc2, key):
    return next((value for value in fc2.values if value.key == key), None)


def clean_twin(fc2, key):
    """The twin a weapon's or vehicle's condition overwrites this colour from, if it carries both."""
    if key.endswith(CLEAN) or key.endswith(BROKEN) or find(fc2, key + BROKEN) is None:
        return None
    return find(fc2, key + CLEAN)


def _link(material, value):
    global _quiet
    fc2 = material.fc2
    twin = clean_twin(fc2, value.key) if value.section == FLOAT and fc2.link_clean else None
    if twin is None:
        return
    _quiet = True
    try:
        twin.number = value.number
    finally:
        _quiet = False


def refresh(material):
    """Rebuild the preview from the values, with the images they already point at."""
    images = {v.path: v.image for v in material.fc2.values if v.section == TEXTURE and v.image}
    materials.build(material, document(material), lambda path: images.get(path) or _loaded(path))


def _loaded(path):
    """An image already in the file for a game path, such as one another material loaded."""
    return next((image for image in bpy.data.images
                 if image.get(materials.PROP_IMAGE_PATH) == path), None)


def used(collection):
    """Every held material on the collection's meshes, once each."""
    seen = {}
    for obj in collection.objects:
        if obj.type != "MESH":
            continue
        for material in obj.data.materials:
            if held(material):
                seen.setdefault(material.name, material)
    return list(seen.values())


def write(pack, collection):
    """Put every material whose values changed back into the pack, and every new one in, and
    say how many."""
    written = 0
    for material in used(collection):
        path = material.get(PROP_MATERIAL_PATH, "")
        original = pack.material(path)
        edited = document(material)
        if original is None:
            if material.get(PROP_ADDED):
                pack.add_material(path, edited)
                written += 1
        elif not same(edited, original):
            pack.replace_document(path, edited)
            written += 1
    return written


def copy(material, name, path):
    """A new material at `path`, holding the values `material` holds now."""
    new = material.copy()
    new.name = name
    new[PROP_MATERIAL_PATH] = path
    new[PROP_ADDED] = True
    new.fc2.name = name
    return new


CLASSES = (FC2_PG_value, FC2_PG_material)


def register():
    """Idempotent, since an import may have registered these already."""
    if hasattr(bpy.types.Material, "fc2"):
        return
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Material.fc2 = PointerProperty(type=FC2_PG_material)


def unregister():
    if not hasattr(bpy.types.Material, "fc2"):
        return
    del bpy.types.Material.fc2
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
