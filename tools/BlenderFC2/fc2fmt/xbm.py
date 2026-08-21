# Reader for `.xbm`, the Dunia material.
#
# An `.xbm` is the same chunk container as an `.xbg`; everything that matters
# sits in its `LTMD` chunk, which an `.xbg` may also carry inline. Either way the
# body is a run of counted sections: texture maps first, then property groups of
# one, two, three and four floats, then a group of integers. The two differ only
# in what precedes that body.

from .assets import normalise
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
        self.part = ""
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
        """A standalone .xbm's LTMD, which opens with five bytes nothing reads."""
        self = cls()
        r = Reader(data, payload)
        r.skip(5)
        self.name = r.cstring()
        self.shader = r.cstring()
        self._read_body(r)
        if r.pos != end:
            raise ValueError("LTMD consumed %d of %d bytes" % (r.pos - payload, end - payload))
        return self

    @classmethod
    def parse_inline(cls, raw):
        """The LTMD an .xbg embeds, whose body is preceded by the name its
        geometry references and the part that name belongs to."""
        self = cls()
        r = Reader(raw, 0)
        self.name = r.cstring()
        self.part = r.cstring()
        self.shader = r.cstring()
        self._read_body(r)
        if r.pos != len(raw):
            raise ValueError("inline LTMD consumed %d of %d bytes" % (r.pos, len(raw)))
        return self

    def _read_body(self, r):
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

    def albedo(self):
        """The diffuse map, under whichever slot name this shader uses."""
        return next((self.textures[s] for s in ALBEDO_SLOTS if s in self.textures), None)

    def tiling(self, slot, default=(1.0, 1.0)):
        return tuple(self.floats.get(slot, default))


def inline_materials(model):
    """The materials an .xbg embeds, keyed by the name its geometry references."""
    return {m.name.lower(): m
            for m in (XbmMaterial.parse_inline(c.raw)
                      for c in model.chunks if c.tag == TAG_LTMD and c.raw)}


def resolve(path, model, source):
    """A material's definition: the one the model embeds, or the .xbm it names."""
    definition = inline_materials(model).get(normalise(path))
    if definition is not None:
        return definition
    data = source.read(path) if source is not None else None
    return XbmMaterial.parse(data) if data else None


def _chunks(data):
    pos = 32
    for _ in range(Reader(data, 28).u32()):
        _word0, size, payload_size, _sub = Reader(data, pos + 4).u32s(4)
        if size < CHUNK_HEADER:
            return
        yield pos, size, pos + size - payload_size
        pos += size
