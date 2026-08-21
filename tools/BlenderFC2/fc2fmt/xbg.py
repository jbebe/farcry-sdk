# Reader and writer for `.xbg`, the Dunia mesh container.
#
# Layout is documented in docs/docs/file-formats/xbm-xbg.md. Tags are stored
# reversed, so EDON in the file is NODE to the engine.

from dataclasses import dataclass, field

from .binary import Reader, Writer
from .transform import multiply, trs_matrix

MAGIC = b"HSEM"
VERSION_FC2 = 0x0006002A
CHUNK_HEADER = 20
NODE_RECORD = 0x44
PALETTE_SLOTS = 48
EMPTY_SLOT = -1
NO_NODE = 0xFFFFFFFF

# Vertex component flags. Position is one of the first three; the rest are
# independent bits consumed in this fixed order.
POS_FLOAT, POS_INT16, POS_HALF = 0x0001, 0x0002, 0x0004
UV0, BONE_WTS1, BONE_WTS2, NORMAL = 0x0008, 0x0010, 0x0020, 0x0040
COLOR, TANGENT, BINORMAL, UNK400, UV1, UV2 = 0x0080, 0x0100, 0x0200, 0x0400, 0x0800, 0x1000

POSITION_KINDS = ((POS_FLOAT, 12), (POS_INT16, 8), (POS_HALF, 8))
COMPONENTS = (
    ("uv0", UV0, 4), ("uv1", UV1, 4), ("uv2", UV2, 4),
    ("bone_wts1", BONE_WTS1, 8), ("bone_wts2", BONE_WTS2, 8),
    ("normal", NORMAL, 4), ("color", COLOR, 4),
    ("tangent", TANGENT, 4), ("binormal", BINORMAL, 4), ("unk400", UNK400, 4),
)

TAG_NODE, TAG_O2BM, TAG_RMTL, TAG_SKID = "EDON", "MB2O", "LTMR", "DIKS"
TAG_SKND, TAG_LODS, TAG_BBOX, TAG_BSPH = "DNKS", "SDOL", "XOBB", "HPSB"
TAG_LOD, TAG_PCMP, TAG_UCMP = "DOL\x00", "PMCP", "PMCU"
TAG_CLUS = "SULC"
TAG_LTMD = "LTMD"


def vertex_layout(flags):
    """Component byte offsets and the stride implied by the format flags."""
    offsets, cursor = {}, 0
    for bit, size in POSITION_KINDS:
        if flags & bit:
            offsets["pos"] = cursor
            cursor += size
            break
    for name, bit, size in COMPONENTS:
        if flags & bit:
            offsets[name] = cursor
            cursor += size
    return offsets, cursor


@dataclass
class Node:
    """An EDON node: a skinning bone, or the pivot a rigid part is modelled on."""
    name: str
    name_hash: int
    first_child: int
    next_sibling: int
    parent: int
    rotation: list
    translation: list
    scale: list
    skin_index: int
    weight: float
    extent: float


@dataclass
class VertexBuffer:
    flags: int
    stride: int
    unknown: int
    offset: int


@dataclass
class Submesh:
    """Where one cluster's triangles live: which buffer, and where in the indices.

    `part` indexes XbgFile.skin_descs and `cluster` the cluster inside it, which
    is how a draw call is paired with its material and bone palette.
    """
    buffer: int
    part: int
    cluster: int
    index_offset: int
    trailing: list

    @classmethod
    def read(cls, r):
        words = r.u32s(7)
        return cls(words[0], words[1], words[2], words[3], words[4:])

    def write(self, w):
        w.u32s([self.buffer, self.part, self.cluster, self.index_offset] + self.trailing)


@dataclass
class Lod:
    distance: float
    vertex_buffers: list
    submeshes: list
    vertex_data: bytes
    index_data: bytes


