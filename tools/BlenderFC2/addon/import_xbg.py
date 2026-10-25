# Build Blender objects from a .fc2model pack, and an armature from its nodes.
#
# One object per drawable part, named <PART>_STATEnn_LODk as the pack names it,
# so an exporter can find the parts again. Rigid parts are parented to the node
# that placed them; skinned parts get vertex groups from their bone palette.
#
# Nothing here reads a Dunia format. The pack arrives decoded - float positions
# in metres, UVs, normals, skin pairs and material documents - which is what
# lets this file be about Blender and nothing else.

import math

import bpy
from mathutils import Matrix, Vector

from . import convert, materials, model as fc2model, rig
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

# Set on an armature whose bones are their nodes turned by convert.BONE_AXES.
PROP_BONE_AXES = "fc2_bone_axes"


def build_armature(mesh, name, collection, skeleton=None):
    """An armature from the pack's nodes, in the file's own node order.

    A bone the skeleton names hangs off the skeleton's parent instead of the
    node's, since that is the tree its clips animate.
    """
    matrices = fc2model.node_world_matrices(mesh)
    data = bpy.data.armatures.new(name + "_rig")
    data[PROP_BONE_AXES] = True
    obj = bpy.data.objects.new(name + "_rig", data)
    collection.objects.link(obj)

    previous = bpy.context.view_layer.objects.active
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    heads = [convert.matrix(m).to_translation() for m in matrices]
    edit_bones = []
    for index, node in enumerate(mesh["nodes"]):
        bone = data.edit_bones.new(fc2model.bone_name(mesh, index))
        children = [heads[c] for c, other in enumerate(mesh["nodes"])
                    if other["parent"] == index]
        length = convert.bone_length(heads[index], children)
        bone.head = heads[index]
        bone.tail = heads[index] + Vector((0.0, length, 0.0))
        # The node turned to point down its X axis, at its child, keeping its twist.
        bone.matrix = convert.matrix(matrices[index]) @ convert.BONE_AXES
        bone.length = length
        edit_bones.append(bone)
    by_name = {bone.name: bone for bone in edit_bones}
    parents = rig.parents(skeleton) if skeleton else {}
    roots = {entry["name"] for entry in skeleton["bones"]} - set(parents) if skeleton else set()
    for bone, node in zip(edit_bones, mesh["nodes"]):
        parent = by_name.get(parents.get(bone.name))
        # A skeleton's root is posed in model space, whatever node the mesh hangs it off.
        if parent is None and bone.name not in roots and node["parent"] < len(edit_bones):
            parent = edit_bones[node["parent"]]
        if parent is not None:
            bone.parent = parent
            bone.use_connect = False
    if skeleton:
        entries = {entry["name"]: entry for entry in skeleton["bones"]}
        for bone in edit_bones:
            if bone.parent is None:
                _join_chain(bone, entries)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = previous

    for index, node in enumerate(mesh["nodes"]):
        bone = data.bones.get(fc2model.bone_name(mesh, index))
        if bone:
            bone[PROP_SKIN_INDEX] = node["skin_index"]
            bone[PROP_EXTENT] = node["extent"]
    # A move on a bone the rig leaves unmarked is one the writer drops, so the
    # viewport refuses it too.
    if skeleton:
        moves = {bone["name"] for bone in skeleton["bones"] if rig.translates(bone)}
        for pose_bone in obj.pose.bones:
            # Blender never moves a joined bone itself; Auto IK drags it.
            free = pose_bone.name in moves or pose_bone.bone.use_connect
            pose_bone.lock_location = (not free,) * 3
    return obj


# A joint further than this off its parent's axis is left loose rather than snapped.
ON_AXIS = 1e-5
# A child nearer than this sits on its parent's own joint rather than down the bone.
SHORTEST = 1e-3


def _join_chain(bone, entries):
    """Join `bone` to the child on its axis, for Auto IK, then do the same down the tree.

    A child a clip moves or the engine derives stays loose.
    """
    children = bone.children
    on_axis = []
    for child in children:
        entry = entries.get(child.name)
        if entry is None or rig.derived(entry) or rig.translates(entry):
            continue
        local = bone.matrix.inverted() @ child.head
        if local.y > SHORTEST and math.hypot(local.x, local.z) < ON_AXIS:
            on_axis.append((local.y, child))
    if on_axis:
        reach, child = max(on_axis, key=lambda pair: pair[0])
        bone.length = reach
        frame, length = child.matrix.copy(), child.length
        child.use_connect = True
        # Connecting leaves the child's tail behind; its own frame goes back on the joint.
        frame.translation = bone.tail
        child.matrix = frame
        child.length = length
    for child in children:
        _join_chain(child, entries)


def require_current(armature):
    """Refuse an armature built before bones were turned, which every clip would miss by a quarter turn."""
    if not armature.data.get(PROP_BONE_AXES):
        raise ValueError("%s was imported by an older version of the add-on. Import the "
                         "model again" % armature.name)


def attach(child, armature, bone_name):
    """Hang an object off a bone in its node's frame, where a clip or a part is placed.

    Blender parents to the tail, so the inverse cancels the bone's length and its turn.
    """
    require_current(armature)
    child.parent = armature
    child.parent_type = "BONE"
    child.parent_bone = bone_name
    child.matrix_parent_inverse = Matrix.Translation(
        (0.0, -armature.data.bones[bone_name].length, 0.0)) @ convert.BONE_AXES.inverted()


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
        node = part.placement_node
        if node < len(mesh["nodes"]):
            attach(obj, armature, fc2model.bone_name(mesh, node))
        else:
            obj.parent = armature
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

    armature = build_armature(mesh, name, collection, pack.rig()) if with_armature else None
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

    armature = build_armature(mesh, name, collection, pack.rig(actor=True))
    armature[PROP_ACTOR_OF] = pack.model
    cache = {}
    parts = [build_part(part, mesh, collection, cache, armature, None)
             for part in fc2model.parts_at(mesh, 0)]
    for obj in parts:
        obj[PROP_ACTOR_OF] = pack.model
    return {"mesh": mesh, "collection": collection, "armature": armature, "parts": parts}
