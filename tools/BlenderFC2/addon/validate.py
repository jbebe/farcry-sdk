# Run the rules against a Blender scene.
#
# This is the only file that reads bpy on behalf of `rules.py`, which keeps the
# rules themselves testable and free of scene vocabulary.
#
# Everything runs through `export_xbg.build_mesh`, so the geometry a rule sees is
# the geometry export would write - a rule that fired on something export would
# not write, or missed something it would, is worse than no rule. That costs the
# same as an export, and it regenerates tangents, so this is not as cheap as it
# looks and is deliberately on demand rather than on every redraw.

import bpy
import numpy

from . import export_xbg, material_data, materials, rules, textures
from .import_xbg import PROP_MATERIAL_PATH, PROP_PART
from .rules import ERROR, WARNING, Finding, Target


def check_scene(context):
    """Every finding for the model in this context, worst first."""
    # Object transforms are evaluated lazily, and one of the rules is about an
    # object having been moved - reading a stale matrix would make it silent
    # exactly when it has something to say.
    context.view_layer.update()

    try:
        collection = export_xbg.collection_of(context)
    except ValueError as error:
        return [Finding(
            ERROR, "pack.ambiguous-collection", str(error), Target(kind="scene"),
            "Make the model's collection active in the Outliner.")]

    try:
        built = export_xbg.build_mesh(collection)
    except ValueError as error:
        # A raise from the encoder is a finding, not a crash: the commonest one
        # is a vertex group naming a bone outside the part's palette, which is
        # exactly what a modeler needs told rather than shown as a traceback.
        return [Finding(
            ERROR, "part.not-encodable", str(error), Target(kind="scene"),
            "Fix it and check again; export would refuse for the same reason.")]

    pack, mesh = built["pack"], built["mesh"]
    limits = pack.limits
    objects = _objects(built)

    found = rules.check_scene(collection, objects, limits)
    found += rules.check_geometry(mesh, _entries(objects, mesh, built["lod"]), limits)
    found += _meshes(objects, mesh, built["lod"], pack)
    found += _materials(collection, pack)
    found += _textures(collection, pack)
    found += _placement(objects)
    found += _added_to_one_lod(built)
    return sorted(found, key=lambda f: (f.severity != ERROR, f.code))


def _added_to_one_lod(built):
    """A part added to the open LOD is drawn at that distance and no other."""
    if not built["added"] or len(built["mesh"]["lods"]) < 2:
        return []
    return [Finding(
        WARNING, "part.added-one-lod",
        "%d added part(s) exist only at LOD%d, so they vanish when the model drops to a coarser "
        "one." % (built["added"], built["lod"]),
        Target(kind="scene"),
        "Nothing in the format forbids it. Import the other LODs and add the part there too if it "
        "should stay visible.")]


def blocking(found):
    return [finding for finding in found if finding.severity == ERROR]


def _objects(built):
    """Every mesh object export wrote, with the part it wrote to.

    Taken from the export itself rather than re-derived, so a part export
    appended is covered by the same rules as one it edited.
    """
    return [(obj, obj.get(PROP_PART, obj.name), submesh)
            for obj, submesh in built["written"]]


def _entries(objects, mesh, lod):
    geometry = mesh["lods"][lod]["geometry"]
    return [(obj.name, submesh, geometry[submesh]) for obj, _name, submesh in objects]


def _meshes(objects, mesh, lod, pack):
    """The per-object rules, over the scene data each one needs."""
    geometry = mesh["lods"][lod]["geometry"]
    out = []
    for obj, _name, submesh in objects:
        entry = geometry[submesh]
        out += rules.check_uvs(
            obj.name, submesh,
            bool(entry.get("uvs")),
            bool(obj.data.uv_layers),
            _uvs_at_origin(obj.data))
        out += rules.check_mesh(

            obj.name, submesh,
            _split_corners(obj.data),
            _groups(obj, entry),
            _foreign_material(obj),
            _part_material(mesh, entry))
        out += rules.check_loose(obj.name, submesh, _loose(obj.data))
    return out



def _uvs_at_origin(data):
    """Whether every UV sits at (0, 0), which is a layer nobody has unwrapped."""
    if not data.uv_layers:
        return False
    layer = data.uv_layers[0]
    return bool(len(layer.data)) and all(
        abs(corner.uv[0]) < 1e-6 and abs(corner.uv[1]) < 1e-6 for corner in layer.data)