@dataclass
class Cluster:
    """One drawable block: material slot, counts, and its 48-slot bone palette.

    face_count is stored twice and index_count is 3x it, so both are derived on
    write rather than tracked — editing one of the three cannot desync the rest.
    """
    material_index: int
    face_count: int
    stride: int
    vertex_count: int
    flags: int
    palette: list

    @classmethod
    def read(cls, r):
        material, faces, _faces_again, _indices, stride, vertices, flags = r.u16s(7)
        return cls(material, faces, stride, vertices, flags, r.i16s(PALETTE_SLOTS))

    def write(self, w):
        w.u16s([self.material_index, self.face_count, self.face_count,
                self.face_count * 3, self.stride, self.vertex_count, self.flags])
        w.i16s(self.palette)

    @property
    def is_skinned(self):
        return bool(self.flags & BONE_WTS1)

    def bones(self):
        return [slot for slot in self.palette if slot != EMPTY_SLOT]


@dataclass
class SkinDesc:
    """A named (part, damage state, LOD) group and the clusters drawing it.

    `bounds` is ten floats whose grouping is undetermined: the community layout
    of a min/max pair holds for 18 of 18,533 shipped parts, so it is carried
    through rather than interpreted. `lod` matches the name's _LODn suffix in
    all 18,533.
    """
    name: str
    lod_metric: float
    bounds: tuple
    lod: int
    reserved: int
    clusters: list = field(default_factory=list)

    @classmethod
    def read(cls, r):
        return cls(name="", lod_metric=r.f32(), bounds=tuple(r.f32s(10)),
                   lod=r.i32(), reserved=r.u32())

    def write(self, w):
        w.f32(self.lod_metric).f32s(self.bounds).i32(self.lod).u32(self.reserved)


@dataclass
class Chunk:
    """Chunk identity, the header word we do not interpret, and any opaque body."""
    tag: str
    word0: int
    raw: bytes = b""


