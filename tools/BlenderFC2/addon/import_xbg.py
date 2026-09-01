# Build Blender objects from a .fc2model pack, and an armature from its nodes.
#
# One object per drawable part, named <PART>_STATEnn_LODk as the pack names it,
# so an exporter can find the parts again. Rigid parts are parented to the node
# that placed them; skinned parts get vertex groups from their bone palette.
#
# Nothing here reads a Dunia format. The pack arrives decoded - float positions
# in metres, UVs, normals, skin pairs and material documents - which is what
# lets this file be about Blender and nothing else.

import bpy

from . import convert, materials, model as fc2model
from .pack import EXTENSION, Pack, stem

# Custom properties carried so an export can rebuild what it did not author.
PROP_SKIN_INDEX = "fc2_skin_index"
PROP_EXTENT = "fc2_extent"
PROP_PART = "fc2_part"
PROP_LOD = "fc2_lod"
PROP_SOURCE = "fc2_source"
PROP_SUBMESH = "fc2_submesh"

# A part's base name on an object the document does not have yet, which export
# appends. Deliberately not turned into a PROP_SUBMESH once written: export
# always reopens the pristine source pack, so an index stamped here would name a
# part that pack never had.
PROP_NEW_PART = "fc2_new_part"

# Marks the body a pack carries for its clips to pose, so a check or an export
# can tell it apart from the model those are about.
PROP_ACTOR_OF = "fc2_actor_of"

# The material's game path, which is its identity in the pack. Kept apart from
# the material's own internal name (see materials.PROP_MATERIAL_NAME) - one file
# used to carry both, so resolving textures overwrote the path with the name.
PROP_MATERIAL_PATH = "fc2_material_path"

# Where a part was placed at import, so an export can tell that the modeler
# moved the object rather than its vertices - which export silently discards.
PROP_PLACEMENT = "fc2_placement"

# The file's normals are not unit length and Blender normalises the ones it
# shades with, so the originals ride along in their own attribute.
ATTR_NORMAL = "fc2_normal"


def build_armature(mesh, name, collection):
    """An armature from the pack's nodes, in the file's own node order."""
    matrices = fc2model.node_world_matrices(mesh)
    data = bpy.data.armatures.new(name + "_rig")
    obj = bpy.data.objects.new(name + "_rig", data)
    collection.objects.link(obj)

    previous = bpy.context.view_layer.objects.active
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    heads = [convert.matrix(m).to_translation() for m in matrices]
    edit_bones = []
    for index, node in enumerate(mesh["nodes"]):
        bone = data.edit_bones.new(fc2model.bone_name(mesh, index))
        bone.head = heads[index]
        children = [heads[c] for c, other in enumerate(mesh["nodes"])
                    if other["parent"] == index]
        bone.tail = convert.bone_tail(bone.head, children)
        # Orient the bone the way the node is, not at its children. The rest
        # pose then matches Dunia's, so a clip's local rotation drives the pose
        # bone directly; aiming bones at children would bake in a twist.
        length = bone.length
        bone.matrix = convert.matrix(matrices[index])
        bone.length = length
        edit_bones.append(bone)
    for index, node in enumerate(mesh["nodes"]):
        if node["parent"] < len(edit_bones):
            edit_bones[index].parent = edit_bones[node["parent"]]
            edit_bones[index].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = previous

    for index, node in enumerate(mesh["nodes"]):
        bone = data.bones.get(fc2model.bone_name(mesh, index))
        if bone:
            bone[PROP_SKIN_INDEX] = node["skin_index"]
            bone[PROP_EXTENT] = node["extent"]
    return obj


def _material(cache, path, pack):
    if path not in cache:
        material = bpy.data.materials.new(stem(path) or "material")
        material[PROP_MATERIAL_PATH] = path
        material.use_nodes = True
        definition = pack.material(path) if pack else None
        if definition is not None:
            try:
                materials.build(material, definition, pack, cache.setdefault("_images", {}))
            except Exception as error:
                print("fc2: material %s: %s" % (path, error))
        cache[path] = material
    return cache[path]


