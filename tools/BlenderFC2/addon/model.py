# Turn a pack's mesh document into per-part meshes ready for a 3D application.
#
# There is no decoding here. The pack already holds float positions in metres,
# UVs, normals and per-vertex skin pairs; this pairs each cluster's geometry
# with the part that owns it, resolves its bone palette to node names, and
# groups a flat array into the points a scene wants.
#
# Triangles come out in the file's own winding, which is D3D clockwise; flipping
# for a right-handed application is the caller's job.

import re
from dataclasses import dataclass, field

from .transform import apply, apply_direction, multiply, trs_matrix

LOD_SUFFIX = re.compile(r"_LOD\d+$", re.IGNORECASE)

# What a part's `placement_node` holds when nothing places it, so it sits in the
# root's space - every skinned part, and any rigid one already modelled in place.
NO_PLACEMENT = 0xFFFF

# What a palette slot holds when it names no bone.
EMPTY_SLOT = -1


@dataclass
class PartMesh:
    """One drawable part of one LOD, with its vertices already grouped."""
    name: str
    full_name: str
    # Which entry of the LOD's geometry list drew this, so an exporter can put
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
    # Set only when parts_at() left the vertices in part space; applying it then
    # is the caller's job. Baked extraction leaves this None so nothing can
    # transform the same part twice.
    placement: tuple = None
    # The node the pack says places this part, so a caller can parent to that bone
    # without going back to the document to find it again.
    placement_node: int = NO_PLACEMENT

    @property
    def is_skinned(self):
        return self.skin is not None

    def bone_slots(self):
        """Per-vertex (weight, bone name) pairs, palette already resolved."""
        if self.skin is None:
            return None
        return [[(weight, self.palette[slot]) for weight, slot in vertex
                 if 0 <= slot < len(self.palette)
                 and self.palette[slot] is not None]
                for vertex in self.skin]


def part_name(full_name):
    return LOD_SUFFIX.sub("", full_name)


def lod_tier(full_name, fallback=0):
    """The tier a part's _LODn suffix names."""
    found = LOD_SUFFIX.search(full_name)
    return int(found.group()[4:]) if found else fallback


def bone_name(mesh, index):
    """What a node is called, named or not - the same name a bone gets."""
    return mesh["nodes"][index]["name"] or "node_%d" % index


def node_world_matrices(mesh):
    """Every node's transform in model space, parents before children.

    Nodes are stored parent-first, so one pass composes the whole tree.
    """
    world = []
    for node in mesh["nodes"]:
        local = trs_matrix(node["rotation"], node["translation"], node["scale"])
        parent = node["parent"]
        world.append(multiply(world[parent], local) if parent < len(world) else local)
    return world


def part_placement(mesh, part, world=None):
    """Where a part sits in model space.

    A rigid part is modelled around its own pivot, so skipping this piles every
    wheel, door and magazine at the origin. A part the document gives no
    placement node - every skinned one, and any rigid one already modelled in
    place - sits in the root's space instead, which is what lifts a character
    off the floor.
    """
    world = world or node_world_matrices(mesh)
    if not world:
        return None
    node = part.get("placement_node", NO_PLACEMENT)
    return world[0] if node >= len(mesh["nodes"]) else world[node]


def lod_count(mesh):
    return len(mesh["lods"])


def parts_at(mesh, lod_index=0, place=True):
    """Every part drawn at one LOD.

    With `place`, vertices come back in model space and `PartMesh.placement` is
    None. Without it, vertices stay around their own pivot and `placement`
    carries the matrix that would move them - which is what an application wants
    when the part is parented to the bone instead.
    """
    world = node_world_matrices(mesh)
    names = [node["name"] or "node_%d" % index
             for index, node in enumerate(mesh["nodes"])]
    lod = mesh["lods"][lod_index]

    meshes = []
    for submesh, geometry in enumerate(lod["geometry"]):
        indices = geometry["indices"]
        if not indices:
            continue
        part = mesh["parts"][geometry["part"]]
        cluster = part["clusters"][geometry["cluster"]]
        placement = part_placement(mesh, part, world)
        meshes.append(_build(mesh, part, cluster, geometry, indices, names,
                             placement, place, submesh))
    return meshes


def _build(mesh, part, cluster, geometry, indices, names, placement, place, submesh):
    matrix = placement if place else None
    positions = _points(geometry["positions"], 3, matrix, apply)
    normals = _points(geometry.get("normals"), 3, matrix, apply_direction)
    materials = mesh["materials"]
    index = cluster["material_index"]
    built = PartMesh(
        name=part_name(part["name"]),
        full_name=part["name"],
        submesh=submesh,
        lod=lod_tier(part["name"]),
        material=materials[index] if index < len(materials) else "",
        positions=positions,
        triangles=[tuple(indices[at:at + 3]) for at in range(0, len(indices), 3)],
        normals=normals,
        uvs=_points(geometry.get("uvs"), 2),
        uvs1=_points(geometry.get("uvs1"), 2),
        colours=_points(geometry.get("colours"), 4),
        skin=_skin(geometry),
        palette=[names[slot] if 0 <= slot < len(names) else None
                 for slot in cluster["palette"]],
        placement_node=part.get("placement_node", NO_PLACEMENT))
    if not place:
        built.placement = placement
    return built


def _points(values, stride, matrix=None, transform=None):
    """A flat array grouped into tuples, optionally moved by a matrix."""
    if not values:
        return None
    points = [tuple(values[at:at + stride]) for at in range(0, len(values), stride)]
    return [transform(matrix, p) for p in points] if matrix else points


def _skin(geometry):
    """Per-vertex (weight, palette slot) pairs, zero weights dropped.

    The pack pads every vertex to the wider of the one- and two-set formats, so
    a four-influence vertex in an eight-slot buffer carries four zeros - which
    would otherwise become vertex groups holding nothing.
    """
    weights = geometry.get("skin_weights")
    slots = geometry.get("skin_slots")
    if not weights or not slots:
        return None

    stride = len(weights) // geometry["vertex_count"]
    return [[(weights[at + i], slots[at + i]) for i in range(stride)
             if weights[at + i] > 0.0]
            for at in range(0, len(weights), stride)]