class XbgFile:
    def __init__(self):
        self.version = VERSION_FC2
        self.header_words = [0] * 5
        self.chunks = []
        self.nodes = []
        self.bind_matrices = []
        self.materials = []
        self.material_word = None
        self.lod_distances = []
        self.lods = []
        self.skin_descs = []
        self.cluster_word0 = 0
        self.bbox = []
        self.bsphere = []
        self.lod_words = []
        self.pos_compress = []
        self.uv_compress = []

    @property
    def pos_scale(self):
        return self.pos_compress[1]

    @property
    def uv_translate(self):
        return self.uv_compress[0]

    @property
    def uv_scale(self):
        return self.uv_compress[1]

    @classmethod
    def parse(cls, data):
        if data[:4] != MAGIC:
            raise ValueError("not an .xbg file")
        self = cls()
        r = Reader(data, 4)
        self.version = r.u32()
        self.header_words = r.u32s(5)
        chunk_count = r.u32()

        pos = 32
        for _ in range(chunk_count):
            tag = data[pos:pos + 4].decode("latin-1")
            word0, size, payload_size, sub_count = Reader(data, pos + 4).u32s(4)
            if size < CHUNK_HEADER:
                raise ValueError("chunk %r at %d has size %d" % (tag, pos, size))
            chunk = Chunk(tag, word0)
            self._read_chunk(data, chunk, pos, size, pos + size - payload_size, sub_count)
            self.chunks.append(chunk)
            pos += size
        if pos != len(data):
            raise ValueError("trailing bytes: consumed %d of %d" % (pos, len(data)))
        return self

    def _read_chunk(self, data, chunk, start, size, payload, sub_count):
        tag = chunk.tag
        r = Reader(data, payload)
        if tag == TAG_NODE:
            self.nodes = [_read_node(r) for _ in range(r.u32())]
        elif tag == TAG_O2BM:
            self.bind_matrices = [r.f32s(16) for _ in range(r.u32())]
        elif tag == TAG_RMTL:
            count = r.u32()
            self.material_word = r.u32() if _has_material_word(self.version) else None
            self.materials = [r.cstring() for _ in range(count)]
        elif tag == TAG_SKID:
            self.lod_distances = [r.u32s(2) for _ in range(r.u32())]
        elif tag == TAG_LODS:
            self.lods = _read_lods(r)
        elif tag == TAG_SKND:
            self._read_skin(data, start, payload, sub_count)
        elif tag == TAG_BBOX:
            self.bbox = r.f32s(6)
        elif tag == TAG_BSPH:
            self.bsphere = r.f32s(4)
        elif tag == TAG_LOD:
            self.lod_words = r.u32s(2)
        elif tag == TAG_PCMP:
            self.pos_compress = r.f32s(2)
        elif tag == TAG_UCMP:
            self.uv_compress = r.f32s(2)
        else:
            chunk.raw = data[start + CHUNK_HEADER:start + size]
            return
        if r.pos > start + size:
            raise ValueError("chunk %r overran by %d bytes" % (tag, r.pos - start - size))

    def _read_skin(self, data, start, payload, sub_count):
        """DNKS names the parts; its SULC sub-chunk holds their bone clusters."""
        if sub_count != 1:
            raise ValueError("DNKS with %d sub-chunks" % sub_count)
        sub = start + CHUNK_HEADER
        if data[sub:sub + 4].decode("latin-1") != TAG_CLUS:
            raise ValueError("DNKS sub-chunk is not SULC")
        self.cluster_word0, sub_size, sub_payload_size, _ = Reader(data, sub + 4).u32s(4)

        names = Reader(data, payload)
        self.skin_descs = []
        for _ in range(names.u32()):
            desc = SkinDesc.read(names)
            desc.name = names.cstring()
            self.skin_descs.append(desc)

        clusters = Reader(data, sub + sub_size - sub_payload_size)
        for desc in self.skin_descs:
            desc.clusters = [Cluster.read(clusters) for _ in range(clusters.u32())]

    def write(self):
        w = Writer()
        w.raw(MAGIC).u32(self.version).u32s(self.header_words).u32(len(self.chunks))
        for chunk in self.chunks:
            self._write_chunk(w, chunk)
        return w.bytes()

    def _write_chunk(self, w, chunk):
        start = len(w)
        w.raw(chunk.tag.encode("latin-1")).u32(chunk.word0).u32(0).u32(0).u32(0)
        sub_count = 1 if chunk.tag == TAG_SKND else 0

        if chunk.tag == TAG_SKND:
            _write_clusters(w, self.skin_descs, self.cluster_word0)
            payload = len(w)
            w.u32(len(self.skin_descs))
            for desc in self.skin_descs:
                desc.write(w)
                w.cstring(desc.name)
        else:
            payload = len(w)
            self._write_payload(w, chunk)

        w.patch_u32(start + 8, len(w) - start)
        w.patch_u32(start + 12, len(w) - payload)
        w.patch_u32(start + 16, sub_count)

    def _write_payload(self, w, chunk):
        tag = chunk.tag
        if tag == TAG_NODE:
            w.u32(len(self.nodes))
            for node in self.nodes:
                _write_node(w, node)
        elif tag == TAG_O2BM:
            w.u32(len(self.bind_matrices))
            for matrix in self.bind_matrices:
                w.f32s(matrix)
        elif tag == TAG_RMTL:
            w.u32(len(self.materials))
            if self.material_word is not None:
                w.u32(self.material_word)
            for name in self.materials:
                w.cstring(name)
        elif tag == TAG_SKID:
            w.u32(len(self.lod_distances))
            for pair in self.lod_distances:
                w.u32s(pair)
        elif tag == TAG_LODS:
            _write_lods(w, self.lods)
        elif tag == TAG_BBOX:
            w.f32s(self.bbox)
        elif tag == TAG_BSPH:
            w.f32s(self.bsphere)
        elif tag == TAG_LOD:
            w.u32s(self.lod_words)
        elif tag == TAG_PCMP:
            w.f32s(self.pos_compress)
        elif tag == TAG_UCMP:
            w.f32s(self.uv_compress)
        else:
            w.raw(chunk.raw)

    def node_by_name(self, name):
        """The root stores a zero hash, so match on the name the engine hashes."""
        return next((n for n in self.nodes if n.name == name), None)

    def node_world_matrices(self):
        """Each node's world transform, parent applied before child."""
        matrices = []
        for node in self.nodes:
            local = trs_matrix(node.rotation, node.translation, node.scale)
            parent = matrices[node.parent] if node.parent < len(matrices) else None
            matrices.append(multiply(parent, local) if parent else local)
        return matrices

    def part_placement(self, part_name, skinned=False):
        """Where a part sits in model space.

        A rigid part is modelled around its own pivot and placed by the node
        sharing its name, so skipping this piles every wheel, door and magazine
        at the origin. A skinned part is in the skeleton root's bind space and
        takes node 0 instead, which lifts a character off the floor.
        """
        matrices = self.node_world_matrices()
        if skinned:
            return matrices[0] if matrices else None
        # Part names are upper-cased against mixed-case node names, so only 559
        # of 16,876 rigid parts match exactly while all of them match folded.
        wanted = part_name.lower()
        index = next((i for i, n in enumerate(self.nodes) if n.name.lower() == wanted), None)
        return None if index is None else matrices[index]

    def rebuild_hierarchy(self):
        """Recompute sibling links and skin indices after nodes have been edited.

        Both are derived from node order, so an exporter that adds or removes a
        node must call this or leave MB2O and the cluster palettes dangling.
        """
        for node in self.nodes:
            node.first_child = node.next_sibling = NO_NODE
        for index in reversed(range(len(self.nodes))):
            node = self.nodes[index]
            if node.parent < len(self.nodes):
                parent = self.nodes[node.parent]
                node.next_sibling, parent.first_child = parent.first_child, index
        skinning = 0
        for node in self.nodes:
            if node.skin_index != EMPTY_SLOT:
                node.skin_index = skinning
                skinning += 1