def _split_corners(data):
    """Vertices whose corners disagree, per attribute the format stores per vertex.

    Returns (attribute, how many, the first) so a rule can report and select
    without knowing anything about Blender.
    """
    out = []
    for kind, split in (("uv", _uv_corners(data)),
                        ("normal", _normal_corners(data)),
                        ("colour", _colour_corners(data))):
        if split is None:
            continue
        indices = sorted(split)
        out.append((kind, len(indices), indices[0] if indices else -1))
    return out


def _uv_corners(data):
    """Vertices whose corners disagree about where they are in the texture."""
    if not data.uv_layers:
        return None
    layer = data.uv_layers[0]
    corners = {}
    for loop in data.loops:
        corners.setdefault(loop.vertex_index, set()).add(
            (round(layer.data[loop.index].uv[0], 5), round(layer.data[loop.index].uv[1], 5)))
    return {vertex for vertex, values in corners.items() if len(values) > 1}


def _normal_corners(data):
    """Vertices whose corners point meaningfully apart.

    Compared by angle, not by value. Blender normalises the normals it shades
    with and the file's are not unit length, so the corners of one vertex differ
    in the fifth decimal on every smooth mesh - 570 of the rifle's 805 vertices
    at four decimal places, and none of them a real seam.
    """
    corners = {}
    for loop in data.loops:
        corners.setdefault(loop.vertex_index, []).append(
            tuple(data.corner_normals[loop.index].vector))
    # About one degree. A real hard edge is tens of degrees apart.
    limit = 0.9998
    return {vertex: values for vertex, values in corners.items()
            if any(sum(a * b for a, b in zip(values[0], other)) < limit
                   for other in values[1:])}


def _colour_corners(data):
    if not data.color_attributes:
        return None
    attribute = data.color_attributes[0]
    if attribute.domain != "CORNER":
        return None
    corners = {}
    for loop in data.loops:
        corners.setdefault(loop.vertex_index, set()).add(
            tuple(round(c, 4) for c in attribute.data[loop.index].color))
    return {vertex for vertex, values in corners.items() if len(values) > 1}


def _loose(data):
    used = {index for polygon in data.polygons for index in polygon.vertices}
    return [index for index in range(len(data.vertices)) if index not in used]


