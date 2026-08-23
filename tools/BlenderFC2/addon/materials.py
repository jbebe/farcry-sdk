# Build a Blender material from a pack's material document, following the
# Generic shader.
#
# A Dunia surface is rarely one texture. The albedo is two tiling detail maps
# blended by the green and blue channels of a per-model mask, each tinted by its
# own colour, over a base tint. That is why a weapon's own texture looks like a
# greyscale smear: it is the mask, not the colour.

import os
import tempfile

import bpy

from .pack import stem

# Slots the Generic shader samples, in the order it blends them.
DIFFUSE1 = "DiffuseTexture1"
DIFFUSE2 = "DiffuseTexture2"
MASK1 = "MaskTexture1"
SPECULAR1 = "SpecularTexture1"
NORMAL1 = "NormalTexture1"

# A character's skin and a cloth material name their albedo differently.
ALBEDO_SLOTS = (DIFFUSE1, "SkinTexture", "FabricTexture")

# The material's own internal name and shader, kept apart from the game path the
# importer stores - one property used to carry both, so resolving the textures
# overwrote the path with the name and lost it.
PROP_MATERIAL_NAME = "fc2_material_name"
PROP_SHADER = "fc2_shader"

# Which slot a node stands for, so an export can match an image back to the slot
# it came from and a rule can tell an edited chain from a rebuilt one.
PROP_SLOT = "fc2_slot"

# What an image was when the pack handed it over, so a rule can say it changed.
PROP_IMAGE_SIZE = "fc2_size"
PROP_IMAGE_PATH = "fc2_texture_path"

# Node graph spacing, purely cosmetic.
COLUMN = 260
ROW = 300


def textures(definition):
    """A material's texture slots as a plain dict, last spelling wins."""
    return {entry["slot"]: entry["path"] for entry in definition.get("textures", ())}


def floats(definition):
    return {entry["key"]: entry["value"] for entry in definition.get("floats", ())}


def integers(definition):
    return {entry["key"]: entry["value"] for entry in definition.get("integers", ())}


def albedo(definition):
    """The diffuse map, under whichever slot name this shader uses."""
    slots = textures(definition)
    return next((slots[name] for name in ALBEDO_SLOTS if name in slots), None)


def tiling(definition, slot, default=(1.0, 1.0)):
    return tuple(floats(definition).get(slot, default))


def _image(game_path, pack, cache):
    """Load a pack's texture as a Blender image by handing Blender its PNG."""
    if game_path in cache:
        return cache[game_path]

    image = None
    png = pack.texture(game_path) if pack else None
    if png:
        name = stem(game_path) + ".png"
        temporary = os.path.join(tempfile.gettempdir(), "fc2tex", name)
        os.makedirs(os.path.dirname(temporary), exist_ok=True)
        with open(temporary, "wb") as handle:
            handle.write(png)
        image = bpy.data.images.load(temporary, check_existing=True)
        # The same game path can carry different pixels in one pack than in
        # another, and check_existing hands back the datablock loaded first.
        image.reload()
        image.name = name
        # Stamped so a rule can tell a texture that was replaced or resized from
        # one that arrived this way. Blender only knows what it is holding now.
        image[PROP_IMAGE_SIZE] = list(image.size)
        image[PROP_IMAGE_PATH] = game_path
    cache[game_path] = image
    return image


def _texture_node(tree, image, slot, uv_scale, location, colorspace="sRGB"):
    node = tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    node.location = location
    node.interpolation = "Smart"
    node[PROP_SLOT] = slot
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


def _multiply(tree, layer, colour, location):
    """Multiply a layer by its DiffuseColor, which is how the engine tints it."""
    if colour is None or tuple(colour) == (1.0, 1.0, 1.0):
        return layer
    node = tree.nodes.new("ShaderNodeMixRGB")
    node.blend_type = "MULTIPLY"
    node.location = location
    node.inputs["Fac"].default_value = 1.0
    tree.links.new(node.inputs["Color1"], layer)
    node.inputs["Color2"].default_value = _rgba(colour)
    return node.outputs["Color"]


def _base_tint(tree, values, weight, location):
    """lerp(DiffuseColorBase, DiffuseColor1, mask.b) - the engine's layer-1 tint.

    Applying DiffuseColor1 flat instead leaves everything washed out, because
    DiffuseColorBase is what darkens the unworn areas.
    """
    base = values.get("DiffuseColorBase")
    layer = values.get("DiffuseColor1")
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


def _normal(tree, slots, pack, cache, definition, principled):
    """A normal map through a Normal Map node, which is the only correct route.

    1,656 of the 2,379 shipped materials carry one. Without it a modeler editing
    a normal map is working blind, and a warning about the channels the format
    does not carry is only honest if the ones it does carry are shown.
    """
    if NORMAL1 not in slots:
        return
    image = _image(slots[NORMAL1], pack, cache)
    if image is None:
        return

    node = _texture_node(tree, image, NORMAL1, tiling(definition, "NormalTiling1"),
                         (0, ROW), colorspace="Non-Color")
    normal_map = tree.nodes.new("ShaderNodeNormalMap")
    normal_map.location = (COLUMN, ROW)
    tree.links.new(normal_map.inputs["Color"], node.outputs["Color"])
    tree.links.new(principled.inputs["Normal"], normal_map.outputs["Normal"])


