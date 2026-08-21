# Build Blender objects from an .xbg, and an armature from its nodes.
#
# One object per drawable part, named <PART>_STATEnn_LODk as the file names it,
# so an exporter can find the parts again. Rigid parts are parented to the node
# that placed them; skinned parts get vertex groups from their bone palette.

import os

import bpy

from fc2fmt import xbm
from fc2fmt.assets import find_root, install_assets
from fc2fmt.bundle import EXTENSION, Bundle
from fc2fmt.mesh import extract
from fc2fmt.xbg import EMPTY_SLOT, XbgFile

from . import convert, materials

# Custom properties carried so an export can rebuild what it did not author.
PROP_NODE_HASH = "fc2_node_hash"
PROP_SKIN_INDEX = "fc2_skin_index"
PROP_EXTENT = "fc2_extent"
PROP_PART = "fc2_part"
PROP_LOD = "fc2_lod"
PROP_MATERIAL = "fc2_material"
PROP_SOURCE = "fc2_source"
PROP_SUBMESH = "fc2_submesh"

# The file's normals are not unit length and Blender normalises the ones it
# shades with, so the originals ride along in their own attribute.
ATTR_NORMAL = "fc2_normal"


def build_armature(model, name, collection):
    """An armature from the EDON nodes, in the file's own node order."""
    matrices = model.node_world_matrices()
    data = bpy.data.armatures.new(name + "_rig")
    obj = bpy.data.objects.new(name + "_rig", data)
    collection.objects.link(obj)

    previous = bpy.context.view_layer.objects.active
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    heads = [convert.matrix(m).to_translation() for m in matrices]
    edit_bones = []
    for index, node in enumerate(model.nodes):
        bone = data.edit_bones.new(bone_name(model, index))
        bone.head = heads[index]
        children = [heads[c] for c, other in enumerate(model.nodes) if other.parent == index]
        bone.tail = convert.bone_tail(bone.head, children)
        # Orient the bone the way the node is, not at its children. The rest
        # pose then matches Dunia's, so a clip's local rotation drives the pose
        # bone directly; aiming bones at children would bake in a twist.
        length = bone.length
        bone.matrix = convert.matrix(matrices[index])
        bone.length = length
        edit_bones.append(bone)
    for index, node in enumerate(model.nodes):
        if node.parent < len(edit_bones):
            edit_bones[index].parent = edit_bones[node.parent]
            edit_bones[index].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = previous

    for index, node in enumerate(model.nodes):
        bone = data.bones.get(bone_name(model, index))
        if bone:
            # A CRC32 overflows Blender's signed 32-bit custom int, so keep hex.
            bone[PROP_NODE_HASH] = "%08x" % node.name_hash
            bone[PROP_SKIN_INDEX] = node.skin_index
            bone[PROP_EXTENT] = node.extent
    return obj


def _material(cache, path, model, source):
    if path not in cache:
        material = bpy.data.materials.new(os.path.basename(path.replace("\\", "/")) or "material")
        material[PROP_MATERIAL] = path
        material.use_nodes = True
        try:
            definition = xbm.resolve(path, model, source)
            if definition is not None:
                materials.build(material, definition, source, cache.setdefault("_images", {}))
        except Exception as error:
            print("fc2: material %s: %s" % (path, error))
        cache[path] = material
    return cache[path]


def build_part(part, model, collection, material_cache, armature, source):
    mesh = bpy.data.meshes.new(part.full_name)
    mesh.from_pydata(part.positions, [], [convert.triangle(t) for t in part.triangles])
    mesh.update()

    if part.uvs:
        layer = mesh.uv_layers.new(name="UVMap")
        for loop in mesh.loops:
            layer.data[loop.index].uv = part.uvs[loop.vertex_index]
    if part.uvs1:
        layer = mesh.uv_layers.new(name="UVMap1")
        for loop in mesh.loops:
            layer.data[loop.index].uv = part.uvs1[loop.vertex_index]
    if part.colours:
        layer = mesh.color_attributes.new(name="Colour", type="FLOAT_COLOR", domain="POINT")
        for index, colour in enumerate(part.colours):
            layer.data[index].color = colour
    if part.normals:
        mesh.normals_split_custom_set_from_vertices(part.normals)
        stored = mesh.attributes.new(name=ATTR_NORMAL, type="FLOAT_VECTOR", domain="POINT")
        stored.data.foreach_set("vector", [c for n in part.normals for c in n])

    mesh.materials.append(_material(material_cache, part.material, model, source))
    obj = bpy.data.objects.new(part.full_name, mesh)
    obj[PROP_PART] = part.name
    obj[PROP_LOD] = part.lod
    obj[PROP_SUBMESH] = part.submesh
    collection.objects.link(obj)

    slots = part.bone_slots()
    if slots and armature:
        groups = {}
        for vertex, pairs in enumerate(slots):
            for weight, node_index in pairs:
                if node_index == EMPTY_SLOT or node_index >= len(model.nodes):
                    continue
                name = model.nodes[node_index].name
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
        node = model.part_node(part.full_name)
        if node is not None:
            obj.parent_type = "BONE"
            obj.parent_bone = bone_name(model, node)
    if part.placement is not None:
        obj.matrix_world = convert.matrix(part.placement)
    return obj


def bone_name(model, index):
    """What build_armature called the bone for a node, named or not."""
    return model.nodes[index].name or "node_%d" % index


def load(path, lod=0, with_armature=True, with_textures=True, game_root=None):
    """Import one model, from a bundle or a loose .xbg beside its install."""
    if path.lower().endswith(EXTENSION):
        return load_bundle(path, lod, with_armature, with_textures)
    source = None
    if with_textures:
        root = game_root or find_root(path)
        source = install_assets(root) if root else None
    name = os.path.splitext(os.path.basename(path))[0]
    return build(open(path, "rb").read(), name, source, lod, with_armature, path)


def load_bundle(path, lod=0, with_armature=True, with_textures=True):
    """Import a .fc2model, which already carries every file the model needs."""
    bundle = Bundle.load(path)
    name = os.path.splitext(os.path.basename(bundle.model))[0]
    source = bundle if with_textures else None
    return build(bundle.read(bundle.model), name, source, lod, with_armature, path)


def build(data, name, source, lod=0, with_armature=True, origin=""):
    """Turn the bytes of one .xbg into Blender objects under a new collection."""
    model = XbgFile.parse(data)
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    # Export reopens this to edit the parts in place, leaving the rest alone.
    collection[PROP_SOURCE] = origin
    collection[PROP_LOD] = lod

    armature = build_armature(model, name, collection) if with_armature else None
    cache = {}
    # Without an armature there is nothing to parent to, so bake the placement
    # into the vertices instead.
    parts = [build_part(part, model, collection, cache, armature, source)
             for part in extract(model, lod, place=armature is None)]
    return {"model": model, "collection": collection, "armature": armature,
            "parts": parts, "source": source}
