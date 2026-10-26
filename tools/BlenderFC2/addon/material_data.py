# A material's document, held on the Blender material that previews it.
#
# The values are the truth and the node graph is a picture of them: an edit
# rebuilds the graph, and export writes the values back. Nothing here reads a
# Dunia format - the document is the pack's JSON, its entries kept in file order
# so an untouched material goes back exactly as it came.

import contextlib
import json
import posixpath
import re

import bpy
from bpy.props import (BoolProperty, CollectionProperty, EnumProperty, FloatVectorProperty,
                       IntProperty, PointerProperty, StringProperty)

from . import convert, materials
from .pack import path_key, same_path

TEXTURE = "Texture"
FLOAT = "Float"
INTEGER = "Integer"

# Each section and the list of the document that holds it, in file order.
SECTIONS = ((TEXTURE, "textures"), (FLOAT, "floats"), (INTEGER, "integers"))

CLEAN = "Clean"
BROKEN = "Broken"

# The material's game path, its identity in the pack.
PROP_MATERIAL_PATH = "fc2_material_path"

# The document's fields nobody edits, as the pack held them.
PROP_DOCUMENT = "fc2_document"

# A material at a path the pack does not carry yet, which export adds.
PROP_ADDED = "fc2_added"

# The model's game path, whose folder a new texture or material lands in.
PROP_MODEL = "fc2_model"

# An image's file extension, which a game path built from its name leaves out.
_IMAGE_EXTENSION = re.compile(r"\.(png|jpe?g|tga|tiff?|bmp|exr|dds)$", re.IGNORECASE)

# Set while values are written in bulk, so update callbacks leave the scene alone.
_quiet = False


@contextlib.contextmanager
def _quietly():
    global _quiet
    _quiet = True
    try:
        yield
    finally:
        _quiet = False


def _changed(self, _context):
    if _quiet:
        return
    material = self.id_data
    _link(material, self)
    if self.section == TEXTURE or self.key in materials.PREVIEWED:
        refresh(material)


def _typed(self, context):
    """A typed game path takes the image the file already holds for it, or none."""
    if _quiet:
        return
    found = _loaded(self.path)
    if found is not self.image:
        with _quietly():
            self.image = found
    _changed(self, context)


def _picked(self, context):
    """A picked image brings its game path: the pack's, or a new one beside the model."""
    if _quiet:
        return
    image = self.image
    path = (image.get(materials.PROP_IMAGE_PATH) or _new_path(image, self.id_data)) if image else ""
    if image and not same_path(path, self.path):
        with _quietly():
            self.path = path
    _changed(self, context)


def _new_path(image, material):
    """Where an image from outside the pack will live, stamped on it so every slot that picks it
    names one file - and no other image's."""
    path = beside_model(material, _IMAGE_EXTENSION.sub("", image.name), ".xbt")
    base, count = path[:-len(".xbt")], 1
    while any(other is not image and same_path(other.get(materials.PROP_IMAGE_PATH), path)
              for other in bpy.data.images):
        count += 1
        path = "%s_%d.xbt" % (base, count)
    image[materials.PROP_IMAGE_PATH] = path
    return path


def beside_model(material, name, extension):
    """A game path in the model's own folder, named after `name`."""
    folder = posixpath.dirname(material.get(PROP_MODEL, "").replace("\\", "/"))
    return posixpath.join(folder, re.sub(r"[^a-z0-9_]+", "_", name.strip().lower()) + extension)


def _colour(self):
    return self.number[:3]


def _set_colour(self, value):
    self.number = (value[0], value[1], value[2], self.number[3])


class FC2_PG_value(bpy.types.PropertyGroup):
    """One entry of a material: a texture slot, a group of floats, or an integer."""
    key: StringProperty()
    section: EnumProperty(items=[(name, name, "") for name, _field in SECTIONS])
    width: IntProperty(min=0, max=4)
    path: StringProperty(name="Texture", update=_typed)
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


def is_new(material):
    """A held material the pack does not carry yet."""
    return held(material) and bool(material.get(PROP_ADDED))


def load(material, document, image_for, model):
    """Hold a document on the material of `model`, its textures resolved by `image_for`, and
    preview it."""
    with _quietly():
        fc2 = material.fc2
        fc2.values.clear()
        fc2.name = document.get("name", "")
        fc2.shader = document.get("shader", "")
        for section, field in SECTIONS:
            for entry in document.get(field, ()):
                add(fc2, section, entry.get("slot", entry.get("key")), entry, image_for)
        material[PROP_DOCUMENT] = json.dumps(
            {key: value for key, value in document.items()
             if key not in ("name", "shader") + tuple(field for _section, field in SECTIONS)})
        material[PROP_MODEL] = model
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
    """The document export writes: the fields nobody edits, and the values as they now are."""
    fc2 = material.fc2
    return {
        **json.loads(material[PROP_DOCUMENT]),
        "name": fc2.name,
        "shader": fc2.shader,
        "textures": [{"slot": v.key, "path": v.path} for v in fc2.values if v.section == TEXTURE],
        "floats": [{"key": v.key, "value": list(v.number[:v.width])}
                   for v in fc2.values if v.section == FLOAT],
        "integers": [{"key": v.key, "value": v.integer} for v in fc2.values if v.section == INTEGER],
    }


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
    fc2 = material.fc2
    twin = clean_twin(fc2, value.key) if value.section == FLOAT and fc2.link_clean else None
    if twin is not None:
        with _quietly():
            twin.number = value.number


def refresh(material):
    """Rebuild the preview from the values, with the images they already point at."""
    images = {path_key(v.path): v.image for v in material.fc2.values
              if v.section == TEXTURE and v.image}
    materials.build(material, document(material),
                    lambda path: images.get(path_key(path)) or _loaded(path))


def _loaded(path):
    """An image already in the file for a game path, such as one another material loaded."""
    return next((image for image in bpy.data.images
                 if path and same_path(image.get(materials.PROP_IMAGE_PATH), path)), None)


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
            if is_new(material):
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
    """Idempotent, since an import or export run as a script registers these itself."""
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
