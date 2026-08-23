# Far Cry 2 model add-on: registration and the file menu entries.
#
# One import and one export, both of `.fc2model` - the decoded pack JackAll
# writes. Nothing here opens a game file, which is the whole arrangement: JackAll
# owns the byte layouts and this owns what a scene looks like.

import bpy
from bpy.props import BoolProperty, EnumProperty, IntProperty, StringProperty
from bpy_extras.io_utils import ExportHelper, ImportHelper

from . import export_xbg, import_mab, import_xbg, panel
from .pack import EXTENSION, Pack


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
    collections = list(obj.users_collection) if obj else []
    collections += [c for c in bpy.data.collections if import_xbg.PROP_SOURCE in c]
    for collection in collections:
        origin = collection.get(import_xbg.PROP_SOURCE)
        if origin:
            return origin, collection
    return None, None


def _clip_items(self, context):
    """The banks the pack carries, straight from its manifest.

    Read here rather than in a panel's draw: draw runs on every redraw, and
    opening a zip from one is how an add-on makes the viewport stutter.
    """
    origin, _collection = _pack_of(context)
    if not origin:
        return [("", "No pack imported", "")]
    try:
        clips = Pack.load(origin).clips
    except Exception:
        return [("", "Could not read the pack", "")]
    if not clips:
        return [("", "This pack carries no animation", "")]
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

        if not result["moved"] and not result["resized"]:
            self.report({"INFO"}, "Wrote the pack unchanged: nothing in LOD%d was edited"
                        % result["lod"])
        else:
            self.report({"INFO"}, "Wrote %d parts at LOD%d, %d rebuilt"
                        % (result["parts"], result["lod"], result["resized"]))
        return {"FINISHED"}


def menu_import(self, _context):
    self.layout.operator(FC2_OT_import_pack.bl_idname,
                         text="Far Cry 2 Model Pack (%s)" % EXTENSION)


def menu_export(self, _context):
    self.layout.operator(FC2_OT_export_pack.bl_idname,
                         text="Far Cry 2 Model Pack (%s)" % EXTENSION)


CLASSES = (FC2_OT_import_pack, FC2_OT_load_clip, FC2_OT_export_pack)


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