def build_part(part, mesh, collection, material_cache, armature, pack):
    data = bpy.data.meshes.new(part.full_name)
    data.from_pydata(part.positions, [], [convert.triangle(t) for t in part.triangles])
    data.update()

    if part.uvs:
        layer = data.uv_layers.new(name="UVMap")
        for loop in data.loops:
            layer.data[loop.index].uv = part.uvs[loop.vertex_index]
    if part.uvs1:
        layer = data.uv_layers.new(name="UVMap1")
        for loop in data.loops:
            layer.data[loop.index].uv = part.uvs1[loop.vertex_index]
    if part.colours:
        layer = data.color_attributes.new(name="Colour", type="FLOAT_COLOR", domain="POINT")
        for index, colour in enumerate(part.colours):
            layer.data[index].color = colour
    if part.normals:
        data.normals_split_custom_set_from_vertices(part.normals)
        stored = data.attributes.new(name=ATTR_NORMAL, type="FLOAT_VECTOR", domain="POINT")
        stored.data.foreach_set("vector", [c for n in part.normals for c in n])

    data.materials.append(_material(material_cache, part.material, pack))
    obj = bpy.data.objects.new(part.full_name, data)
    obj[PROP_PART] = part.name
    obj[PROP_LOD] = part.lod
    obj[PROP_SUBMESH] = part.submesh
    collection.objects.link(obj)

    slots = part.bone_slots()
    if slots and armature:
        groups = {}
        for vertex, pairs in enumerate(slots):
            for weight, name in pairs:
                if name not in groups:
                    groups[name] = obj.vertex_groups.new(name=name)
                groups[name].add([vertex], weight, "REPLACE")
        obj.modifiers.new(name="Armature", type="ARMATURE").object = armature
        obj.parent = armature
    elif armature:
        # A rigid part is modelled around its own pivot, so parent it to that
        # bone and let Blender place it. matrix_world is assigned after the
        # parent so Blender solves the local transform; baking the placement
        # into the vertices as well would move the part twice.
        obj.parent = armature
        node = part.placement_node
        if node < len(mesh["nodes"]):
            obj.parent_type = "BONE"
            obj.parent_bone = fc2model.bone_name(mesh, node)
    if part.placement is not None:
        obj.matrix_world = convert.matrix(part.placement)
    stamp_placement(obj)
    return obj


def stamp_placement(obj):
    """Record where the part landed, which is what lets a rule say it moved.

    Export reads mesh.vertices[i].co, which is object-local, so an object moved
    in object mode is silently discarded. Called after the placement is set, and
    by everything that places a part, or the rule compares against a stale one.
    """
    obj[PROP_PLACEMENT] = [c for row in obj.matrix_world for c in row]


def load(path, lod=0, with_armature=True, with_textures=True):
    """Import one model from a pack."""
    if not path.lower().endswith(EXTENSION):
        raise ValueError(
            "%s is not a %s. Export one with 'jackall-cli fc2model export', or from "
            "JackAll's Files tab." % (path, EXTENSION))
    pack = Pack.load(path)
    return build(pack, stem(pack.model), lod, with_armature, with_textures, path)


def build(pack, name, lod=0, with_armature=True, with_textures=True, origin=""):
    """Turn one pack's mesh into Blender objects under a new collection."""
    mesh = pack.mesh()
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    # Export reopens this to edit the parts in place, leaving the rest alone.
    collection[PROP_SOURCE] = origin
    collection[PROP_LOD] = lod

    armature = build_armature(mesh, name, collection) if with_armature else None
    cache = {}
    # Without an armature there is nothing to parent to, so bake the placement
    # into the vertices instead.
    parts = [build_part(part, mesh, collection, cache, armature,
                        pack if with_textures else None)
             for part in fc2model.parts_at(mesh, lod, place=armature is None)]
    built = {"pack": pack, "mesh": mesh, "collection": collection,
             "armature": armature, "parts": parts}
    if with_armature and pack.actor:
        built["actor"] = build_actor(pack)
    return built


def build_actor(pack):
    """The body a pack carries for its clips to pose, in a collection of its own.

    It travels without materials or textures, so its parts come back untextured.
    Kept out of the subject's collection: an export picks the collection it is
    given, and two models in one would make that ambiguous.
    """
    mesh = pack.mesh(actor=True)
    name = stem(pack.actor)
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    collection[PROP_ACTOR_OF] = pack.model

    armature = build_armature(mesh, name, collection)
    armature[PROP_ACTOR_OF] = pack.model
    cache = {}
    parts = [build_part(part, mesh, collection, cache, armature, None)
             for part in fc2model.parts_at(mesh, 0)]
    for obj in parts:
        obj[PROP_ACTOR_OF] = pack.model
    return {"mesh": mesh, "collection": collection, "armature": armature, "parts": parts}
