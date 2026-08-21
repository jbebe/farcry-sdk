# Write a Blender scene back into the .xbg it was imported from.
#
# Export edits parts in place inside the original container, so the nodes,
# materials, bone palettes, the LODs that were never imported and every chunk
# this project carries through opaque all survive untouched. Only the geometry
# of the objects present is rebuilt.
#
# The file stores UVs, colours and normals per vertex, not per corner, so a
# split UV or a split normal cannot be represented: the first corner of each
# vertex wins.

import os

import bpy

from fc2fmt.bundle import EXTENSION, Bundle
from fc2fmt.encode import Layout, encode
from fc2fmt.geometry import read_lod, write_lod
from fc2fmt.transform import apply
from fc2fmt.xbg import (BONE_WTS1, BONE_WTS2, COLOR, EMPTY_SLOT, NORMAL, UV0, UV1,
                        XbgFile)

from . import convert
from .import_xbg import ATTR_NORMAL, PROP_LOD, PROP_SOURCE, PROP_SUBMESH


def source_model(collection):
    """Reopen the file this collection was imported from."""
    origin = collection.get(PROP_SOURCE)
    if not origin:
        raise ValueError("%s was not imported by this add-on" % collection.name)
    if not os.path.exists(origin):
        raise ValueError("the source %s is gone; re-import it" % origin)
    if origin.lower().endswith(EXTENSION):
        bundle = Bundle.load(origin)
        return XbgFile.parse(bundle.read(bundle.model))
    return XbgFile.parse(open(origin, "rb").read())


def save(path, collection, recompute_tangents=False):
    """Rebuild the collection's LOD in its source model and write it out."""
    model = source_model(collection)
    lod_index = collection.get(PROP_LOD, 0)
    lod = model.lods[lod_index]
    geometries = read_lod(model, lod)
    layout = Layout.of(model)

    resized = edited = moved = 0
    for obj in collection.objects:
        if obj.type != "MESH" or PROP_SUBMESH not in obj:
            continue
        position = obj[PROP_SUBMESH]
        if not 0 <= position < len(geometries):
            raise ValueError("%s names submesh %d, the LOD has %d"
                             % (obj.name, position, len(geometries)))
        # _encode edits the geometry in place, so what it replaces is captured first.
        before = (len(geometries[position].vertices),
                  geometries[position].vertices.pack(),
                  list(geometries[position].indices))
        after = _encode(obj, model, geometries[position], layout, recompute_tangents)
        geometries[position] = after
        edited += 1
        resized += len(after.vertices) != before[0]
        moved += (after.vertices.pack(), after.indices) != before[1:]

    write_lod(model, lod, geometries)
    # Refitting is skipped when nothing moved, because the shipped sphere is a
    # tighter fit than this can reproduce and rewriting it would lose bytes.
    if moved:
        _refit_bounds(model)
    with open(path, "wb") as handle:
        handle.write(model.write())
    return {"model": model, "parts": edited, "resized": resized, "moved": moved,
            "lod": lod_index}


def _refit_bounds(model):
    """Refit every part's sphere and box, and the model's own, around what is
    now drawn. Culling reads these, so stale ones make a part vanish."""
    points = {}
    for lod in model.lods:
        for geometry in read_lod(model, lod):
            if geometry.face_count:
                points.setdefault(geometry.part, []).extend(
                    geometry.vertices.positions(model.pos_scale))

    placed = []
    for index, part in points.items():
        desc = model.skin_descs[index]
        desc.set_bounds(part)
        placement = model.part_placement(desc.name)
        placed.extend(apply(placement, p) if placement else p for p in part)
    if placed:
        model.set_bounds(placed)


def _encode(obj, model, geometry, layout, recompute_tangents):
    """One object's geometry, packed into the layout its buffer already uses."""
    mesh = obj.data
    count = len(mesh.vertices)
    flags = geometry.vertices.flags
    # A part that kept its vertex count inherits everything not written here,
    # which is what lets an untouched export match the source byte for byte.
    template = geometry.vertices if count == len(geometry.vertices) else None
    frame = _tangent_frame(mesh, count) if template is None or recompute_tangents else None

    geometry.vertices = encode(
        flags, count, layout, template,
        positions=[tuple(v.co) for v in mesh.vertices],
        uvs=_per_vertex_uv(mesh, 0, count) if flags & UV0 else None,
        uvs1=_per_vertex_uv(mesh, 1, count) if flags & UV1 else None,
        normals=_normals(mesh, count) if flags & NORMAL else None,
        tangents=frame[0] if frame else None,
        binormals=frame[1] if frame else None,
        colours=_colours(mesh, count) if flags & COLOR else None,
        skin=_skin(obj, model, geometry, flags) if flags & BONE_WTS1 else None)
    geometry.indices = [i for triangle in _triangles(mesh)
                        for i in convert.triangle(triangle)]
    return geometry


