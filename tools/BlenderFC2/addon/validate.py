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

from . import export_xbg, materials, rules
from .import_xbg import ATTR_NORMAL, PROP_MATERIAL_PATH, PROP_PART, PROP_SUBMESH
from .rules import ERROR, Finding, Target


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
    objects = _objects(collection, mesh, built["lod"])

    found = rules.check_scene(collection, objects, limits)
    found += rules.check_geometry(mesh, _entries(objects, mesh, built["lod"]), limits)
    found += _meshes(objects, mesh, built["lod"], pack)
    found += _materials(collection, pack)
    found += _textures(collection, pack)
    found += _placement(objects)
    return sorted(found, key=lambda f: (f.severity != ERROR, f.code))


def blocking(found):
    return [finding for finding in found if finding.severity == ERROR]


def _objects(collection, mesh, lod):
    """Every mesh object export would write, with the part it writes to."""
    count = len(mesh["lods"][lod]["geometry"])
    out = []
    for obj in collection.objects:
        if obj.type != "MESH" or PROP_SUBMESH not in obj:
            continue
        submesh = obj[PROP_SUBMESH]
        if not 0 <= submesh < count:
            continue
        out.append((obj, obj.get(PROP_PART, obj.name), submesh))
    return out


def _entries(objects, mesh, lod):
    geometry = mesh["lods"][lod]["geometry"]
    return [(obj.name, submesh, geometry[submesh]) for obj, _name, submesh in objects]


def _meshes(objects, mesh, lod, pack):
    """The per-object rules, over the scene data each one needs."""
    geometry = mesh["lods"][lod]["geometry"]
    out = []
    for obj, _name, submesh in objects:
        entry = geometry[submesh]
        out += rules.check_mesh(
            obj.name, submesh,
            _split_corners(obj.data),
            _groups(obj, entry),
            _slot_material(obj, pack),
            _part_material(mesh, entry))
        out += rules.check_loose(obj.name, submesh, _loose(obj.data))
    return out


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


def _slot_material(obj, pack):
    """The `.xbm` the object's material slot points at, by game path."""
    material = obj.data.materials[0] if obj.data.materials else None
    return material.get(PROP_MATERIAL_PATH, "") if material else ""


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
    """Every material on an exported object, walked once."""
    out = []
    seen = set()
    for obj in collection.objects:
        if obj.type != "MESH":
            continue
        for material in obj.data.materials:
            if material is None or material.name in seen:
                continue
            seen.add(material.name)
            out += _material(material, pack)
    return out


def _textures(collection, pack):
    """Every image an exported material samples, walked once."""
    out = []
    seen = set()
    for obj in collection.objects:
        if obj.type != "MESH":
            continue
        for material in obj.data.materials:
            if material is None or not material.use_nodes:
                continue
            for node in material.node_tree.nodes:
                if node.type != "TEX_IMAGE" or node.image is None:
                    continue
                if node.image.name in seen:
                    continue
                seen.add(node.image.name)
                out += _texture(node, pack)
    return out


def _texture(node, pack):
    image = node.image
    if not tuple(image.size) or not image.size[0]:
        return []

    path = image.get(materials.PROP_IMAGE_PATH, "")
    entry = pack.entry(path) if path else None
    return rules.check_texture(
        image.name,
        node.get(materials.PROP_SLOT, ""),
        tuple(image.size),
        image.get(materials.PROP_IMAGE_SIZE),
        entry.record.get("codec") if entry is not None else None,
        _alpha_levels(image))


# How many pixels to look at when asking whether an image has soft alpha. A
# 2048-square image is sixteen million floats and reading all of them would make
# a check take longer than the export it is checking.
ALPHA_SAMPLES = 4096


def _alpha_levels(image):
    """Roughly how many distinct alpha values an image holds, or None.

    Sampled on a stride rather than read whole. Three is the interesting
    threshold - fully on, fully off, and anything between - so a sample that
    misses a rare soft texel costs a warning nobody needed.
    """
    if image.channels < 4:
        return None
    try:
        pixels = image.pixels
        count = len(pixels) // 4
        if count == 0:
            return None
        stride = max(1, count // ALPHA_SAMPLES)
        return len({round(pixels[index * 4 + 3], 2)
                    for index in range(0, count, stride)})
    except (RuntimeError, AttributeError):
        # An image with no pixel data loaded - a broken path, or one Blender has
        # freed - has nothing to say rather than something to complain about.
        return None


def _material(material, pack):
    path = material.get(PROP_MATERIAL_PATH, "")
    definition = pack.material(path) if path else None
    if definition is None:
        # A material an .xbg embeds has no file, so the pack cannot carry it -
        # four of the 7,496 shipped material names, none of them weapons.
        return []

    entry = pack.entry(path)
    out = rules.check_edit(entry) if entry is not None and _edited(material) else []
    return out + rules.check_material(
        material.name,
        definition.get("shader", ""),
        materials.textures(definition),
        _driven(material),
        {key: tuple(value) for key, value in materials.floats(definition).items()
         if len(value) == 2},
        lambda texture: (pack.entry(texture) or entry) is not None
        and (pack.entry(texture).owned if pack.entry(texture) else False))


def _edited(material):
    """Whether the shader graph was rebuilt rather than the one import wired.

    Import tags every node it makes with the slot it stands for, so a graph with
    an untagged image node in it is one a modeler changed.
    """
    if not material.use_nodes:
        return False
    return any(node.type == "TEX_IMAGE" and materials.PROP_SLOT not in node
               for node in material.node_tree.nodes)


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