def _has_material_word(version):
    """LTMR gained a trailing word after mesh version 41.3; FC2 ships 42.6."""
    major, minor = version & 0xFFFF, version >> 16
    return major > 0x29 or (major == 0x29 and minor > 3)


def _read_node(r):
    start = r.pos
    hashed, first_child, next_sibling, parent = r.u32s(4)
    node = Node(name="", name_hash=hashed, first_child=first_child,
                next_sibling=next_sibling, parent=parent,
                rotation=r.quat(), translation=r.vec3(), scale=r.vec3(),
                skin_index=r.i32(), weight=r.f32(), extent=r.f32())
    node.name = r.seek(start + NODE_RECORD).cstring()
    return node


def _write_node(w, node):
    w.u32s([node.name_hash, node.first_child, node.next_sibling, node.parent])
    w.quat(node.rotation).vec3(node.translation).vec3(node.scale)
    w.i32(node.skin_index).f32(node.weight).f32(node.extent)
    w.cstring(node.name)


def _read_lods(r):
    lods = []
    for _ in range(r.u32()):
        distance = r.f32()
        buffers = [VertexBuffer(*r.u32s(4)) for _ in range(r.u32())]
        submeshes = [Submesh.read(r) for _ in range(r.u32())]
        vertex_size = r.u32()
        vertex_data = r.align(16).raw(vertex_size)
        index_count = r.u32()
        lods.append(Lod(distance, buffers, submeshes, vertex_data,
                        r.align(16).raw(index_count * 2)))
    return lods


def _write_lods(w, lods):
    w.u32(len(lods))
    for lod in lods:
        w.f32(lod.distance)
        w.u32(len(lod.vertex_buffers))
        for vb in lod.vertex_buffers:
            w.u32s([vb.flags, vb.stride, vb.unknown, vb.offset])
        w.u32(len(lod.submeshes))
        for submesh in lod.submeshes:
            submesh.write(w)
        w.u32(len(lod.vertex_data))
        w.align(16).raw(lod.vertex_data)
        w.u32(len(lod.index_data) // 2)
        w.align(16).raw(lod.index_data)


def _write_clusters(w, skin_descs, word0):
    """Emit the SULC sub-chunk, whose payload is also addressed from its end."""
    start = len(w)
    w.raw(TAG_CLUS.encode("latin-1")).u32(word0).u32(0).u32(0).u32(0)
    payload = len(w)
    for desc in skin_descs:
        w.u32(len(desc.clusters))
        for cluster in desc.clusters:
            cluster.write(w)
    w.patch_u32(start + 8, len(w) - start)
    w.patch_u32(start + 12, len(w) - payload)
