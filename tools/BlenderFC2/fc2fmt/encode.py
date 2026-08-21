# Pack float-space vertex arrays back into a VertexStream.
#
# The inverse of the accessors on VertexStream: positions, UVs, normals,
# colours and skin weights go in as an editor holds them and come out at file
# precision. A template stream can be passed to inherit anything not supplied,
# which is how an untouched part comes back byte for byte; without one the
# constants every shipped vertex carries are used instead.

from .vertex import VertexStream
from .xbg import (BINORMAL, BONE_WTS1, BONE_WTS2, COLOR, NORMAL, POS_FLOAT, POS_HALF,
                  TANGENT, UV0, UV1, vertex_layout)

# int16 positions, so a coordinate is this many scale steps at most.
_POSITION_LIMIT = 32767

# Weights are stored in four slots per set, two sets at most.
SLOTS_PER_SET = 4

# Slots that carry the same value in all 14,319,419 shipped vertices.
POSITION_W = 1
DIRECTION_W = 128

# What a vertex holds before anything is written into it.
DEFAULTS = {
    "pos": (0, 0, 0, POSITION_W),
    "uv0": (0, 0),
    "uv1": (0, 0),
    "uv2": (0, 0),
    "normal": (DIRECTION_W, DIRECTION_W, 255, DIRECTION_W),
    "tangent": (255, DIRECTION_W, DIRECTION_W, DIRECTION_W),
    "binormal": (DIRECTION_W, 255, DIRECTION_W, DIRECTION_W),
    "color": (255, 255, 255, 255),
    "bone_wts1": (255, 0, 0, 0, 0, 0, 0, 0),
    "bone_wts2": (0, 0, 0, 0, 0, 0, 0, 0),
}


def to_byte(value, low=0.0, high=1.0):
    """Quantise to 0..255 across the given range, clamped."""
    span = high - low or 1.0
    return max(0, min(255, int(round((value - low) / span * 255.0))))


def _direction(vector, w):
    """A direction as the file stores it: unsigned BGRA, so z, y, x, then w."""
    return (to_byte(vector[2], -1.0, 1.0), to_byte(vector[1], -1.0, 1.0),
            to_byte(vector[0], -1.0, 1.0), w)


def _skin(pairs, sets):
    """Split (weight, slot) pairs across the one or two weight components."""
    padded = list(pairs) + [(0.0, 0)] * (SLOTS_PER_SET * sets - len(pairs))
    out = []
    for index in range(sets):
        chunk = padded[index * SLOTS_PER_SET:(index + 1) * SLOTS_PER_SET]
        out.append(tuple(to_byte(w) for w, _s in chunk) + tuple(s for _w, s in chunk))
    return out


class Layout:
    """The scales a stream's integer components are expressed in."""

    def __init__(self, pos_scale, uv_translate, uv_scale):
        self.pos_scale = pos_scale
        self.uv_translate = uv_translate
        self.uv_scale = uv_scale

    @classmethod
    def of(cls, model):
        return cls(model.pos_scale, model.uv_translate, model.uv_scale)


def encode(flags, count, layout, template=None, positions=None, uvs=None, uvs1=None,
           normals=None, tangents=None, binormals=None, colours=None, skin=None):
    """Build a VertexStream for `flags` from float-space arrays.

    Anything not supplied is taken from `template` when one is given and holds
    `count` vertices — that is how an untouched part comes back byte for byte —
    and from DEFAULTS otherwise.
    """
    if flags & (POS_FLOAT | POS_HALF):
        raise NotImplementedError("only int16 positions are written")
    if template is not None and len(template) != count:
        raise ValueError("template holds %d vertices, need %d" % (len(template), count))

    offsets, _stride = vertex_layout(flags)
    if template is not None:
        components = {name: list(values) for name, values in template.components.items()}
    else:
        components = {name: [DEFAULTS[name]] * count for name in offsets}

    if positions is not None:
        limit = _POSITION_LIMIT * layout.pos_scale
        for point in positions:
            if max(abs(c) for c in point) > limit:
                raise ValueError(
                    "a vertex at %.3f is past the %.3f this model's PMCP scale can "
                    "store; rescale before encoding" % (max(abs(c) for c in point), limit))
        w = components["pos"][0][3] if template is not None else POSITION_W
        components["pos"] = [(int(round(p[0] / layout.pos_scale)),
                              int(round(p[1] / layout.pos_scale)),
                              int(round(p[2] / layout.pos_scale)), w)
                             for p in positions]
    for name, flag, values in (("uv0", UV0, uvs), ("uv1", UV1, uvs1)):
        if values is None or not flags & flag:
            continue
        components[name] = [_uv(value, layout) for value in values]
    for name, flag, values in (("normal", NORMAL, normals), ("tangent", TANGENT, tangents),
                               ("binormal", BINORMAL, binormals)):
        if values is None or not flags & flag:
            continue
        components[name] = [_direction(v, DIRECTION_W) for v in values]
    if colours is not None and flags & COLOR:
        components["color"] = [(to_byte(c[2]), to_byte(c[1]), to_byte(c[0]), to_byte(c[3]))
                               for c in colours]
    if skin is not None and flags & BONE_WTS1:
        sets = 2 if flags & BONE_WTS2 else 1
        packed = [_skin(pairs, sets) for pairs in skin]
        components["bone_wts1"] = [p[0] for p in packed]
        if sets == 2:
            components["bone_wts2"] = [p[1] for p in packed]

    for name, values in components.items():
        if len(values) != count:
            raise ValueError("%s holds %d vertices, need %d" % (name, len(values), count))
    return VertexStream(flags, count, components)


def _uv(value, layout):
    """Undo the bottom-up V flip and the PMCU translate/scale."""
    return (int(round((value[0] - layout.uv_translate) / layout.uv_scale)),
            int(round(((1.0 - value[1]) - layout.uv_translate) / layout.uv_scale)))
