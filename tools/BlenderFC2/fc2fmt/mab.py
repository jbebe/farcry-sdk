# Reader and writer for `.mab`, the Dunia animation bank.
#
# Layout is documented in docs/docs/file-formats/mab.md. Everything outside the
# fields below is preserved verbatim, so a parsed file writes back unchanged.

import struct

from .binary import Reader, Writer

HEADER_SIZE = 16
VERSION_FC2 = 0x4C
BODY_TAG = b"AnD\x1a"

# Body offsets, all relative to file + HEADER_SIZE.
OFF_OPAQUE = 0x28
OFF_TAG = 0x70
OFF_SECTIONS = 0x78

MASK_WORDS = 5
SECTION_COUNT = 10
SECTION_DATA = OFF_SECTIONS + SECTION_COUNT * 4

# Section table slots the engine dereferences by name.
SECTION_CONSTANT = 2
SECTION_KEYFRAMES = 3
SECTION_EVENTS = 7

# Smallest-three quaternion codec. Three components are stored in 16 bits each
# over the range +/- 1/sqrt(2); the omitted one is recovered from the norm.
QUAT_SCALE = 4.315969e-05
QUAT_BIAS = 0.70710677
QUAT_BYTES = 6

# Keyframes are grouped in eights, one presence byte per track per group.
GROUP_SHIFT = 3
GROUP_FRAMES = 1 << GROUP_SHIFT

# Where the three stored components and the recovered one land in xyzw, keyed by
# the sign bits of the first two words as (first << 1) | second.
ENGINE_LAYOUT = ((3, 0, 1, 2), (0, 1, 3, 2), (0, 3, 1, 2), (0, 1, 2, 3))

_PACKED_QUAT = struct.Struct("<HHh")
_U16 = struct.Struct("<H")
_KEYFRAME_HEADER = struct.Struct("<HHHH")


def unpack_quaternion(first, second, third, layout=ENGINE_LAYOUT):
    """(u16, u16, i16) -> xyzw, or None when the words do not form a rotation."""
    a = float(first & 0x7FFF) * QUAT_SCALE - QUAT_BIAS
    b = float(second & 0x7FFF) * QUAT_SCALE - QUAT_BIAS
    c = float(third) * QUAT_SCALE - QUAT_BIAS
    remainder = 1.0 - a * a - b * b - c * c
    if remainder < 0.0:
        return None
    values = (a, b, c, remainder ** 0.5)
    return tuple(values[i] for i in layout[(first >> 14 & 2) | (second >> 15 & 1)])


def read_quaternion(data, offset, layout=ENGINE_LAYOUT):
    if offset + QUAT_BYTES > len(data):
        return None
    return unpack_quaternion(*_PACKED_QUAT.unpack_from(data, offset), layout=layout)


def mask_bones(mask):
    """Skeleton bone ids named by a five-word bitmask, in ascending order."""
    return [word * 32 + bit
            for word, value in enumerate(mask)
            for bit in range(32) if value >> bit & 1]


def mask_slot(mask, bone_id):
    """A bone's index within its section: the popcount of the mask below it."""
    word, bit = divmod(bone_id, 32)
    if not mask[word] >> bit & 1:
        return None
    below = sum(bin(value).count("1") for value in mask[:word])
    return below + bin(mask[word] & ((1 << bit) - 1)).count("1")


