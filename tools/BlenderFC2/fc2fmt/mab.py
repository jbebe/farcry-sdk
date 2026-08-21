# Reader and writer for `.mab`, the Dunia animation bank.
#
# Layout is documented in docs/docs/file-formats/mab.md. A bank holds one clip
# per participating skeleton, each chained from the one before. Everything
# outside the fields below is preserved verbatim, so a parsed file writes back
# unchanged.

import struct
from dataclasses import dataclass

from .binary import Reader, Writer

HEADER_SIZE = 16
VERSION_FC2 = 0x4C
BODY_TAG = b"AnD\x1a"

# Where the tag sits in a clip, and where the clip's fixed fields end.
OFF_TAG = 0x70
CLIP_HEADER = 0xA0

MASK_WORDS = 5
MASK_COUNT = 4
SECTION_COUNT = 9

# Which bones a mask names. Rotation and translation are masked independently.
MASK_CONSTANT_ROTATION = 0
MASK_KEYFRAME_ROTATION = 1
MASK_CONSTANT_TRANSLATION = 2
MASK_ANIMATED_TRANSLATION = 3

# Section table slots, in the order the engine dereferences them.
SECTION_ROOT_TRANSLATION = 0
SECTION_ROOT_ROTATION = 1
SECTION_CONSTANT_ROTATION = 2
SECTION_KEYFRAME_ROTATION = 3
SECTION_CONSTANT_TRANSLATION = 4
SECTION_ANIMATED_TRANSLATION = 5
SECTION_TAGS = 6
SECTION_EVENTS = 7
SECTION_NEXT_CLIP = 8

# Smallest-three quaternion codec. Three components are stored in 16 bits each
# over the range +/- 1/sqrt(2); the omitted one is recovered from the norm.
QUAT_SCALE = 4.315969e-05
QUAT_BIAS = 0.70710677
QUAT_BYTES = 6

VEC3_BYTES = 12

# The trajectory sections carry one unnamed track rather than a masked set.
ROOT_BONE = 0

# Every array section opens with the same eight bytes.
TRACK_HEADER = 8

# Sparse rotation keyframes are grouped in eights, one presence byte per track.
GROUP_SHIFT = 3
GROUP_FRAMES = 1 << GROUP_SHIFT

# The tag section: a u32 count, then one fixed-size record per participant.
TAG_COUNT_BYTES = 4
TAG_STRIDE = 0xAC
TAG_KIND = 0x00
TAG_CLIP = 0x0C
# Four name slots, each a CRC32 then 32 NUL-padded bytes. The third is empty in
# every shipped record.
TAG_NAMES = (0x18, 0x3C, 0x60, 0x84)
TAG_NAME_BYTES = 32

# Where the three stored components and the recovered one land in xyzw, keyed by
# the sign bits of the first two words as (first << 1) | second.
ENGINE_LAYOUT = ((3, 0, 1, 2), (0, 1, 3, 2), (0, 3, 1, 2), (0, 1, 2, 3))

_PACKED_QUAT = struct.Struct("<HHh")
_U16 = struct.Struct("<H")
_VEC3 = struct.Struct("<3f")
_TRACK_HEADER = struct.Struct("<HHHH")


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


def tag_name(block, at):
    """One name slot of a tag record, without its hash."""
    text = block[at + 4:at + 4 + TAG_NAME_BYTES].split(b"\0")[0]
    return text.decode("ascii", "replace")


def tag_hash(block, at):
    return struct.unpack_from("<I", block, at)[0]


@dataclass
class Participant:
    """Something a clip animates besides its own skeleton.

    `parent` is the bone on the owning clip's skeleton that the participant's
    rig hangs from, and its clip is expressed in that bone's frame.
    """

    kind: int
    name: str
    parent: str
    reference: str
    clip_offset: int

    @property
    def is_primary(self):
        """Whether this is the prop itself rather than a track on another one.

        A reload names its rifle once with no reference and again per magazine
        with one, so instantiating every record would fill the scene with
        duplicate rifles.
        """
        return not self.reference


