# Binary primitives shared by every Dunia format reader and writer.
#
# Dunia is little-endian on PC. Names are identified by CRC32 of the exact-case
# string, which is what CStringID::SetContent computes and what the engine
# matches bones on.

import struct
import zlib


def name_hash(name):
    """CRC32 of the exact-case name, the engine's CStringID key."""
    return zlib.crc32(name.encode("ascii")) & 0xFFFFFFFF


class Reader:
    def __init__(self, data, pos=0):
        self.data = data
        self.pos = pos

    def __len__(self):
        return len(self.data)

    def seek(self, pos):
        self.pos = pos
        return self

    def skip(self, count):
        self.pos += count
        return self

    def align(self, boundary):
        self.pos = (self.pos + boundary - 1) & ~(boundary - 1)
        return self

    def _unpack(self, fmt, size):
        value = struct.unpack_from(fmt, self.data, self.pos)
        self.pos += size
        return value

    def u8(self):
        return self._unpack("<B", 1)[0]

    def u16(self):
        return self._unpack("<H", 2)[0]

    def u32(self):
        return self._unpack("<I", 4)[0]

    def i16(self):
        return self._unpack("<h", 2)[0]

    def i32(self):
        return self._unpack("<i", 4)[0]

    def f32(self):
        return self._unpack("<f", 4)[0]

    def u16s(self, count):
        return list(self._unpack("<%dH" % count, count * 2))

    def i16s(self, count):
        return list(self._unpack("<%dh" % count, count * 2))

    def u32s(self, count):
        return list(self._unpack("<%dI" % count, count * 4))

    def f32s(self, count):
        return list(self._unpack("<%df" % count, count * 4))

    def vec3(self):
        return self.f32s(3)

    def quat(self):
        """Local rotation, stored xyzw."""
        return self.f32s(4)

    def raw(self, count):
        chunk = self.data[self.pos:self.pos + count]
        self.pos += count
        return chunk

    def string_id(self):
        """A CStringID: CRC32, length, then unterminated characters."""
        hashed = self.u32()
        return hashed, self.raw(self.u32()).decode("latin-1")

    def cstring(self):
        """A length-prefixed, NUL-terminated name, as the .xbg chunks store it."""
        name = self.raw(self.u32()).decode("latin-1")
        self.skip(1)
        return name


class Writer:
    def __init__(self):
        self.buf = bytearray()

    def __len__(self):
        return len(self.buf)

    def align(self, boundary):
        """Pad with a descending byte counter, the filler the exporter emits."""
        padding = -len(self.buf) % boundary
        self.buf += bytes(range(padding, 0, -1))
        return self

    def _pack(self, fmt, *values):
        self.buf += struct.pack(fmt, *values)
        return self

    def u8(self, value):
        return self._pack("<B", value)

    def u16(self, value):
        return self._pack("<H", value)

    def u32(self, value):
        return self._pack("<I", value)

    def i16(self, value):
        return self._pack("<h", value)

    def i32(self, value):
        return self._pack("<i", value)

    def f32(self, value):
        return self._pack("<f", value)

    def u16s(self, values):
        return self._pack("<%dH" % len(values), *values)

    def i16s(self, values):
        return self._pack("<%dh" % len(values), *values)

    def u32s(self, values):
        return self._pack("<%dI" % len(values), *values)

    def f32s(self, values):
        return self._pack("<%df" % len(values), *values)

    vec3 = f32s
    quat = f32s

    def raw(self, data):
        self.buf += data
        return self

    def string_id(self, name, hashed=None):
        encoded = name.encode("latin-1")
        self.u32(name_hash(name) if hashed is None else hashed)
        self.u32(len(encoded))
        return self.raw(encoded)

    def cstring(self, name):
        encoded = name.encode("latin-1")
        self.u32(len(encoded))
        return self.raw(encoded).u8(0)

    def patch_u32(self, offset, value):
        struct.pack_into("<I", self.buf, offset, value)
        return self

    def bytes(self):
        return bytes(self.buf)
