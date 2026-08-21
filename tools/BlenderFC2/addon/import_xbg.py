# Build Blender objects from an .xbg, and an armature from its nodes.
#
# One object per drawable part, named <PART>_STATEnn_LODk as the file names it,
# so an exporter can find the parts again. Rigid parts are parented to the node
# that placed them; skinned parts get vertex groups from their bone palette.

import os

import bpy

from fc2fmt.mesh import extract, part_name
from fc2fmt.skeleton import SkeletonFile
from fc2fmt.xbg import EMPTY_SLOT, XbgFile

from . import convert

# Custom properties carried so an export can rebuild what it did not author.
PROP_NODE_HASH = "fc2_node_hash"
PROP_SKIN_INDEX = "fc2_skin_index"
PROP_EXTENT = "fc2_extent"
PROP_PART = "fc2_part"
PROP_LOD = "fc2_lod"
PROP_MATERIAL = "fc2_material"


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
        bone = data.edit_bones.new(node.name or "node_%d" % index)
        bone.head = heads[index]
        children = [heads[c] for c, other in enumerate(model.nodes) if other.parent == index]
        bone.tail = convert.bone_tail(bone.head, children)
        edit_bones.append(bone)
    for index, node in enumerate(model.nodes):
        if node.parent < len(edit_bones):
            edit_bones[index].parent = edit_bones[node.parent]
            edit_bones[index].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = previous

    for index, node in enumerate(model.nodes):
        bone = data.bones.get(node.name or "node_%d" % index)
        if bone:
            # A CRC32 overflows Blender's signed 32-bit custom int, so keep hex.
            bone[PROP_NODE_HASH] = "%08x" % node.name_hash
            bone[PROP_SKIN_INDEX] = node.skin_index
            bone[PROP_EXTENT] = node.extent
    return obj


def _material(cache, path):
    if path not in cache:
        material = bpy.data.materials.new(os.path.basename(path.replace("\\", "/")) or "material")
        material[PROP_MATERIAL] = path
        material.use_nodes = True
        cache[path] = material
    return cache[path]


def build_part(part, model, collection, materials, armature):
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

    mesh.materials.append(_material(materials, part.material))
    obj = bpy.data.objects.new(part.full_name, mesh)
    obj[PROP_PART] = part.name
    obj[PROP_LOD] = part.lod
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
        node = _folded(model, part.name)
        if node is not None:
            obj.parent_type = "BONE"
            obj.parent_bone = node.name
    if part.placement is not None:
        obj.matrix_world = convert.matrix(part.placement)
    return obj


def _folded(model, name):
    """Part names are upper-cased against mixed-case node names."""
    wanted = name.lower()
    return next((n for n in model.nodes if n.name.lower() == wanted), None)


def load(path, lod=0, with_armature=True):
    """Import one .xbg, plus its sibling _ref.skeleton when one is present."""
    model = XbgFile.parse(open(path, "rb").read())
    name = os.path.splitext(os.path.basename(path))[0]
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)

    armature = build_armature(model, name, collection) if with_armature else None
    materials = {}
    # Without an armature there is nothing to parent to, so bake the placement
    # into the vertices instead.
    parts = [build_part(part, model, collection, materials, armature)
             for part in extract(model, lod, place=armature is None)]
    return {"model": model, "collection": collection, "armature": armature, "parts": parts}


def load_skeleton(path):
    return SkeletonFile.parse(open(path, "rb").read())
