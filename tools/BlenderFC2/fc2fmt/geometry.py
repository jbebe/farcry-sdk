# Split a LOD into per-cluster geometry, and put it back together.
#
# A LOD stores one flat vertex block and one flat index block. Every cluster
# owns a contiguous run of each: its vertices are `vertex_count` of them at the
# running total for that buffer, its indices are `face_count * 3` at the running
# total for the LOD, and both are ordered by submesh. Measured across the retail
# set, 29,296 of 29,296 clusters and 9,746 of 9,746 LODs.
#
# So editing a part means rebuilding the blocks rather than patching them, and
# every offset and count in the LOD is derived here rather than tracked.

import collections
from dataclasses import dataclass, field

from .vertex import VertexStream, pack_indices, unpack_indices


@dataclass
class ClusterGeometry:
    """One drawable block's own vertices and triangles, indices local to it."""
    submesh: int
    buffer: int
    part: int
    cluster: int
    vertices: VertexStream
    indices: list = field(default_factory=list)

    @property
    def face_count(self):
        return len(self.indices) // 3


def read_lod(model, lod):
    """Split a LOD into its clusters' geometry, one entry per submesh."""
    indices = unpack_indices(lod)
    streams, base, out = {}, collections.defaultdict(int), []
    for position, submesh in enumerate(lod.submeshes):
        cluster = _cluster(model, submesh, position)
        if submesh.buffer not in streams:
            streams[submesh.buffer] = VertexStream.unpack(
                lod.vertex_data, lod.vertex_buffers[submesh.buffer],
                lod.vertex_buffers[submesh.buffer].vertex_count)
        start = base[submesh.buffer]
        base[submesh.buffer] += cluster.vertex_count
        run = indices[submesh.index_offset:submesh.index_offset + cluster.face_count * 3]
        out.append(ClusterGeometry(
            submesh=position, buffer=submesh.buffer, part=submesh.part,
            cluster=submesh.cluster,
            vertices=streams[submesh.buffer].slice(start, cluster.vertex_count),
            indices=[index - start for index in run]))
    return out


def write_lod(model, lod, geometries):
    """Rebuild a LOD's blocks, offsets and counts from per-cluster geometry."""
    if len(geometries) != len(lod.submeshes):
        raise ValueError("%d geometries for %d submeshes"
                         % (len(geometries), len(lod.submeshes)))

    per_buffer = collections.defaultdict(list)
    for geometry in geometries:
        per_buffer[geometry.buffer].append(geometry)

    vertex_data = bytearray()
    for index, buffer in enumerate(lod.vertex_buffers):
        buffer.offset = len(vertex_data)
        for geometry in per_buffer[index]:
            vertex_data += geometry.vertices.pack()
    lod.vertex_data = bytes(vertex_data)

    indices, base = [], collections.defaultdict(int)
    for position, (submesh, geometry) in enumerate(zip(lod.submeshes, geometries)):
        cluster = _cluster(model, submesh, position)
        buffer = lod.vertex_buffers[geometry.buffer]
        start = base[geometry.buffer]
        base[geometry.buffer] += len(geometry.vertices)
        submesh.index_offset = len(indices)
        indices.extend(index + start for index in geometry.indices)
        cluster.face_count = geometry.face_count
        cluster.vertex_count = len(geometry.vertices)
        cluster.stride = buffer.stride
        # The submesh's last vertex index, then its byte offset into the LOD's
        # whole vertex block rather than into its own buffer.
        submesh.trailing = [start + cluster.vertex_count - 1,
                            buffer.offset + start * buffer.stride, 0]
    for index, buffer in enumerate(lod.vertex_buffers):
        buffer.vertex_count = base[index]
    lod.index_data = pack_indices(indices)


def _cluster(model, submesh, position):
    if submesh.part >= len(model.skin_descs):
        raise ValueError("submesh %d names part %d of %d"
                         % (position, submesh.part, len(model.skin_descs)))
    clusters = model.skin_descs[submesh.part].clusters
    if submesh.cluster >= len(clusters):
        raise ValueError("submesh %d names cluster %d of %d"
                         % (position, submesh.cluster, len(clusters)))
    return clusters[submesh.cluster]
