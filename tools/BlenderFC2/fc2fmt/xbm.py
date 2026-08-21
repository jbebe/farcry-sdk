# Reader for `.xbm`, the Dunia material.
#
# An `.xbm` is the same chunk container as an `.xbg`; everything that matters
# sits in its `LTMD` chunk, which an `.xbg` may also carry inline. The payload
# is a run of counted sections: texture maps first, then property groups of one,
# two, three and four floats, then a group of integers.

from .binary import Reader
from .xbg import CHUNK_HEADER, MAGIC, TAG_LTMD

# Slots the Generic shader samples, in the order it blends them.
DIFFUSE1 = "DiffuseTexture1"
DIFFUSE2 = "DiffuseTexture2"
MASK1 = "MaskTexture1"
SPECULAR1 = "SpecularTexture1"

# A character's skin and a cloth material name their albedo differently.
ALBEDO_SLOTS = (DIFFUSE1, "SkinTexture", "FabricTexture")

# Property group sizes, in the order the sections appear.
GROUP_SIZES = (1, 2, 3, 4)


class XbmMaterial:
    def __init__(self):
        self.name = ""
        self.shader = ""
        self.textures = {}
        self.floats = {}
        self.integers = {}
        self.trailing = 0

    @classmethod
    def parse(cls, data):
        if data[:4] != MAGIC:
            raise ValueError("not an .xbm file")
        for start, size, payload in _chunks(data):
            if data[start:start + 4].decode("latin-1") == TAG_LTMD:
                return cls.parse_ltmd(data, payload, start + size)
        raise ValueError("no LTMD chunk")

    @classmethod
    def parse_ltmd(cls, data, payload, end):
        self = cls()
        r = Reader(data, payload)
        # Five bytes precede the name in every shipped material.
        r.skip(5)
        self.name = r.cstring()
        self.shader = r.cstring()
        # Each field is read into a local first: in `d[a()] = b()` Python
        # evaluates b() before a(), which would read these fields out of order.
        for _ in range(r.u32()):
            path = r.cstring()
            self.textures[r.cstring()] = path
        for width in GROUP_SIZES:
            for _ in range(r.u32()):
                key = r.cstring()
                self.floats[key] = r.f32s(width)
        for _ in range(r.u32()):
            key = r.cstring()
            self.integers[key] = r.u32()
        self.trailing = r.u32()
        if r.pos != end:
            raise ValueError("LTMD consumed %d of %d bytes" % (r.pos - payload, end - payload))
        return self

    def albedo(self):
        """The diffuse map, under whichever slot name this shader uses."""
        return next((self.textures[s] for s in ALBEDO_SLOTS if s in self.textures), None)

    def tiling(self, slot, default=(1.0, 1.0)):
        return tuple(self.floats.get(slot, default))


def _chunks(data):
    pos = 32
    for _ in range(Reader(data, 28).u32()):
        _word0, size, payload_size, _sub = Reader(data, pos + 4).u32s(4)
        if size < CHUNK_HEADER:
            return
        yield pos, size, pos + size - payload_size
        pos += size
