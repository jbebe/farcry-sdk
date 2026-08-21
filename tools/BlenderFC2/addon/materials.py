# Build a Blender material from an .xbm, following the engine's Generic shader.
#
# A Dunia surface is rarely one texture. The albedo is two tiling detail maps
# blended by the green and blue channels of a per-model mask, each tinted by its
# own colour, over a base tint. That is why a weapon's own .xbt looks like a
# greyscale smear: it is the mask, not the colour.

import os
import tempfile

import bpy

from fc2fmt import xbt
from fc2fmt.xbm import DIFFUSE1, DIFFUSE2, MASK1, XbmMaterial

from .resolve import game_files

# Node graph spacing, purely cosmetic.
COLUMN = 260
ROW = 300


def _image(path, files, cache):
    """Load an .xbt as a Blender image by handing Blender its DDS payload."""
    if path in cache:
        return cache[path]
    resolved = files.find(path)
    image = None
    if resolved:
        try:
            texture = xbt.read(resolved)
            name = os.path.basename(resolved)[:-4] + ".dds"
            temporary = os.path.join(tempfile.gettempdir(), "fc2tex", name)
            os.makedirs(os.path.dirname(temporary), exist_ok=True)
            with open(temporary, "wb") as handle:
                handle.write(texture.dds)
            image = bpy.data.images.load(temporary, check_existing=True)
            image.name = os.path.basename(path)
        except Exception:
            image = None
    cache[path] = image
    return image


def _texture_node(tree, image, uv_scale, location, colorspace="sRGB"):
    node = tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    node.location = location
    node.interpolation = "Smart"
    if colorspace != "sRGB":
        image.colorspace_settings.name = colorspace
    if uv_scale and uv_scale != (1.0, 1.0):
        mapping = tree.nodes.new("ShaderNodeMapping")
        mapping.location = (location[0] - COLUMN, location[1])
        mapping.inputs["Scale"].default_value = (uv_scale[0], uv_scale[1], 1.0)
        coords = tree.nodes.new("ShaderNodeTexCoord")
        coords.location = (location[0] - 2 * COLUMN, location[1])
        tree.links.new(mapping.inputs["Vector"], coords.outputs["UV"])
        tree.links.new(node.inputs["Vector"], mapping.outputs["Vector"])
    return node


def _rgba(colour, fallback=(1.0, 1.0, 1.0)):
    values = tuple(colour) if colour else fallback
    return (values[0], values[1], values[2], 1.0)


def _multiply(tree, source, colour, location):
    """Multiply a layer by its DiffuseColor, which is how the engine tints it."""
    if colour is None or tuple(colour) == (1.0, 1.0, 1.0):
        return source
    node = tree.nodes.new("ShaderNodeMixRGB")
    node.blend_type = "MULTIPLY"
    node.location = location
    node.inputs["Fac"].default_value = 1.0
    tree.links.new(node.inputs["Color1"], source)
    node.inputs["Color2"].default_value = _rgba(colour)
    return node.outputs["Color"]


def _base_tint(tree, definition, weight, location):
    """lerp(DiffuseColorBase, DiffuseColor1, mask.b) — the engine's layer-1 tint.

    Applying DiffuseColor1 flat instead leaves everything washed out, because
    DiffuseColorBase is what darkens the unworn areas.
    """
    base = definition.floats.get("DiffuseColorBase")
    layer = definition.floats.get("DiffuseColor1")
    if base is None and layer is None:
        return None
    node = tree.nodes.new("ShaderNodeMixRGB")
    node.location = location
    node.inputs["Color1"].default_value = _rgba(base)
    node.inputs["Color2"].default_value = _rgba(layer)
    if weight is None:
        node.inputs["Fac"].default_value = 1.0
    else:
        tree.links.new(node.inputs["Fac"], weight)
    return node.outputs["Color"]


def build(material, xbm_path, files, cache):
    """Wire an existing Blender material to the textures its .xbm names."""
    data = files.find(xbm_path)
    if not data:
        return False
    definition = XbmMaterial.parse(open(data, "rb").read())

    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    output.location = (3 * COLUMN, 0)
    principled = tree.nodes.new("ShaderNodeBsdfPrincipled")
    principled.location = (2 * COLUMN, 0)
    tree.links.new(output.inputs["Surface"], principled.outputs["BSDF"])

    albedo = definition.albedo()
    first = _image(albedo, files, cache) if albedo else None
    if first is None:
        return False

    # The mask supplies both weights: green blends in layer 2, blue chooses how
    # far layer 1's tint moves from DiffuseColorBase towards DiffuseColor1.
    mask_path = definition.textures.get(MASK1)
    mask = _image(mask_path, files, cache) if mask_path else None
    layer_weight = tint_weight = None
    if mask is not None:
        mask_node = _texture_node(tree, mask, definition.tiling("MaskTiling1"),
                                  (0, -2 * ROW), colorspace="Non-Color")
        split = tree.nodes.new("ShaderNodeSeparateColor")
        split.location = (COLUMN, -2 * ROW)
        tree.links.new(split.inputs["Color"], mask_node.outputs["Color"])
        layer_weight = split.outputs["Green"]
        tint_weight = split.outputs["Blue"]

    base = _texture_node(tree, first, definition.tiling("DiffuseTiling1"), (0, 0))
    tint = _base_tint(tree, definition, tint_weight, (COLUMN, ROW))
    colour = base.outputs["Color"]
    if tint is not None:
        node = tree.nodes.new("ShaderNodeMixRGB")
        node.blend_type = "MULTIPLY"
        node.location = (1.4 * COLUMN, ROW)
        node.inputs["Fac"].default_value = 1.0
        tree.links.new(node.inputs["Color1"], colour)
        tree.links.new(node.inputs["Color2"], tint)
        colour = node.outputs["Color"]

    second_path = definition.textures.get(DIFFUSE2)
    second = _image(second_path, files, cache) if second_path else None
    if second is not None and layer_weight is not None:
        second_node = _texture_node(tree, second, definition.tiling("DiffuseTiling2"), (0, -ROW))
        second_colour = _multiply(tree, second_node.outputs["Color"],
                                  definition.floats.get("DiffuseColor2"), (COLUMN, -ROW))
        blend = tree.nodes.new("ShaderNodeMixRGB")
        blend.location = (1.7 * COLUMN, 0)
        tree.links.new(blend.inputs["Fac"], layer_weight)
        tree.links.new(blend.inputs["Color1"], colour)
        tree.links.new(blend.inputs["Color2"], second_colour)
        colour = blend.outputs["Color"]

    tree.links.new(principled.inputs["Base Color"], colour)
    principled.inputs["Roughness"].default_value = 0.6
    if definition.integers.get("AlphaTestEnabled"):
        material.blend_method = "CLIP"
    material["fc2_shader"] = definition.shader
    material["fc2_material"] = definition.name
    return True
