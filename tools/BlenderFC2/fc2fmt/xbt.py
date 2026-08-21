# Reader for `.xbt`, the Dunia texture.
#
# An `.xbt` is a small header followed by a complete, valid `.dds`; the header
# gives the payload offset directly. Header layout is documented in
# docs/docs/file-formats/xbt.md.
#
# Half of all textures split their top mip into a sibling `<name>_mip0.xbt`
# holding a single level at twice the size, so opening only the named file
# yields a texture that is correct but half resolution on each axis.

import os

from .binary import Reader

MAGIC = b"TBX\x00"
COMPANION_SUFFIX = "_mip0.xbt"


class XbtTexture:
    def __init__(self, version, flags, payload, companion=""):
        self.version = version
        self.flags = flags
        self.payload = payload
        self.companion = companion

    @classmethod
    def parse(cls, data):
        if data[:4] != MAGIC:
            raise ValueError("not an .xbt file")
        r = Reader(data, 4)
        version = r.u32()
        header_size = r.u32()
        flags = r.u32()
        if version >= 11:
            r.skip(12)
        name = data[r.pos:header_size].split(b"\x00")[0].decode("latin-1")
        return cls(version, flags, data[header_size:], name)

    @property
    def dds(self):
        return self.payload


def read(path):
    """The texture at `path`, preferring its full-resolution companion."""
    companion = path[:-4] + COMPANION_SUFFIX if not path.endswith(COMPANION_SUFFIX) else None
    if companion and os.path.exists(companion):
        path = companion
    return XbtTexture.parse(open(path, "rb").read())
