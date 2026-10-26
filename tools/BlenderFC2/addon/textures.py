# Textures back into the pack: an image painted or swapped in Blender, and a new one in a slot.
#
# A texture travels as PNG and JackAll encodes it. What this decides is which PNG, at which game
# path and in which codec; the blocks, the mip chain and the header are JackAll's.

import math
import os
import tempfile

import bpy
import numpy

from . import material_data
from .pack import path_key

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

# The two samplerCUBE slots in the game's descriptors. A flat image cannot become a cube map.
CUBE_SLOTS = ("ReflectionTexture", "ReflectionCubeTexture")


def plan(pack, collection):
    """Every texture slot holding an image, once per game path: (slot, image, path, entry or None).

    Raises for what no export can write, so a check can say so before an export refuses.
    """
    out = {}
    for material in material_data.used(collection):
        for value in material.fc2.values:
            if value.section != material_data.TEXTURE or value.image is None or not value.path:
                continue
            entry = pack.entry(value.path)
            if entry is None and value.key in CUBE_SLOTS:
                raise ValueError("%s on %s is a cube map, and %s is a flat image; pick a cube map "
                                 "the game has" % (value.key, material.name, value.image.name))
            out.setdefault(path_key(value.path), (value.key, value.image, value.path, entry))
    return list(out.values())


def changed(pack, slot, image, entry):
    """The PNG to write for this slot's image, or None when the pack already holds it."""
    png = encode(image, swizzle=entry is None and _normal(slot))
    return png if entry is None or png != pack.content(entry) else None


def write(pack, collection):
    """Put every changed and every new texture into the pack, and say how many."""
    written = 0
    for slot, image, path, entry in plan(pack, collection):
        png = changed(pack, slot, image, entry)
        if png is None:
            continue
        if entry is None:
            pack.add_texture(path, png, codec(image, slot), levels(png))
        else:
            resized = size(png) != size(pack.content(entry))
            pack.replace(path, png)
            if resized:
                entry.record["levels"] = levels(png)
        written += 1
    return written


def codec(image, slot):
    """DXT5 where alpha carries something - coverage, or a normal's X - else DXT1."""
    if _normal(slot):
        return "DXT5"
    found = alpha(image)
    return "DXT5" if found is not None and (found < 0.999).any() else "DXT1"


def _normal(slot):
    """NormalTexture1 or NormalTexture2."""
    return slot.startswith("NormalTexture")


def levels(png):
    """The whole mip chain, down to one texel."""
    return int(math.log2(max(size(png)))) + 1


def size(png):
    """Width and height, out of the PNG's header."""
    return int.from_bytes(png[16:20], "big"), int.from_bytes(png[20:24], "big")


def alpha(image):
    """Every texel's alpha, in one read; None for an image with none or no pixels loaded."""
    if image.channels < 4:
        return None
    try:
        pixels = numpy.empty(len(image.pixels), dtype=numpy.float32)
        image.pixels.foreach_get(pixels)
    except (RuntimeError, AttributeError):
        return None
    return pixels[3::4]


def encode(image, swizzle=False):
    """The image as PNG: the file it was loaded from unless it was painted since.

    `swizzle` stores a new normal map the way the game reads one, X in alpha and Y in the colour.
    """
    if not image.is_dirty and not swizzle and image.packed_file is None and image.filepath:
        path = bpy.path.abspath(image.filepath)
        if os.path.exists(path):
            with open(path, "rb") as handle:
                data = handle.read()
            if data.startswith(PNG_SIGNATURE):
                return data

    width, height = image.size
    pixels = numpy.empty(width * height * image.channels, dtype=numpy.float32)
    image.pixels.foreach_get(pixels)
    if image.channels == 4:
        rgba = pixels.reshape(-1, 4)
    else:
        rgba = numpy.ones((width * height, 4), dtype=numpy.float32)
        rgba[:, :image.channels] = pixels.reshape(-1, image.channels)
    if swizzle:
        rgba[:, 3] = rgba[:, 0]
        rgba[:, 0] = rgba[:, 2] = rgba[:, 1]

    copy = bpy.data.images.new("fc2-export", width, height, alpha=True)
    try:
        copy.colorspace_settings.name = "Non-Color"
        copy.pixels.foreach_set(rgba.ravel())
        out = os.path.join(tempfile.gettempdir(), "fc2tex-export", "encoded.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        copy.filepath_raw = out
        copy.file_format = "PNG"
        copy.save()
        with open(out, "rb") as handle:
            return handle.read()
    finally:
        bpy.data.images.remove(copy)
