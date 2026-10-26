# Textures back into the pack: an image painted or swapped in Blender, and a new one in a slot.
#
# A texture travels as PNG and JackAll encodes it. What this decides is which PNG, at which game
# path and in which codec; the blocks, the mip chain and the header are JackAll's.

import hashlib
import math
import os
import tempfile

import bpy
import numpy

from . import material_data

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

# The two samplerCUBE slots in the game's descriptors. A flat image cannot become a cube map.
CUBE_SLOTS = ("ReflectionTexture", "ReflectionCubeTexture")


def plan(pack, collection):
    """Every texture slot whose image export would write: (slot, image, game path, entry or None).

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
            out.setdefault(value.path.lower(), (value.key, value.image, value.path, entry))
    return list(out.values())


def changes(pack, collection):
    """What export would write: (slot, image, game path, entry or None, PNG) for every new
    texture and every one whose image no longer matches the pack's."""
    out = []
    for slot, image, path, entry in plan(pack, collection):
        png = encode(image, slot if entry is None else None)
        if entry is None or hashlib.sha256(png).hexdigest() != entry.record["sha256"]:
            out.append((slot, image, path, entry, png))
    return out


def write(pack, collection):
    """Put every changed and every new texture into the pack, and say how many."""
    changed = changes(pack, collection)
    for slot, image, path, entry, png in changed:
        if entry is None:
            pack.add_texture(path, png, codec(image, slot), levels(png))
            continue
        resized = size(png) != size(pack.content(entry))
        pack.replace(path, png)
        if resized:
            entry.record["levels"] = levels(png)
    return len(changed)


def codec(image, slot):
    """DXT5 where alpha carries something - coverage, or a normal's X - else DXT1."""
    if slot.startswith("NormalTexture"):
        return "DXT5"
    return "DXT5" if _soft_alpha(image) else "DXT1"


def levels(png):
    """The whole mip chain, down to one texel."""
    return int(math.log2(max(size(png)))) + 1


def size(png):
    """Width and height, out of the PNG's header."""
    return int.from_bytes(png[16:20], "big"), int.from_bytes(png[20:24], "big")


def encode(image, new_slot=None):
    """The image as PNG: the file it was loaded from unless it was painted since.

    A new normal map is stored the way the game reads one, X in alpha and Y in the colour,
    which is the swizzle the importer undoes.
    """
    normal = new_slot is not None and new_slot.startswith("NormalTexture")
    if not image.is_dirty and not normal and image.packed_file is None and image.filepath:
        path = bpy.path.abspath(image.filepath)
        if os.path.exists(path):
            with open(path, "rb") as handle:
                data = handle.read()
            if data.startswith(PNG_SIGNATURE):
                return data

    width, height = image.size
    pixels = numpy.empty(width * height * image.channels, dtype=numpy.float32)
    image.pixels.foreach_get(pixels)
    rgba = numpy.ones((width * height, 4), dtype=numpy.float32)
    rgba[:, :image.channels] = pixels.reshape(-1, image.channels)
    if normal:
        rgba[:, 3] = rgba[:, 0]
        rgba[:, 0] = rgba[:, 2] = rgba[:, 1]

    copy = bpy.data.images.new("fc2-export", width, height, alpha=True)
    try:
        copy.colorspace_settings.name = "Non-Color"
        copy.pixels.foreach_set(rgba.ravel())
        out = os.path.join(tempfile.gettempdir(), "fc2tex", "export.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        copy.filepath_raw = out
        copy.file_format = "PNG"
        copy.save()
        with open(out, "rb") as handle:
            return handle.read()
    finally:
        bpy.data.images.remove(copy)


def _soft_alpha(image, samples=4096):
    """Whether any sampled texel is less than opaque."""
    if image.channels < 4:
        return False
    pixels = numpy.empty(len(image.pixels), dtype=numpy.float32)
    image.pixels.foreach_get(pixels)
    alpha = pixels[3::4]
    return bool((alpha[::max(1, len(alpha) // samples)] < 0.999).any())
