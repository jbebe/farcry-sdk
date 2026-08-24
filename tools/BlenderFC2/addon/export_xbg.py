# Write a Blender scene back into the pack it was imported from.
#
# Export edits geometry in place inside the pack's mesh document, so nodes,
# materials, bone palettes, the LODs that were never imported and everything the
# document carries whole all survive untouched. Only the parts present are
# rebuilt, and only the entries actually changed grow an origin hash - which is
# what stops applying a pack from re-encoding a texture it never touched.
#
# There is no quantisation here. The document holds float positions in metres
# and JackAll packs them on the way back, which is why this file can be about
# Blender and nothing else.
#
# The format stores UVs, colours and normals per vertex, not per corner, so a
# split UV or a split normal cannot be represented: the first corner of each
# vertex wins.

import os
import struct

import bpy

from . import convert, model as fc2model
from .import_mab import PROP_PROP_OF
from .import_xbg import (
    ATTR_NORMAL, PROP_LOD, PROP_MATERIAL_PATH, PROP_NEW_PART, PROP_SOURCE, PROP_SUBMESH)
from .pack import EXTENSION, Pack
from .transform import apply

# The per-vertex channels a geometry entry can carry, which is what decides
# whether a new part's borrowed vertex format has been filled.
COMPONENTS = ("positions", "uvs", "uvs1", "normals", "tangents", "binormals", "colours")

def source_pack(collection):
    """Reopen the pack this collection was imported from."""
    origin = collection.get(PROP_SOURCE)
    if not origin:
        raise ValueError("%s was not imported by this add-on" % collection.name)
    if not origin.lower().endswith(EXTENSION):
        raise ValueError("%s was imported from %s, which is not a pack" % (collection.name, origin))
    if not os.path.exists(origin):
        raise ValueError("the source %s is gone; re-import it" % origin)
    return Pack.load(origin)


def build_mesh(collection, recompute_tangents=False):
    """The edited mesh document, and what changed in it.

    Split out from `save` so a validity check and an export run the same code:
    a rule that fired on something export would not write, or missed something
    it would, is worse than no rule.
    """
    pack = source_pack(collection)
    mesh = pack.mesh()
    lod_index = collection.get(PROP_LOD, 0)
    geometries = mesh["lods"][lod_index]["geometry"]

    written = []
    edited = resized = moved = added = 0
    for obj in collection.objects:
        if obj.type != "MESH" or PROP_SUBMESH not in obj:
            continue
        submesh = obj[PROP_SUBMESH]
        if not 0 <= submesh < len(geometries):
            raise ValueError("%s names submesh %d, the LOD has %d"
                             % (obj.name, submesh, len(geometries)))

        geometry = geometries[submesh]
        before = geometry["vertex_count"]
        moved += _encode(obj, mesh, geometry, recompute_tangents)
        edited += 1
        resized += geometry["vertex_count"] != before
        written.append((obj, submesh))

    # Sorted so two new parts land in the same order on every export, whatever
    # order the collection happens to hold them in.
    for obj in sorted(new_parts(collection), key=lambda o: o.name):
        geometry = _add_part(obj, mesh, geometries, lod_index)
        _encode(obj, mesh, geometry, recompute_tangents)
        _require_filled(obj, geometry)
        written.append((obj, len(geometries) - 1))
        added += 1

    # Refitting is skipped when nothing moved, because the shipped sphere is a
    # tighter fit than this can reproduce and rewriting it would lose bytes.
    if moved or added:
        refit_bounds(mesh)
    return {"pack": pack, "mesh": mesh, "parts": edited, "resized": resized,
            "moved": moved, "added": added, "written": written, "lod": lod_index}


def new_parts(collection):
    """Objects export will append rather than write into a part already there."""
    return [obj for obj in collection.objects
            if obj.type == "MESH" and PROP_SUBMESH not in obj and PROP_NEW_PART in obj]


