# Assemble an .xbg into per-part meshes ready for a 3D application.
#
# The container stores one vertex buffer behind many parts, so this walks the
# SDOL submesh table, pairs each draw call with its DNKS cluster, and emits only
# the vertices that part actually references.
#
# Triangles come out in the file's own winding, which is D3D clockwise; flipping
# for a right-handed application is the caller's job.

import re
from dataclasses import dataclass, field

from .transform import apply, apply_direction
from .vertex import VertexStream, buffer_vertex_count, unpack_indices

LOD_SUFFIX = re.compile(r"_LOD\d+$", re.IGNORECASE)
DEGENERATE = 0xFFFF


@dataclass
class PartMesh:
    """One drawable part of one LOD, with its vertices already localised."""
    name: str
    full_name: str
    lod: int
    material: str
    positions: list
    triangles: list
    normals: list = None
    uvs: list = None
    uvs1: list = None
    colours: list = None
    skin: list = None
    palette: list = field(default_factory=list)
    # Set only when extract() left the vertices in part space; applying it then
    # is the caller's job. Baked extraction leaves this None so nothing can
    # transform the same part twice.
    placement: tuple = None

    @property
    def is_skinned(self):
        return self.skin is not None

    def bone_slots(self):
        """Per-vertex (weight, node index) pairs, palette already resolved."""
        if self.skin is None:
            return None
        return [[(w, self.palette[slot]) for w, slot in v if slot < len(self.palette)]
                for v in self.skin]


def part_name(full_name):
    return LOD_SUFFIX.sub("", full_name)


def extract(model, lod_index=0, place=True):
    """Every part drawn at one LOD.

    With `place`, vertices come back in model space and `PartMesh.placement` is
    None. Without it, vertices stay around their own pivot and `placement`
    carries the matrix that would move them — which is what an application
    wants when the part is parented to the bone instead.
    """
    lod = model.lods[lod_index]
    indices = unpack_indices(lod)
    streams, meshes = {}, []

    for submesh in lod.submeshes:
        if submesh.part >= len(model.skin_descs):
            continue
        desc = model.skin_descs[submesh.part]
        if submesh.cluster >= len(desc.clusters):
            continue
        cluster = desc.clusters[submesh.cluster]
        if not cluster.face_count:
            continue

        if submesh.buffer not in streams:
            buffer = lod.vertex_buffers[submesh.buffer]
            streams[submesh.buffer] = (
                buffer,
                VertexStream.unpack(lod.vertex_data, buffer,
                                    buffer_vertex_count(lod, submesh.buffer)))
        buffer, stream = streams[submesh.buffer]

        triangles, used = [], {}
        start = submesh.index_offset
        for corner in range(start, start + cluster.face_count * 3, 3):
            face = indices[corner:corner + 3]
            if len(face) < 3 or DEGENERATE in face:
                continue
            triangles.append(tuple(used.setdefault(v, len(used)) for v in face))
        if not triangles:
            continue

        order = sorted(used, key=used.get)
        placement = model.part_placement(part_name(desc.name), cluster.is_skinned)
        part = _build(model, desc, cluster, stream, order, triangles,
                      placement if place else None)
        if not place:
            part.placement = placement
        meshes.append(part)
    return meshes


def _gather(values, order, transform=None):
    if values is None:
        return None
    picked = [values[i] for i in order]
    return [transform(v) for v in picked] if transform else picked


def _build(model, desc, cluster, stream, order, triangles, placement):
    positions = _gather(stream.positions(model.pos_scale), order,
                        (lambda p: apply(placement, p)) if placement else None)
    normals = _gather(stream.normals(), order,
                      (lambda n: apply_direction(placement, n)) if placement else None)
    skin = _gather(stream.skin(), order)
    material = (model.materials[cluster.material_index]
                if cluster.material_index < len(model.materials) else "")
    return PartMesh(
        name=part_name(desc.name), full_name=desc.name, lod=desc.lod, material=material,
        positions=positions, triangles=triangles, normals=normals,
        uvs=_gather(stream.uvs(model.uv_translate, model.uv_scale, 0), order),
        uvs1=_gather(stream.uvs(model.uv_translate, model.uv_scale, 1), order),
        colours=_gather(stream.colors(), order), skin=skin,
        palette=cluster.palette)