class Clip:
    """One skeleton's animation within a bank."""

    def __init__(self):
        self.masks = [[0] * MASK_WORDS for _ in range(MASK_COUNT)]
        self.reference_rotation = (0.0, 0.0, 0.0, 1.0)
        self.loop_rotation = (0.0, 0.0, 0.0, 1.0)
        self.duration = 0.0
        self.sections = [0] * SECTION_COUNT
        self.data = b""

    @classmethod
    def parse(cls, body):
        if len(body) < CLIP_HEADER or body[OFF_TAG:OFF_TAG + 4] != BODY_TAG:
            raise ValueError("missing AnD clip tag")
        self = cls()
        r = Reader(body, 0)
        self.masks = [r.u32s(MASK_WORDS) for _ in range(MASK_COUNT)]
        self.reference_rotation = r.f32s(4)
        self.loop_rotation = r.f32s(4)
        # The tag was checked above; step over it to reach the duration.
        r.skip(4)
        self.duration = r.f32()
        self.sections = [r.i32() for _ in range(SECTION_COUNT)]
        if r.i32():
            raise ValueError("the slot the engine writes its own pointer to is set")
        self.data = body[CLIP_HEADER:]
        return self

    def write(self):
        w = Writer()
        for mask in self.masks:
            w.u32s(mask)
        w.f32s(self.reference_rotation).f32s(self.loop_rotation)
        w.raw(BODY_TAG).f32(self.duration)
        for offset in self.sections:
            w.i32(offset)
        return w.i32(0).raw(self.data).bytes()

    def section(self, index):
        """Bytes of one section, or None when the slot is unused."""
        offset = self.sections[index]
        if offset <= 0:
            return None
        end = min((s for s in self.sections if s > offset),
                  default=CLIP_HEADER + len(self.data))
        return self.data[offset - CLIP_HEADER:end - CLIP_HEADER]

    def track_header(self, index):
        """(track count, last frame, frames per second) for an array section."""
        block = self.section(index)
        if not block or len(block) < _TRACK_HEADER.size:
            return None
        count, last_frame, rate, _spare = _TRACK_HEADER.unpack_from(block, 0)
        return count, last_frame, rate

    def bone_ids(self):
        """Every skeleton bone id this clip addresses, in ascending order."""
        return sorted({bone for mask in self.masks for bone in mask_bones(mask)})

    def constant_bones(self):
        return mask_bones(self.masks[MASK_CONSTANT_ROTATION])

    def keyframed_bones(self):
        return mask_bones(self.masks[MASK_KEYFRAME_ROTATION])

    def constant_rotations(self, layout=ENGINE_LAYOUT):
        """bone id -> the single rotation held for the whole clip."""
        return self._constant(SECTION_CONSTANT_ROTATION, MASK_CONSTANT_ROTATION,
                              QUAT_BYTES,
                              lambda block, at: read_quaternion(block, at, layout))

    def constant_translations(self):
        """bone id -> the single offset held for the whole clip."""
        return self._constant(SECTION_CONSTANT_TRANSLATION,
                              MASK_CONSTANT_TRANSLATION, VEC3_BYTES,
                              _VEC3.unpack_from)

    def _constant(self, section, mask, stride, read):
        block = self.section(section)
        if not block:
            return {}
        count = _U16.unpack_from(block, 0)[0]
        out = {}
        # mask_bones yields ids ascending, so a bone's slot is its ordinal.
        for slot, bone_id in enumerate(mask_bones(self.masks[mask])):
            if slot >= count:
                break
            value = read(block, TRACK_HEADER + slot * stride)
            if value is not None:
                out[bone_id] = tuple(value)
        return out

    def keyframe_header(self):
        return self.track_header(SECTION_KEYFRAME_ROTATION)

    def keyframe_tracks(self, layout=ENGINE_LAYOUT):
        """bone id -> [(frame, quaternion)], read out of the sparse groups.

        Frames are grouped in eights. Each group stores, per track in bone-id
        order, the rotation at its first frame; then a presence byte per track,
        padded to an even count; then the rotations for the subframes those
        bytes name, again in track order. Bit i of a presence byte means a key
        at subframe i + 1, and bit 7 is the group's first frame, which is always
        present and stored up front.
        """
        block = self.section(SECTION_KEYFRAME_ROTATION)
        bones = self.keyframed_bones()
        header = self.keyframe_header()
        if not block or not bones or not header:
            return {}
        tracks, last_frame, _rate = header
        if tracks != len(bones):
            raise ValueError("%d tracks for %d bones in the keyframe mask"
                             % (tracks, len(bones)))
        groups = (last_frame >> GROUP_SHIFT) + 1
        offsets = struct.unpack_from("<%di" % groups, block, TRACK_HEADER)

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

    def translation_tracks(self):
        """bone id -> [(frame, offset)], one entry per frame with no gaps."""
        return self._dense(SECTION_ANIMATED_TRANSLATION,
                           mask_bones(self.masks[MASK_ANIMATED_TRANSLATION]),
                           VEC3_BYTES, _VEC3.unpack_from)

    def root_translation(self):
        """[(frame, offset)] for the trajectory the clip drives the actor along."""
        return self._dense(SECTION_ROOT_TRANSLATION, [ROOT_BONE],
                           VEC3_BYTES, _VEC3.unpack_from).get(ROOT_BONE, [])

    def root_rotation(self, layout=ENGINE_LAYOUT):
        """[(frame, quaternion)] for the heading that trajectory is turned to."""
        return self._dense(
            SECTION_ROOT_ROTATION, [ROOT_BONE], QUAT_BYTES,
            lambda block, at: read_quaternion(block, at, layout)).get(ROOT_BONE, [])

    def _dense(self, section, bones, stride, read):
        """A frame-major section: every track's value at frame 0, then at 1, ..."""
        block = self.section(section)
        header = self.track_header(section)
        if not block or not header or not bones:
            return {}
        tracks, last_frame, _rate = header
        if tracks != len(bones):
            raise ValueError("section %d holds %d tracks for %d bones"
                             % (section, tracks, len(bones)))
        out = {bone: [] for bone in bones}
        for frame in range(last_frame + 1):
            base = TRACK_HEADER + frame * tracks * stride
            for slot, bone in enumerate(bones):
                out[bone].append((frame, read(block, base + slot * stride)))
        return out

    def tags(self):
        return self.section(SECTION_TAGS)

    def participants(self):
        """What this clip animates besides its own skeleton, in chain order."""
        block = self.section(SECTION_TAGS)
        if not block:
            return []
        out = []
        for index in range(struct.unpack_from("<I", block, 0)[0]):
            base = TAG_COUNT_BYTES + index * TAG_STRIDE
            if base + TAG_STRIDE > len(block):
                break
            name, parent, _spare, reference = (tag_name(block, base + at)
                                               for at in TAG_NAMES)
            # The record stores its clip relative to itself; carry it relative
            # to this clip instead, which is what section() and data are in.
            offset = struct.unpack_from("<i", block, base + TAG_CLIP)[0]
            out.append(Participant(
                kind=block[base + TAG_KIND], name=name, parent=parent,
                reference=reference,
                clip_offset=self.sections[SECTION_TAGS] + base + offset))
        return out

    def participant_clips(self):
        """(participant, its clip) for everything this clip attaches."""
        return [(part, Clip.parse(self.data[part.clip_offset - CLIP_HEADER:]))
                for part in self.participants()]

    def events(self):
        return self.section(SECTION_EVENTS)

    def next_clip(self):
        """The next skeleton's clip in the bank, or None at the end."""
        block = self.section(SECTION_NEXT_CLIP)
        return Clip.parse(block) if block else None

    def clips(self):
        """Every clip in the bank, this one first."""
        out, clip = [], self
        while clip is not None:
            out.append(clip)
            clip = clip.next_clip()
        return out


class MabFile(Clip):
    """A bank on disk: a small file header, then the first clip."""

    def __init__(self):
        super().__init__()
        self.header = b""

    @property
    def version(self):
        return _U16.unpack_from(self.header, 0)[0]

    @classmethod
    def parse(cls, data):
        if len(data) < HEADER_SIZE + CLIP_HEADER:
            raise ValueError("file too small to be a .mab")
        version = _U16.unpack_from(data, 0)[0]
        if version != VERSION_FC2:
            raise ValueError("unsupported .mab version %#x" % version)
        self = super().parse(data[HEADER_SIZE:])
        self.header = data[:HEADER_SIZE]
        return self

    def write(self):
        return self.header + super().write()