def _add_part(obj, mesh, geometries, lod_index):
    """Give the document a part it did not have, drawn by one new cluster.

    The vertex format is taken from a rigid part already in this LOD rather than
    invented, so the new one is written in a layout the file already carries and
    `_encode` fills exactly the components that layout holds.
    """
    donor = _donor(geometries, obj)
    donor_part = mesh["parts"][donor["part"]]
    donor_cluster = donor_part["clusters"][donor["cluster"]]

    full_name = "%s_LOD%d" % (obj[PROP_NEW_PART], lod_index)
    if any(part["name"] == full_name for part in mesh["parts"]):
        raise ValueError("%s would add a second part called %s, and the engine tells parts apart "
                         "by a hash of that name" % (obj.name, full_name))

    index = len(mesh["parts"])
    mesh["parts"].append({
        "name": full_name,
        "lod_metric": donor_part["lod_metric"],
        "bounds": [0.0] * 10,
        "placement_node": _placement_node(obj, mesh),
        "clusters": [{
            "material_index": _material_index(obj, mesh),
            "flags": donor_cluster["flags"],
            "palette": list(donor_cluster["palette"]),
        }],
    })

    fresh = {"buffer": donor["buffer"], "part": index, "cluster": 0,
             "vertex_count": 0, "indices": []}
    for key in COMPONENTS:
        if key in donor:
            fresh[key] = []
    geometries.append(fresh)
    return fresh


def _require_filled(obj, geometry):
    """Every channel the borrowed format carries has to have been supplied.

    An edited part keeps whatever a channel Blender cannot supply already held.
    A new one has nothing to keep, and JackAll fills what it is not given with
    defaults - flat UVs, white, straight up - so an unwrapped part would go out
    silently wrong rather than refused.
    """
    for key in COMPONENTS:
        if key in geometry and not geometry[key]:
            raise ValueError("%s has no %s, which the part it takes its vertex format from carries"
                             % (obj.name, key))


def _donor(geometries, obj):
    """A rigid part in this LOD, whose vertex format the new one borrows."""
    for geometry in geometries:
        if "skin_weights" not in geometry:
            return geometry
    raise ValueError("%s cannot be added: every part in this LOD is skinned, and a new part "
                     "is written rigid" % obj.name)


def _placement_node(obj, mesh):
    """The node a new part hangs on, which is the bone it is parented to."""
    if obj.parent_type != "BONE" or not obj.parent_bone:
        return fc2model.NO_PLACEMENT
    for index in range(len(mesh["nodes"])):
        if fc2model.bone_name(mesh, index) == obj.parent_bone:
            return index
    raise ValueError("%s hangs on %r, which is not one of this model's nodes"
                     % (obj.name, obj.parent_bone))


def _material_index(obj, mesh):
    """Which of the model's materials the object's first slot names.

    The same slot `validate` reads, so the material a rule checks is the one
    export writes.
    """
    material = obj.data.materials[0] if obj.data.materials else None
    if material is None:
        raise ValueError("%s carries no material, so nothing says how to draw it" % obj.name)
    try:
        return mesh["materials"].index(material.get(PROP_MATERIAL_PATH, ""))
    except ValueError:
        raise ValueError("%s uses %r, which is not one of this model's materials"
                         % (obj.name, material.name)) from None


def save(path, collection, recompute_tangents=False):
    """Rebuild the collection's LOD in its source pack and write the pack out."""
    built = build_mesh(collection, recompute_tangents)
    pack = built["pack"]
    if built["moved"] or built["resized"] or built["added"]:
        pack.replace_document(pack.model, built["mesh"])
    pack.save(path)
    return built


def refit_bounds(mesh):
    """Refit every part's sphere and box, and the model's own, around what is
    now drawn. Culling reads these, so stale ones make a part vanish."""
    world = fc2model.node_world_matrices(mesh)
    points = {}
    for lod in mesh["lods"]:
        for geometry in lod["geometry"]:
            if geometry["indices"]:
                points.setdefault(geometry["part"], []).extend(
                    _triples(geometry["positions"]))

    placed = []
    for index, part_points in points.items():
        part = mesh["parts"][index]
        part["bounds"] = _bounds(part_points)
        placement = fc2model.part_placement(mesh, part, world)
        placed.extend(apply(placement, p) if placement else p for p in part_points)
    if placed:
        # The shipped sphere is a fitted one, tighter than the box allows and
        # centred off the box centre in 94% of models, so this does not
        # reproduce it. What it writes encloses every vertex, which is what
        # culling needs.
        fitted = _bounds(placed)
        mesh["sphere"] = fitted[:4]
        mesh["box"] = fitted[4:]


def _bounds(points):
    """A part's ten floats: sphere centre and radius, then the box."""
    low = [min(p[a] for p in points) for a in range(3)]
    high = [max(p[a] for p in points) for a in range(3)]
    centre = [(low[a] + high[a]) / 2.0 for a in range(3)]
    radius = max(_distance(p, centre) for p in points)
    return centre + [radius] + low + high


def _distance(a, b):
    return sum((a[i] - b[i]) ** 2 for i in range(3)) ** 0.5


def _triples(values):
    return [tuple(values[at:at + 3]) for at in range(0, len(values), 3)]


