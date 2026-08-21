# Typed access to an .xbg vertex buffer.
#
# The container keeps each LOD's vertex block as bytes; this decodes it into
# per-component arrays and packs them back. Components are stored at file
# precision and converted on demand, so unpack followed by pack reproduces the
# original bytes exactly.

import struct

from .xbg import (BINORMAL, BONE_WTS1, BONE_WTS2, COLOR, NORMAL, POS_FLOAT, POS_HALF,
                  POS_INT16, TANGENT, UNK400, UV0, UV1, UV2, vertex_layout)

# D3DCOLOR bytes are unsigned-normalised and stored BGRA, so a direction
# component is byte / 255 * 2 - 1 read back as z, y, x.
_UNSIGNED_TO_SIGNED = [b / 255.0 * 2.0 - 1.0 for b in range(256)]

# Per component: the struct format for one vertex and the flag that enables it.
_FORMATS = {
    "pos_float": ("<3f", POS_FLOAT),
    "pos_int16": ("<4h", POS_INT16),
    "pos_half": ("<4H", POS_HALF),
    "uv0": ("<2h", UV0),
    "uv1": ("<2h", UV1),
    "uv2": ("<2h", UV2),
    "bone_wts1": ("<8B", BONE_WTS1),
    "bone_wts2": ("<8B", BONE_WTS2),
    "normal": ("<4B", NORMAL),
    "color": ("<4B", COLOR),
    "tangent": ("<4B", TANGENT),
    "binormal": ("<4B", BINORMAL),
    "unk400": ("<4B", UNK400),
}

# An unused UV channel is written as this sentinel rather than zeroed.
UV_UNUSED = -32768


def _position_key(flags):
    if flags & POS_FLOAT:
        return "pos_float"
    if flags & POS_HALF:
        return "pos_half"
    return "pos_int16"


class VertexStream:
    """One vertex buffer, decoded to raw per-component arrays.

    Values stay at file precision. `positions()`, `uvs()` and `normals()`
    convert; `pack()` rebuilds the bytes from the raw arrays.
    """

    def __init__(self, flags, count, components):
        self.flags = flags
        self.count = count
        self.components = components

    def __len__(self):
        return self.count

    @classmethod
    def unpack(cls, data, buffer, count):
        offsets, stride = vertex_layout(buffer.flags)
        if stride != buffer.stride:
            raise ValueError("flags %#x imply stride %d, file says %d"
                             % (buffer.flags, stride, buffer.stride))
        position = _position_key(buffer.flags)
        components = {}
        for name, offset in offsets.items():
            key = position if name == "pos" else name
            reader = struct.Struct(_FORMATS[key][0])
            base = buffer.offset + offset
            components[name] = [reader.unpack_from(data, base + i * stride)
                                for i in range(count)]
        return cls(buffer.flags, count, components)

    def slice(self, start, count):
        """A stream over `count` of this buffer's vertices, from `start`."""
        return VertexStream(self.flags, count,
                            {name: values[start:start + count]
                             for name, values in self.components.items()})

    def pack(self):
        offsets, stride = vertex_layout(self.flags)
        position = _position_key(self.flags)
        out = bytearray(self.count * stride)
        for name, offset in offsets.items():
            key = position if name == "pos" else name
            writer = struct.Struct(_FORMATS[key][0])
            for i, value in enumerate(self.components[name]):
                writer.pack_into(out, i * stride + offset, *value)
        return bytes(out)

    def positions(self, scale):
        """Model-space positions. int16 storage is scaled by the PMCP factor."""
        raw = self.components["pos"]
        if self.flags & POS_FLOAT:
            return [(v[0], v[1], v[2]) for v in raw]
        if self.flags & POS_HALF:
            raise NotImplementedError("half-float positions; no shipped file uses them")
        return [(v[0] * scale, v[1] * scale, v[2] * scale) for v in raw]

    def uvs(self, translate, scale, channel=0):
        """UVs in Blender's bottom-up V, or None when the channel is unused."""
        name = "uv%d" % channel
        raw = self.components.get(name)
        if raw is None or all(v[0] == UV_UNUSED and v[1] == UV_UNUSED for v in raw):
            return None
        return [(translate + u * scale, 1.0 - (translate + v * scale)) for u, v in raw]

    def _directions(self, name):
        raw = self.components.get(name)
        if raw is None:
            return None
        return [(_UNSIGNED_TO_SIGNED[v[2]], _UNSIGNED_TO_SIGNED[v[1]],
                 _UNSIGNED_TO_SIGNED[v[0]]) for v in raw]

    def normals(self):
        return self._directions("normal")

    def tangents(self):
        return self._directions("tangent")

    def colors(self):
        """Vertex colour as RGBA floats; the file stores BGRA."""
        raw = self.components.get("color")
        if raw is None:
            return None
        return [(v[2] / 255.0, v[1] / 255.0, v[0] / 255.0, v[3] / 255.0) for v in raw]

    def skin(self):
        """(weight, palette slot) pairs per vertex, zero-weight entries dropped."""
        first = self.components.get("bone_wts1")
        if first is None:
            return None
        second = self.components.get("bone_wts2")
        out = []
        for i in range(self.count):
            pairs = [(w / 255.0, b) for w, b in zip(first[i][:4], first[i][4:]) if w]
            if second is not None:
                pairs += [(w / 255.0, b) for w, b in zip(second[i][:4], second[i][4:]) if w]
            out.append(pairs)
        return out


def buffer_vertex_count(lod, index):
    """Vertices in one buffer, derived from where the next buffer starts."""
    buffer = lod.vertex_buffers[index]
    following = [b.offset for b in lod.vertex_buffers if b.offset > buffer.offset]
    end = min(following) if following else len(lod.vertex_data)
    return (end - buffer.offset) // buffer.stride


def unpack_indices(lod):
    count = len(lod.index_data) // 2
    return list(struct.unpack_from("<%dH" % count, lod.index_data, 0))


def pack_indices(indices):
    return struct.pack("<%dH" % len(indices), *indices)
