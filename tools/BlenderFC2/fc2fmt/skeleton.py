# Reader and writer for `.skeleton` (magic `LKS\0`), the Dunia rig file.
#
# Layout is documented in docs/docs/file-formats/skeleton.md. Only object
# version 7 is supported, which is the only one any shipped file uses.

from dataclasses import dataclass, field

from .binary import Reader, Writer, name_hash

MAGIC = b"LKS\x00"
FILE_VERSION = 18
OBJECT_TAG = 0x3ADE68B1
BONE_VERSION = 7
HANDLE_VERSION = 3
NO_BONE = 0xFFFF

# Slots in m_rgCommonBoneIds and m_rgTranslationBoneIds that name no bone.
EMPTY_SLOT = 0xFFFF

# Orientation constraint kinds, from the m_eOriConst switch in SerializeBone.
ORI_NONE = 0
ORI_LOOK_AT = 1
ORI_BLEND = 2
ORI_DEPENDENT = 3
ORI_DAMPED = 4

# Position constraint kinds 1..3 all carry a scale-to bone plus an offset.
POS_NONE = 0


@dataclass
class Constraint:
    """A bone's orientation or position constraint payload."""
    kind: int
    bones: list = field(default_factory=list)
    weights: list = field(default_factory=list)
    offset: list = field(default_factory=list)


@dataclass
class Bone:
    name: str
    id: int
    parent: int
    first_child: int
    next_sibling: int
    child_to_parent: list
    local_offset: list
    length: float
    ori: Constraint
    pos: Constraint
    animated_translation: int
    body_part: int
    com_weight: float
    name_hash: int
    version: int = BONE_VERSION


@dataclass
class AnimHandle:
    id: int
    name: str
    parent_bone: str
    child_to_parent: list
    local_offset: list
    parent_to_child: list
    local_offset_inverted: list
    parent_to_child_repeat: list
    name_hash: int
    parent_bone_hash: int
    version: int = HANDLE_VERSION


def _read_constraint(r, kind, is_ori):
    """Constraint payloads are fixed per kind; see the SerializeBone switch."""
    if is_ori:
        if kind == ORI_LOOK_AT:
            return Constraint(kind, [r.i32()], [], r.vec3())
        if kind == ORI_BLEND:
            first, first_weight = r.i32(), r.f32()
            second, second_weight = r.i32(), r.f32()
            return Constraint(kind, [first, second], [first_weight, second_weight])
        if kind in (ORI_DEPENDENT, ORI_DAMPED):
            return Constraint(kind, [r.i32()], [r.f32()])
    elif 1 <= kind <= 3:
        return Constraint(kind, [r.i32()], [], r.vec3())
    return Constraint(kind)


def _write_constraint(w, c, is_ori):
    if is_ori:
        if c.kind == ORI_LOOK_AT:
            w.i32(c.bones[0]).vec3(c.offset)
        elif c.kind == ORI_BLEND:
            w.i32(c.bones[0]).f32(c.weights[0]).i32(c.bones[1]).f32(c.weights[1])
        elif c.kind in (ORI_DEPENDENT, ORI_DAMPED):
            w.i32(c.bones[0]).f32(c.weights[0])
    elif 1 <= c.kind <= 3:
        w.i32(c.bones[0]).vec3(c.offset)


