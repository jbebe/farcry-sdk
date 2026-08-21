# Reader for `.xbm`, the Dunia material.
#
# An `.xbm` is the same chunk container as an `.xbg`; everything that matters
# sits in its `LTMD` chunk, which an `.xbg` may also carry inline. Either way the
# body is a run of counted sections: texture maps first, then property groups of
# one, two, three and four floats, then a group of integers. The two differ only
# in what precedes that body.

from .assets import normalise
from .binary import Reader, Writer
from .xbg import TAG_LTMD, XbgFile

# Slots the Generic shader samples, in the order it blends them.
DIFFUSE1 = "DiffuseTexture1"
DIFFUSE2 = "DiffuseTexture2"
MASK1 = "MaskTexture1"
SPECULAR1 = "SpecularTexture1"

# A character's skin and a cloth material name their albedo differently.
ALBEDO_SLOTS = (DIFFUSE1, "SkinTexture", "FabricTexture")

# Property group sizes, in the order the sections appear.
GROUP_SIZES = (1, 2, 3, 4)

# Bytes a standalone LTMD opens with that nothing traced reads.
PREAMBLE = 5

# The three property sections, named as the attributes that hold them.
TEXTURES, FLOATS, INTEGERS = "textures", "floats", "integers"


class XbmMaterial:
    def __init__(self):
        self.name = ""
        self.part = ""
        self.shader = ""
        self.textures = {}
        self.floats = {}
        self.integers = {}
        # The same entries in file order. One retail material repeats a key
        # inside a section, which a dict cannot hold and a writer must.
        self.entries = []
        self.trailing = 0
        self.preamble = b"\x00" * PREAMBLE
        # The nine other chunks an .xbm carries, so an edit can be written back.
        self.container = None

    @classmethod
    def parse(cls, data):
        model = XbgFile.parse(data)
        self = cls.parse_ltmd(chunk_of(model).raw)
        self.container = model
        return self

    def write(self):
        """The whole .xbm back, carrying whatever was changed here."""
        if self.container is None:
            raise ValueError("%r came from an .xbg, which has no .xbm to write"
                             % self.name)
        chunk_of(self.container).raw = self.pack()
        return self.container.write()

    @classmethod
    def parse_ltmd(cls, raw):
        """A standalone .xbm's LTMD, which opens with five bytes nothing reads."""
        self = cls()
        r = Reader(raw, 0)
        self.preamble = r.raw(PREAMBLE)
        self.name = r.cstring()
        self.shader = r.cstring()
        self._read_body(r)
        if r.pos != len(raw):
            raise ValueError("LTMD consumed %d of %d bytes" % (r.pos, len(raw)))
        return self

    def pack(self):
        """The LTMD payload this material is stored as."""
        w = Writer().raw(self.preamble).cstring(self.name).cstring(self.shader)
        textures = self.section(TEXTURES)
        w.u32(len(textures))
        for slot, path in textures:
            w.cstring(path).cstring(slot)
        for width in GROUP_SIZES:
            group = self.section(FLOATS, width)
            w.u32(len(group))
            for key, values in group:
                w.cstring(key).f32s(values)
        integers = self.section(INTEGERS)
        w.u32(len(integers))
        for key, value in integers:
            w.cstring(key).u32(value)
        return w.u32(self.trailing).bytes()

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
            self._add(TEXTURES, r.cstring(), path)
        for width in GROUP_SIZES:
            for _ in range(r.u32()):
                key = r.cstring()
                self._add(FLOATS, key, r.f32s(width))
        for _ in range(r.u32()):
            key = r.cstring()
            self._add(INTEGERS, key, r.u32())
        self.trailing = r.u32()

    def _add(self, section, key, value):
        self.entries.append((section, key, value))
        getattr(self, section)[key] = value

    def set(self, section, key, value):
        """Change a property the material already carries."""
        table = getattr(self, section)
        if key not in table:
            raise KeyError("%r carries no %s named %r" % (self.name, section, key))
        table[key] = value
        self.entries = [(s, k, value if (s, k) == (section, key) else v)
                        for s, k, v in self.entries]

    def section(self, name, width=None):
        """One section's entries in file order, optionally one float width."""
        return [(key, value) for group, key, value in self.entries
                if group == name and (width is None or len(value) == width)]

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


def chunk_of(model):
    """The chunk an .xbm keeps its material in."""
    chunk = next((c for c in model.chunks if c.tag == TAG_LTMD), None)
    if chunk is None:
        raise ValueError("no LTMD chunk")
    return chunk