class MabFile:
    def __init__(self):
        self.header = b""
        self.constant_mask = [0] * MASK_WORDS
        self.keyframe_mask = [0] * MASK_WORDS
        self.opaque = b""
        self.duration = 0.0
        self.sections = [0] * SECTION_COUNT
        self.body_tail = b""

    @property
    def version(self):
        return _U16.unpack_from(self.header, 0)[0]

    @classmethod
    def parse(cls, data):
        if len(data) < HEADER_SIZE + SECTION_DATA:
            raise ValueError("file too small to be a .mab")
        self = cls()
        self.header = data[:HEADER_SIZE]
        if self.version != VERSION_FC2:
            raise ValueError("unsupported .mab version %#x" % self.version)
        body = Reader(data, HEADER_SIZE)
        self.constant_mask = body.u32s(MASK_WORDS)
        self.keyframe_mask = body.u32s(MASK_WORDS)
        self.opaque = body.raw(OFF_TAG - OFF_OPAQUE)
        if body.raw(4) != BODY_TAG:
            raise ValueError("missing AnD body tag")
        self.duration = body.f32()
        self.sections = [body.i32() for _ in range(SECTION_COUNT)]
        self.body_tail = data[HEADER_SIZE + SECTION_DATA:]
        return self

    def write(self):
        w = Writer()
        w.raw(self.header).u32s(self.constant_mask).u32s(self.keyframe_mask)
        w.raw(self.opaque).raw(BODY_TAG).f32(self.duration)
        for offset in self.sections:
            w.i32(offset)
        return w.raw(self.body_tail).bytes()

    def section(self, index):
        """Bytes of one section, or None when the slot is unused."""
        offset = self.sections[index]
        if offset <= 0:
            return None
        end = min((s for s in self.sections if s > offset),
                  default=SECTION_DATA + len(self.body_tail))
        return self.body_tail[offset - SECTION_DATA:end - SECTION_DATA]

    def constant_bones(self):
        return mask_bones(self.constant_mask)

    def keyframed_bones(self):
        return mask_bones(self.keyframe_mask)

    def constant_rotations(self, layout=ENGINE_LAYOUT):
        """bone id -> the single rotation held for the whole clip."""
        block = self.section(SECTION_CONSTANT)
        if not block:
            return {}
        count = _U16.unpack_from(block, 0)[0]
        rotations = {}
        # mask_bones yields ids ascending, so a bone's slot is its ordinal.
        for slot, bone_id in enumerate(self.constant_bones()):
            if slot >= count:
                break
            quat = read_quaternion(block, 8 + slot * QUAT_BYTES, layout)
            if quat is not None:
                rotations[bone_id] = quat
        return rotations

    def keyframe_header(self):
        """(track count, last frame, frames per second) for the keyframe block."""
        block = self.section(SECTION_KEYFRAMES)
        if not block or len(block) < _KEYFRAME_HEADER.size:
            return None
        tracks, last_frame, rate, _spare = _KEYFRAME_HEADER.unpack_from(block, 0)
        return tracks, last_frame, rate

    def keyframe_tracks(self, layout=ENGINE_LAYOUT):
        """bone id -> [(frame, quaternion)], read out of the sparse groups.

        Frames are grouped in eights. Each group stores, per track in bone-id
        order, the rotation at its first frame; then a presence byte per track,
        padded to an even count; then the rotations for the subframes those
        bytes name, again in track order. Bit i of a presence byte means a key
        at subframe i + 1, and bit 7 is the group's first frame, which is always
        present and stored up front.
        """
        block = self.section(SECTION_KEYFRAMES)
        bones = self.keyframed_bones()
        header = self.keyframe_header()
        if not block or not bones or not header:
            return {}
        tracks, last_frame, _rate = header
        if tracks != len(bones):
            raise ValueError("%d tracks for %d bones in the keyframe mask"
                             % (tracks, len(bones)))
        groups = (last_frame >> GROUP_SHIFT) + 1
        offsets = struct.unpack_from("<%di" % groups, block, _KEYFRAME_HEADER.size)

        out = {bone: [] for bone in bones}
        for group, start in enumerate(offsets):
            presence = start + tracks * QUAT_BYTES
            cursor = presence + ((tracks + 1) & ~1)
            first = group << GROUP_SHIFT
            for slot, bone in enumerate(bones):
                out[bone].append(
                    (first, read_quaternion(block, start + slot * QUAT_BYTES, layout)))
                byte = block[presence + slot]
                for bit in range(GROUP_FRAMES - 1):
                    if byte >> bit & 1:
                        out[bone].append(
                            (first + bit + 1, read_quaternion(block, cursor, layout)))
                        cursor += QUAT_BYTES
        return out

    def events(self):
        return self.section(SECTION_EVENTS)