class SkeletonFile:
    def __init__(self):
        self.file_version = FILE_VERSION
        self.version = BONE_VERSION
        self.bones = []
        self.common_bone_ids = []
        self.handles = []
        self.scale_factor = 1.0
        self.translation_bone_ids = []
        # Three groups of five, stored zeroed; CSkeleton::FillLODBitmask
        # regenerates them after load.
        self.lod_masks = [[0] * 5 for _ in range(3)]

    @classmethod
    def parse(cls, data):
        r = Reader(data)
        if r.raw(4) != MAGIC:
            raise ValueError("not a .skeleton file")
        self = cls()
        self.file_version = r.u32()
        tag, self.version = r.u32(), r.u32()
        if tag != OBJECT_TAG:
            raise ValueError("bad skeleton object tag %#x" % tag)
        bone_count, common_count = r.u16(), r.u16()

        for _ in range(bone_count):
            bone_tag, bone_version = r.u32(), r.u32()
            if bone_tag != OBJECT_TAG or bone_version < BONE_VERSION:
                raise ValueError("unsupported bone encoding %#x v%d" % (bone_tag, bone_version))
            quat, offset, length = r.quat(), r.vec3(), r.f32()
            ids = r.u16s(4)
            ori = _read_constraint(r, r.u8(), True)
            pos = _read_constraint(r, r.u8(), False)
            hashed, name = r.string_id()
            self.bones.append(Bone(
                name=name, id=ids[0], parent=ids[1], first_child=ids[2], next_sibling=ids[3],
                child_to_parent=quat, local_offset=offset, length=length, ori=ori, pos=pos,
                animated_translation=r.u8(), body_part=r.u8(), com_weight=r.f32(),
                name_hash=hashed, version=bone_version))

        self.common_bone_ids = r.u16s(common_count)
        for _ in range(r.u16()):
            handle_tag, handle_version = r.u32(), r.u32()
            if handle_tag != OBJECT_TAG:
                raise ValueError("bad anim handle tag %#x" % handle_tag)
            handle_id = r.u16()
            name_hashed, name = r.string_id()
            parent_hashed, parent = r.string_id()
            self.handles.append(AnimHandle(
                id=handle_id, name=name, parent_bone=parent,
                child_to_parent=r.quat(), local_offset=r.vec3(),
                parent_to_child=r.quat(), local_offset_inverted=r.vec3(),
                parent_to_child_repeat=r.quat(),
                name_hash=name_hashed, parent_bone_hash=parent_hashed,
                version=handle_version))

        self.scale_factor = r.f32()
        self.translation_bone_ids = r.u16s(r.u16())
        self.lod_masks = []
        for _ in range(3):
            chunk_count = r.u32()
            self.lod_masks.append(r.u32s(chunk_count))
        if r.pos != len(data):
            raise ValueError("trailing bytes: consumed %d of %d" % (r.pos, len(data)))
        return self

    def write(self):
        w = Writer()
        w.raw(MAGIC).u32(self.file_version).u32(OBJECT_TAG).u32(self.version)
        w.u16(len(self.bones)).u16(len(self.common_bone_ids))

        for b in self.bones:
            w.u32(OBJECT_TAG).u32(b.version)
            w.quat(b.child_to_parent).vec3(b.local_offset).f32(b.length)
            w.u16s([b.id, b.parent, b.first_child, b.next_sibling])
            w.u8(b.ori.kind)
            _write_constraint(w, b.ori, True)
            w.u8(b.pos.kind)
            _write_constraint(w, b.pos, False)
            w.string_id(b.name, b.name_hash)
            w.u8(b.animated_translation).u8(b.body_part).f32(b.com_weight)

        w.u16s(self.common_bone_ids)
        w.u16(len(self.handles))
        for h in self.handles:
            w.u32(OBJECT_TAG).u32(h.version).u16(h.id)
            w.string_id(h.name, h.name_hash)
            w.string_id(h.parent_bone, h.parent_bone_hash)
            w.quat(h.child_to_parent).vec3(h.local_offset)
            w.quat(h.parent_to_child).vec3(h.local_offset_inverted)
            w.quat(h.parent_to_child_repeat)

        w.f32(self.scale_factor)
        w.u16(len(self.translation_bone_ids)).u16s(self.translation_bone_ids)
        for mask in self.lod_masks:
            w.u32(len(mask)).u32s(mask)
        return w.bytes()

    def bone_by_name(self, name):
        """Resolved by CRC32, the way the engine matches an .xbg node to a bone."""
        wanted = name_hash(name)
        return next((b for b in self.bones if b.name_hash == wanted), None)

    def rebuild_hierarchy(self):
        """Recompute first_child/next_sibling from each bone's parent."""
        by_id = {b.id: b for b in self.bones}
        for bone in self.bones:
            bone.first_child = bone.next_sibling = NO_BONE
        for bone in reversed(self.bones):
            parent = by_id.get(bone.parent)
            if parent is not None:
                bone.next_sibling, parent.first_child = parent.first_child, bone.id
