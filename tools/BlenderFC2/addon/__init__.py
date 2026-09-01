# Far Cry 2 model add-on: registration and the file menu entries.
#
# One import and one export, both of `.fc2model` - the decoded pack JackAll
# writes. Nothing here opens a game file, which is the whole arrangement: JackAll
# owns the byte layouts and this owns what a scene looks like.

import itertools
import os

import bpy
from bpy.props import (BoolProperty, EnumProperty, FloatProperty, IntProperty,
                       StringProperty)
from bpy_extras.io_utils import ExportHelper, ImportHelper

from mathutils import Matrix

from . import export_mab, export_xbg, import_mab, import_xbg, panel
from .pack import EXTENSION, Pack, read_manifest

# What the bone enum holds when a part is placed by nothing.
ROOT_BONE = "__root__"


class FC2_OT_import_pack(bpy.types.Operator, ImportHelper):
    """Import a model pack: its mesh, rig, materials and textures"""

    bl_idname = "import_scene.fc2_pack"
    bl_label = "Import Far Cry 2 Model Pack"
    bl_options = {"REGISTER", "UNDO"}

    filename_ext = EXTENSION
    filter_glob: StringProperty(default="*" + EXTENSION, options={"HIDDEN"})
    lod: IntProperty(name="LOD", default=0, min=0,
                     description="Which detail level to import")
    with_armature: BoolProperty(name="Build armature", default=True,
                                description="Create bones from the model's nodes")
    with_textures: BoolProperty(
        name="Load textures", default=True,
        description="Wire each material to the textures the pack carries")

    def execute(self, context):
        try:
            result = import_xbg.load(self.filepath, self.lod, self.with_armature,
                                     self.with_textures)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

        banks = len(result["pack"].clips)
        self.report({"INFO"}, "Imported %d parts%s" % (
            len(result["parts"]),
            "; %d animation bank(s) to load" % banks if banks else ""))
        return {"FINISHED"}


def _pack_of(context):
    """The pack the active object was imported from, and its collection."""
    obj = context.object
    for collection in itertools.chain(obj.users_collection if obj else (),
                                      bpy.data.collections):
        origin = collection.get(import_xbg.PROP_SOURCE)
        if origin:
            return origin, collection
    return None, None


_NO_PACK = [("", "No pack imported", "")]
_UNREADABLE = [("", "Could not read the pack", "")]
_NO_CLIPS = [("", "This pack carries no animation", "")]

# Each pack's bank list against the mtime it was read at. Blender runs an items
# callback on every redraw and keeps no reference to what it returns, so the
# list is built once and outlives the call.
_CLIPS = {}


def _clip_items(self, context):
    """The banks the pack carries, from its manifest alone."""
    origin, _collection = _pack_of(context)
    if not origin:
        return _NO_PACK
    try:
        stamp = os.path.getmtime(origin)
    except OSError:
        return _UNREADABLE
    if origin not in _CLIPS or _CLIPS[origin][0] != stamp:
        try:
            _CLIPS[origin] = (stamp, _banks(origin) or _NO_CLIPS)
        except Exception:
            # Cached like any other answer: a pack that cannot be read would
            # otherwise reopen its zip on every redraw, and the stamp clears
            # this the moment the file is rewritten.
            _CLIPS[origin] = (stamp, _UNREADABLE)
    return _CLIPS[origin][1]


def _banks(origin):
    clips = sorted(read_manifest(origin).get("clips", []),
                   key=lambda clip: clip["label"].casefold())
    return [(clip["path"],
             clip["label"],
             "%d frames at %d Hz%s" % (clip.get("frames", 0), clip.get("rate", 0),
                                       ", on %s" % clip["bone"] if clip.get("bone") else ""))
            for clip in clips]