def _tangent_frame(mesh, count):
    """Blender's tangent frame, which agrees with the one the file ships.

    Measured against retail geometry, its tangents sit within 0.9 dot of the
    stored ones for 89 to 96 percent of vertices, the rest being seam and
    smoothing differences that any regeneration produces.
    """
    if not mesh.uv_layers:
        return None
    mesh.calc_tangents(uvmap=mesh.uv_layers[0].name)
    tangents = _first_corner(mesh, count, lambda i: tuple(mesh.loops[i].tangent))
    binormals = _first_corner(mesh, count, lambda i: tuple(mesh.loops[i].bitangent))
    fallback = (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)
    return ([t or fallback[0] for t in tangents],
            [b or fallback[1] for b in binormals])


def _triangles(mesh):
    if all(len(polygon.vertices) == 3 for polygon in mesh.polygons):
        return [tuple(polygon.vertices) for polygon in mesh.polygons]
    mesh.calc_loop_triangles()
    return [tuple(triangle.vertices) for triangle in mesh.loop_triangles]


def _first_corner(mesh, count, read):
    """Per-vertex values from the first corner that touches each vertex."""
    out = [None] * count
    for loop in mesh.loops:
        if out[loop.vertex_index] is None:
            out[loop.vertex_index] = read(loop.index)
    return out


def _per_vertex_uv(mesh, channel, count):
    if channel >= len(mesh.uv_layers):
        return None
    layer = mesh.uv_layers[channel]
    values = _first_corner(mesh, count, lambda index: tuple(layer.data[index].uv))
    return [v if v is not None else (0.0, 0.0) for v in values]


def _normals(mesh, count):
    """The file's own normals when they are still attached, else Blender's.

    Blender normalises the normals it shades with and the file's are not unit
    length, so re-encoding those would move roughly half of them by one step.
    """
    stored = mesh.attributes.get(ATTR_NORMAL)
    if stored is not None and len(stored.data) == count:
        return [tuple(item.vector) for item in stored.data]
    values = _first_corner(mesh, count,
                           lambda index: tuple(mesh.corner_normals[index].vector))
    return [v if v is not None else (0.0, 0.0, 1.0) for v in values]


def _colours(mesh, count):
    if not mesh.color_attributes:
        return None
    data = mesh.color_attributes[0].data
    if len(data) == count:
        return [tuple(item.color) for item in data]
    return _first_corner(mesh, count, lambda index: tuple(data[index].color))


def _skin(obj, model, geometry, flags):
    """Vertex groups back into (weight, palette slot) pairs.

    The buffer has four slots per weight set, so a vertex with more influences
    than that keeps its heaviest — which is what the palette can address.
    """
    cluster = model.skin_descs[geometry.part].clusters[geometry.cluster]
    slot_of = {}
    for slot, node in enumerate(cluster.palette):
        if node != EMPTY_SLOT and node < len(model.nodes):
            slot_of.setdefault(model.nodes[node].name, slot)

    limit = 8 if flags & BONE_WTS2 else 4
    names = {group.index: group.name for group in obj.vertex_groups}
    out = []
    for vertex in obj.data.vertices:
        pairs = []
        for item in vertex.groups:
            if not item.weight:
                continue
            name = names.get(item.group)
            if name not in slot_of:
                raise ValueError("%s: vertex group %r is not in the part's bone palette"
                                 % (obj.name, name))
            pairs.append((item.weight, slot_of[name]))
        out.append(sorted(pairs, key=lambda pair: -pair[0])[:limit])
    return out


def collection_of(context):
    """The collection to export: the active one, or the only imported one."""
    active = context.view_layer.active_layer_collection
    if active and PROP_SOURCE in active.collection:
        return active.collection
    imported = [c for c in bpy.data.collections if PROP_SOURCE in c]
    if len(imported) == 1:
        return imported[0]
    raise ValueError("select the collection to export; found %d" % len(imported))