def _encode(obj, mesh, geometry, recompute_tangents):
    """One object's geometry, written into the document's flat arrays.

    Which components to write is decided by which ones the geometry already
    holds, not by a copy of the container's format flags. A buffer's layout is
    fixed and this cannot widen it, so presence says the same thing the flags
    would - without a second place for the bit values to be wrong.
    """
    data = obj.data
    count = len(data.vertices)
    # A part that kept its vertex count keeps whatever this does not rewrite,
    # which is what lets an untouched export match the source byte for byte.
    same_size = count == geometry["vertex_count"]
    frame = _tangent_frame(data, count) if not same_size or recompute_tangents else None

    changed = not same_size
    geometry["vertex_count"] = count
    changed |= _put(geometry, "positions", _flat([tuple(v.co) for v in data.vertices]))
    changed |= _put(geometry, "uvs", _flat(_per_vertex_uv(data, 0, count)))
    changed |= _put(geometry, "uvs1", _flat(_per_vertex_uv(data, 1, count)))
    changed |= _put(geometry, "normals", _flat(_normals(data, count)))
    changed |= _put(geometry, "colours", _flat(_colours(data, count)))
    if frame:
        changed |= _put(geometry, "tangents", _flat(frame[0]))
        changed |= _put(geometry, "binormals", _flat(frame[1]))
    changed |= _skin(obj, mesh, geometry)

    indices = [i for triangle in _triangles(data) for i in convert.triangle(triangle)]
    changed |= indices != geometry["indices"]
    geometry["indices"] = indices
    return changed


def _put(geometry, key, values):
    """Write one component back, and say whether it actually moved.

    A component the geometry does not carry is left absent even when Blender
    could supply one: the buffer's layout is fixed, so an extra UV set has
    nowhere to go and inventing an array would have JackAll pack it into a
    buffer with no room for it.

    The comparison rounds to float32 first. The pack's numbers were written as
    the shortest decimal that round-trips through a float, and Blender hands
    back the float widened to a double - the same value, spelled differently, so
    comparing the doubles calls every vertex moved.
    """
    if key not in geometry:
        return False
    if values is None:
        return False
    if _same(geometry[key], values):
        return False
    geometry[key] = values
    return True


def _same(before, after):
    if len(before) != len(after):
        return False
    return all(_f32(a) == _f32(b) for a, b in zip(before, after))


def _f32(value):
    """A number as the float32 the file will hold, widened back to a double."""
    return struct.unpack("<f", struct.pack("<f", value))[0]


def _flat(points):
    return [c for point in points for c in point] if points else None


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


def _skin(obj, mesh, geometry):
    """Vertex groups back into the document's weight and slot arrays.

    The width is the one the geometry already holds, so a vertex with more
    influences than the buffer addresses keeps its heaviest, and every vertex is
    padded back out to it - the arrays are flat with a fixed stride, and a
    ragged one has none.
    """
    if "skin_weights" not in geometry or "skin_slots" not in geometry:
        return False

    part = mesh["parts"][geometry["part"]]
    palette = part["clusters"][geometry["cluster"]]["palette"]
    nodes = mesh["nodes"]
    slot_of = {}
    for slot, node in enumerate(palette):
        if 0 <= node < len(nodes):
            slot_of.setdefault(fc2model.bone_name(mesh, node), slot)

    limit = max(1, len(geometry["skin_weights"]) // max(1, geometry["vertex_count"]))
    names = {group.index: group.name for group in obj.vertex_groups}
    weights, slots = [], []
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
        pairs = sorted(pairs, key=lambda pair: -pair[0])[:limit]
        pairs += [(0.0, 0)] * (limit - len(pairs))
        weights.extend(weight for weight, _slot in pairs)
        slots.extend(slot for _weight, slot in pairs)

    changed = _put(geometry, "skin_weights", weights)
    changed |= geometry["skin_slots"] != slots
    geometry["skin_slots"] = slots
    return changed


def collection_of(context):
    """The collection to export: the active one, or the only imported one.

    A prop a clip attached is sourced too, so it is skipped here - otherwise
    loading any clip with props makes every later export ask which of two
    collections was meant.
    """
    active = context.view_layer.active_layer_collection
    if active and _is_model(active.collection):
        return active.collection
    imported = [c for c in bpy.data.collections if _is_model(c)]
    if len(imported) == 1:
        return imported[0]
    raise ValueError("select the collection to export; found %d" % len(imported))


def _is_model(collection):
    return PROP_SOURCE in collection and PROP_PROP_OF not in collection