class FC2_OT_load_clip(bpy.types.Operator):
    """Pose the armature with one of the animation banks the pack carries"""

    bl_idname = "object.fc2_load_clip"
    bl_label = "Load Far Cry 2 Animation"
    bl_options = {"REGISTER", "UNDO"}

    clip: EnumProperty(name="Animation", items=_clip_items,
                       description="Which bank the pack carries to play")
    set_frame_range: BoolProperty(
        name="Set frame range", default=True,
        description="Point the scene's frame range and rate at the clip")
    with_props: BoolProperty(
        name="Mark attachments", default=True,
        description="Put a marker on each bone the bank attaches something to, "
                    "carrying that participant's own track")

    @classmethod
    def poll(cls, context):
        return _pack_of(context)[0] is not None

    def invoke(self, context, _event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        armature = context.object
        if armature is None or armature.type != "ARMATURE":
            armature = next((o for o in context.scene.objects if o.type == "ARMATURE"), None)
        if armature is None:
            self.report({"ERROR"}, "Select the armature to animate")
            return {"CANCELLED"}
        if not self.clip:
            self.report({"ERROR"}, "This pack carries no animation to load")
            return {"CANCELLED"}

        origin, _collection = _pack_of(context)
        try:
            result = import_mab.load(Pack.load(origin), self.clip, armature,
                                     with_props=self.with_props)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

        if self.set_frame_range:
            import_mab.apply_to_scene(context.scene, result["clip"])
        props = (", plus %s" % ", ".join(p["participant"]["name"] for p in result["props"])
                 if result["props"] else "")
        if result["unmatched"]:
            self.report({"WARNING"}, "%d keys on %d bones%s; %d tracks name no bone here"
                        % (result["keys"], result["bones"], props, result["unmatched"]))
        else:
            self.report({"INFO"}, "%d keys on %d bones%s"
                        % (result["keys"], result["bones"], props))
        return {"FINISHED"}


class FC2_OT_write_clip(bpy.types.Operator):
    """Write the armature's current Action back into the bank it came from"""

    bl_idname = "object.fc2_write_clip"
    bl_label = "Write Animation"
    bl_options = {"REGISTER"}

    clip: EnumProperty(name="Animation", items=_clip_items,
                       description="Which of the pack's banks to rewrite")
    lossless: BoolProperty(
        name="Key every frame", default=False,
        description="Store a key on every frame instead of only where the motion departs "
                    "from its neighbours. Exact, and roughly eight times the bytes")
    tolerance: FloatProperty(
        name="Tolerance", default=0.002, min=0.0, max=0.1, precision=4,
        description="How far a dropped frame may sit from the interpolation of the frames "
                    "kept around it")

    @classmethod
    def poll(cls, context):
        return _pack_of(context)[0] is not None

    def invoke(self, context, _event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        armature = context.object
        if armature is None or armature.type != "ARMATURE":
            armature = next((o for o in context.scene.objects if o.type == "ARMATURE"), None)
        if armature is None:
            self.report({"ERROR"}, "Select the armature whose Action to write")
            return {"CANCELLED"}
        if not self.clip:
            self.report({"ERROR"}, "This pack carries no animation to write")
            return {"CANCELLED"}

        origin, _collection = _pack_of(context)
        pack = Pack.load(origin)
        try:
            written = export_mab.write(pack, self.clip, armature,
                                       lossless=self.lossless, tolerance=self.tolerance)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

        # Saved straight back over the pack it came from, so the next export or
        # apply carries the new motion without a second file to keep in step.
        pack.save(origin)
        self.report({"INFO"},
                    "Wrote clip %d of %d: %d bones keyed, %d held still, %d moved, %d keys"
                    % (written["clip"], written["clips"], written["keyed_rotations"],
                       written["constant_rotations"], written["animated_translations"],
                       written["keys"]))
        return {"FINISHED"}


# The bone enum's items, kept alive for the callback the way _CLIPS is.
_BONES = []


def _bone_items(_self, context):
    global _BONES
    _origin, collection = _pack_of(context)
    armature = _armature_in(collection)
    _BONES = [(ROOT_BONE, "Model root", "Sit in the model's own space, placed by nothing")]
    if armature:
        _BONES += [(bone.name, bone.name, "") for bone in armature.data.bones]
    return _BONES


def _armature_in(collection):
    if collection is None:
        return None
    return next((obj for obj in collection.objects if obj.type == "ARMATURE"), None)


class FC2_OT_add_part(bpy.types.Operator):
    """Add the selected mesh to the model as a part it did not have"""

    bl_idname = "object.fc2_add_part"
    bl_label = "Add as New Part"
    bl_options = {"REGISTER", "UNDO"}

    name: StringProperty(
        name="Part name", default="",
        description="What the model calls the part, without the _LOD suffix export adds")
    bone: EnumProperty(
        name="Attach to", items=_bone_items,
        description="The bone that places the part, the way a weapon's parts hang on theirs")

    @classmethod
    def poll(cls, context):
        obj = context.object
        return obj is not None and obj.type == "MESH" and _pack_of(context)[0] is not None

    def invoke(self, context, _event):
        if not self.name:
            self.name = context.object.name.upper()
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        obj = context.object
        _origin, collection = _pack_of(context)
        armature = _armature_in(collection)
        name = self.name.strip()
        if not name:
            self.report({"ERROR"}, "Give the part a name")
            return {"CANCELLED"}

        for other in list(obj.users_collection):
            other.objects.unlink(obj)
        collection.objects.link(obj)

        on_bone = armature is not None and self.bone != ROOT_BONE
        place = (armature.matrix_world @ armature.data.bones[self.bone].matrix_local
                 if on_bone else Matrix.Identity(4))
        # A part is modelled around its own pivot, and export reads vertices in
        # object space, so the transform that put the object where the modeler
        # left it moves into the mesh rather than staying on the object.
        obj.data.transform(place.inverted() @ obj.matrix_world)
        if armature:
            obj.parent = armature
            if on_bone:
                obj.parent_type = "BONE"
                obj.parent_bone = self.bone
        obj.matrix_world = place

        obj[import_xbg.PROP_NEW_PART] = name
        import_xbg.stamp_placement(obj)
        self.report({"INFO"}, "%s will be added as %s" % (obj.name, name))
        return {"FINISHED"}


class FC2_OT_export_pack(bpy.types.Operator, ExportHelper):
    """Write the edited parts back into the pack they were imported from"""

    bl_idname = "export_scene.fc2_pack"
    bl_label = "Export Far Cry 2 Model Pack"
    bl_options = {"REGISTER"}

    filename_ext = EXTENSION
    filter_glob: StringProperty(default="*" + EXTENSION, options={"HIDDEN"})
    recompute_tangents: BoolProperty(
        name="Recompute tangents", default=False,
        description="Rebuild the tangent frame from the UVs. Done automatically for "
                    "a part whose vertex count changed; turn on after editing UVs on "
                    "a part that kept its topology")

    def execute(self, context):
        try:
            collection = export_xbg.collection_of(context)
            result = export_xbg.save(self.filepath, collection, self.recompute_tangents)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

        if not result["moved"] and not result["resized"] and not result["added"]:
            self.report({"INFO"}, "Wrote the pack unchanged: nothing in LOD%d was edited"
                        % result["lod"])
        else:
            added = ", %d added" % result["added"] if result["added"] else ""
            self.report({"INFO"}, "Wrote %d parts at LOD%d, %d rebuilt%s"
                        % (result["parts"], result["lod"], result["resized"], added))
        return {"FINISHED"}


def menu_import(self, _context):
    self.layout.operator(FC2_OT_import_pack.bl_idname,
                         text="Far Cry 2 Model Pack (%s)" % EXTENSION)


def menu_export(self, _context):
    self.layout.operator(FC2_OT_export_pack.bl_idname,
                         text="Far Cry 2 Model Pack (%s)" % EXTENSION)


CLASSES = (FC2_OT_import_pack, FC2_OT_load_clip, FC2_OT_write_clip, FC2_OT_add_part,
           FC2_OT_export_pack)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    # After the operators: the panel's buttons name them, and a panel drawn
    # against an unregistered operator is a poll error on the first redraw.
    panel.register()
    bpy.types.TOPBAR_MT_file_import.append(menu_import)
    bpy.types.TOPBAR_MT_file_export.append(menu_export)


def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(menu_export)
    bpy.types.TOPBAR_MT_file_import.remove(menu_import)
    panel.unregister()
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