def _specular(tree, slots, pack, cache, definition, values, principled):
    """Specular as the engine has it: a per-texel map, a colour, and a power.

    1,889 shipped materials carry the map. Blender's Principled has no direct
    equivalent, so the map drives Roughness inverted - a bright specular texel is
    a smooth one - and SpecularPower sets the floor. That is an approximation,
    but it is the difference between seeing a specular edit and seeing nothing.
    """
    power = values.get("SpecularPower")
    if power:
        # Shipped powers run 1 to about 64. Higher is glossier, and Blender's
        # roughness runs the other way.
        principled.inputs["Roughness"].default_value = max(
            0.05, min(1.0, 1.0 - (float(power[0]) / 64.0)))

    if SPECULAR1 not in slots:
        return
    image = _image(slots[SPECULAR1], pack, cache)
    if image is None:
        return

    node = _texture_node(tree, image, SPECULAR1, tiling(definition, "SpecularTiling1"),
                         (0, 2 * ROW), colorspace="Non-Color")
    tint = _multiply(tree, node.outputs["Color"], values.get("SpecularColor1"),
                     (COLUMN, 2 * ROW))
    invert = tree.nodes.new("ShaderNodeInvert")
    invert.location = (1.4 * COLUMN, 2 * ROW)
    tree.links.new(invert.inputs["Color"], tint)
    tree.links.new(principled.inputs["Roughness"], invert.outputs["Color"])


def _vertex_colour(tree, weight, location):
    """Multiply a blend weight by the vertex colour's own channel.

    The pixel shader multiplies vertex colour into both mask weights, and 2,159
    shipped materials turn it on - so a mesh's painted wear is invisible without
    this, and a modeler editing it is the only instrument.
    """
    attribute = tree.nodes.new("ShaderNodeVertexColor")
    attribute.layer_name = "Colour"
    attribute.location = (location[0] - COLUMN, location[1])
    node = tree.nodes.new("ShaderNodeMixRGB")
    node.blend_type = "MULTIPLY"
    node.location = location
    node.inputs["Fac"].default_value = 1.0
    tree.links.new(node.inputs["Color1"], weight)
    tree.links.new(node.inputs["Color2"], attribute.outputs["Color"])
    return node.outputs["Color"]


def build(material, definition, pack, cache):
    """Wire an existing Blender material to the textures its document names."""
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    output.location = (3 * COLUMN, 0)
    principled = tree.nodes.new("ShaderNodeBsdfPrincipled")
    principled.location = (2 * COLUMN, 0)
    tree.links.new(output.inputs["Surface"], principled.outputs["BSDF"])

    material[PROP_SHADER] = definition.get("shader", "")
    material[PROP_MATERIAL_NAME] = definition.get("name", "")

    slots = textures(definition)
    values = floats(definition)
    first = _image(albedo(definition), pack, cache) if albedo(definition) else None
    if first is None:
        return False

    # The mask supplies both weights: green blends in layer 2, blue chooses how
    # far layer 1's tint moves from DiffuseColorBase towards DiffuseColor1.
    mask = _image(slots[MASK1], pack, cache) if MASK1 in slots else None
    layer_weight = tint_weight = None
    if mask is not None:
        mask_node = _texture_node(tree, mask, MASK1, tiling(definition, "MaskTiling1"),
                                  (0, -2 * ROW), colorspace="Non-Color")
        split = tree.nodes.new("ShaderNodeSeparateColor")
        split.location = (COLUMN, -2 * ROW)
        tree.links.new(split.inputs["Color"], mask_node.outputs["Color"])
        layer_weight = split.outputs["Green"]
        tint_weight = split.outputs["Blue"]
        # The pixel shader multiplies vertex colour into both weights, and 2,159
        # shipped materials turn it on, so a mesh's painted wear is invisible
        # without this.
        if integers(definition).get("VertexColorEnabled"):
            layer_weight = _vertex_colour(tree, layer_weight, (1.4 * COLUMN, -2 * ROW))
            tint_weight = _vertex_colour(tree, tint_weight, (1.4 * COLUMN, -3 * ROW))

    base = _texture_node(tree, first, DIFFUSE1, tiling(definition, "DiffuseTiling1"), (0, 0))
    tint = _base_tint(tree, values, tint_weight, (COLUMN, ROW))
    colour = base.outputs["Color"]
    if tint is not None:
        node = tree.nodes.new("ShaderNodeMixRGB")
        node.blend_type = "MULTIPLY"
        node.location = (1.4 * COLUMN, ROW)
        node.inputs["Fac"].default_value = 1.0
        tree.links.new(node.inputs["Color1"], colour)
        tree.links.new(node.inputs["Color2"], tint)
        colour = node.outputs["Color"]

    second = _image(slots[DIFFUSE2], pack, cache) if DIFFUSE2 in slots else None
    if second is not None and layer_weight is not None:
        second_node = _texture_node(tree, second, DIFFUSE2,
                                    tiling(definition, "DiffuseTiling2"), (0, -ROW))
        second_colour = _multiply(tree, second_node.outputs["Color"],
                                  values.get("DiffuseColor2"), (COLUMN, -ROW))
        blend = tree.nodes.new("ShaderNodeMixRGB")
        blend.location = (1.7 * COLUMN, 0)
        tree.links.new(blend.inputs["Fac"], layer_weight)
        tree.links.new(blend.inputs["Color1"], colour)
        tree.links.new(blend.inputs["Color2"], second_colour)
        colour = blend.outputs["Color"]

    tree.links.new(principled.inputs["Base Color"], colour)
    principled.inputs["Roughness"].default_value = 0.6
    _specular(tree, slots, pack, cache, definition, values, principled)
    _normal(tree, slots, pack, cache, definition, principled)
    if integers(definition).get("AlphaTestEnabled"):
        material.blend_method = "CLIP"
    return True