def _groups(obj, geometry):
    """Per vertex: how many non-zero groups, their sum, and the buffer's width.

    Only for a part the format actually skins - a rigid one has no weights, and
    a stray vertex group on it changes nothing either way.
    """
    weights = geometry.get("skin_weights")
    if not weights or not obj.vertex_groups:
        return []
    limit = max(1, len(weights) // max(1, geometry["vertex_count"]))
    return [(vertex.index,
             sum(1 for item in vertex.groups if item.weight > 0.0),
             sum(item.weight for item in vertex.groups),
             limit)
            for vertex in obj.data.vertices]


def _foreign_material(obj):
    """The name of a slot material that names no game path, which export cannot write."""
    material = obj.data.materials[0] if obj.data.materials else None
    return material.name if material and not material.get(PROP_MATERIAL_PATH) else ""


def _part_material(mesh, geometry):
    """The `.xbm` the cluster's own material index names, by game path.

    Compared as paths, not as names: a material's internal name and the file it
    lives in are different things by design - the rifle's `AK47WOOD` sits in
    `jpcormier-m-2007100950589039.xbm` - so comparing those calls every shipped
    material reassigned.
    """
    part = mesh["parts"][geometry["part"]]
    index = part["clusters"][geometry["cluster"]]["material_index"]
    names = mesh["materials"]
    return names[index] if index < len(names) else ""


def _placement(objects):
    """An object moved in object mode, whose transform export silently discards.

    Export reads `mesh.vertices[i].co`, which is object-local, so moving,
    rotating or scaling a part in object mode changes nothing in the file. It is
    the purest "it looked right in Blender" failure, and it is invisible without
    the placement stashed at import.
    """
    from .import_xbg import PROP_PLACEMENT

    out = []
    for obj, name, submesh in objects:
        stashed = obj.get(PROP_PLACEMENT)
        if stashed is None:
            continue
        now = [c for row in obj.matrix_world for c in row]
        if max(abs(a - b) for a, b in zip(stashed, now)) <= 1e-5:
            continue
        out.append(Finding(
            ERROR, "object.moved",
            "'%s' has been moved in object mode, and export writes vertex positions "
            "only - the move would be silently discarded." % obj.name,
            Target(object=obj.name, kind="object", name=name, index=submesh),
            "Apply the transform (Object > Apply > All Transforms), or undo the move."))
    return out


def _materials(collection, pack):
    """Every material export would write, walked once."""
    return [finding for material in material_data.used(collection)
            for finding in _material(material, pack)]


def _textures(collection, pack):
    """The rules over every image export would write, and an edit to a shared one."""
    try:
        planned = textures.plan(pack, collection)
    except ValueError as error:
        return [Finding(ERROR, "texture.not-writable", str(error), Target(kind="scene"),
                        "Export would refuse for the same reason.")]
    out = []
    for slot, image, _path, entry in planned:
        if image.size[0]:
            out += rules.check_texture(
                image.name, slot, tuple(image.size), image.get(materials.PROP_IMAGE_SIZE),
                entry.record.get("codec") if entry is not None else None, _alpha_levels(image))
        # Only a shared one is encoded here: writing an owned one is what export is for.
        if entry is not None and not entry.owned \
                and textures.changed(pack, slot, image, entry) is not None:
            out += rules.check_edit(entry)
    return out


# How many texels decide how many distinct alpha values an image holds.
ALPHA_SAMPLES = 4096


def _alpha_levels(image):
    """Roughly how many distinct alpha values an image holds, or None.

    Three is the interesting threshold - fully on, fully off, and anything between - so a
    sample that misses a rare soft texel costs a warning nobody needed.
    """
    found = textures.alpha(image)
    if found is None or not len(found):
        return None
    return len(numpy.unique(numpy.round(found[::max(1, len(found) // ALPHA_SAMPLES)], 2)))

def _material(material, pack):
    path = material.get(PROP_MATERIAL_PATH, "")
    original = pack.material(path) if path else None
    added = material_data.is_new(material)
    if original is None and not added:
        # A material an .xbg embeds has no file, so the pack cannot carry it -
        # four of the 7,496 shipped material names, none of them weapons.
        return []

    # The document export would write, so the rules judge what reaches the pack.
    definition = material_data.document(material) if material_data.held(material) else original
    entry = pack.entry(path)
    edited = original is not None and not material_data.same(definition, original)
    out = rules.check_edit(entry) if entry is not None and edited else []
    return out + rules.check_material(
        material.name,
        definition.get("shader", ""),
        materials.textures(definition),
        _driven(material),
        {key: tuple(value) for key, value in materials.floats(definition).items()
         if len(value) == 2},
        lambda texture: (pack.entry(texture) or entry) is not None
        and (pack.entry(texture).owned if pack.entry(texture) else False))


def _driven(material):
    """Which Principled inputs the *modeler* wired something into.

    The importer drives some of these itself - a specular map lands on Roughness
    and a normal map on Normal - so a bare "is this linked" test reports every
    shipped material as using a channel the format does not carry. Every node
    the importer makes is tagged with the slot it stands for, so a chain with an
    untagged image in it is one somebody changed, and that is what a warning is
    about.

    Metallic is also reported when it merely carries a non-zero value, because
    that is how an imported PBR material arrives and it looks like nothing in
    the viewport.
    """
    driven = {}
    if not material.use_nodes:
        return driven

    principled = next((node for node in material.node_tree.nodes
                       if node.type == "BSDF_PRINCIPLED"), None)
    if principled is None:
        return driven

    for socket in principled.inputs:
        if socket.is_linked and _hand_wired(socket):
            driven[socket.name] = True
    if not driven.get("Metallic") and _scalar(principled, "Metallic") > 0.0:
        driven["Metallic"] = True
    return driven


def _hand_wired(socket, limit=64):
    """Whether this socket's chain is one the importer did not build.

    True when an image upstream carries no slot tag, and true when there is no
    image upstream at all - the importer only ever drives a Principled input
    from a tagged texture, so a bare colour or a maths chain is the modeler's.
    """
    seen = set()
    images = 0
    stack = [link.from_node for link in socket.links]
    while stack and len(seen) < limit:
        node = stack.pop()
        if node.name in seen:
            continue
        seen.add(node.name)
        if node.type == "TEX_IMAGE":
            images += 1
            if materials.PROP_SLOT not in node:
                return True
            continue
        for inner in node.inputs:
            stack.extend(link.from_node for link in inner.links)
    return images == 0


def _scalar(node, name):
    socket = node.inputs.get(name)
    try:
        return float(socket.default_value) if socket is not None else 0.0
    except TypeError:
        return 0.0
