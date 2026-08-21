# Assemble an .xbg into per-part meshes ready for a 3D application.
#
# The container stores one vertex buffer behind many parts, so this pairs each
# draw call from fc2fmt.geometry with its DNKS cluster and material.
#
# Triangles come out in the file's own winding, which is D3D clockwise; flipping
# for a right-handed application is the caller's job.

import re
from dataclasses import dataclass, field

from .geometry import read_lod
from .transform import apply, apply_direction

LOD_SUFFIX = re.compile(r"_LOD\d+$", re.IGNORECASE)


@dataclass
class PartMesh:
    """One drawable part of one LOD, with its vertices already localised."""
    name: str
    full_name: str
    # Which entry of the LOD submesh table drew this, so an exporter can put
    # the edited geometry back in the right place.
    submesh: int
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
    meshes = []
    for geometry in read_lod(model, model.lods[lod_index]):
        if not geometry.face_count:
            continue
        desc = model.skin_descs[geometry.part]
        cluster = desc.clusters[geometry.cluster]
        # A cluster owns exactly the vertices it references — 29,296 of 29,296
        # in the retail set — so the slice is taken whole, in the file's own
        # order. Compacting it here would permute what an exporter writes back.
        triangles = [tuple(geometry.indices[corner:corner + 3])
                     for corner in range(0, len(geometry.indices), 3)]
        placement = model.part_placement(desc.name)
        part = _build(model, desc, cluster, geometry.vertices, triangles,
                      placement if place else None, geometry.submesh)
        if not place:
            part.placement = placement
        meshes.append(part)
    return meshes


def _map(values, transform):
    """Apply a transform to every value, or hand the list back untouched."""
    return [transform(v) for v in values] if values and transform else values


def _build(model, desc, cluster, stream, triangles, placement, position):
    positions = _map(stream.positions(model.pos_scale),
                     (lambda p: apply(placement, p)) if placement else None)
    normals = _map(stream.normals(),
                   (lambda n: apply_direction(placement, n)) if placement else None)
    material = (model.materials[cluster.material_index]
                if cluster.material_index < len(model.materials) else "")
    return PartMesh(
        name=part_name(desc.name), full_name=desc.name, submesh=position,
        lod=desc.lod, material=material,
        positions=positions, triangles=triangles, normals=normals,
        uvs=stream.uvs(model.uv_translate, model.uv_scale, 0),
        uvs1=stream.uvs(model.uv_translate, model.uv_scale, 1),
        colours=stream.colors(), skin=stream.skin(),
        palette=cluster.palette)
