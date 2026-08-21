# Far Cry 2 format add-on: registration and the file menu entries.

import os
import sys

import bpy
from bpy.props import BoolProperty, IntProperty, StringProperty
from bpy_extras.io_utils import ExportHelper, ImportHelper

# fc2fmt sits beside this package and imports no bpy, so it also runs headless.
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from fc2fmt.bundle import EXTENSION

from . import export_xbg, import_mab, import_xbg


class FC2ImportBase(ImportHelper):
    """The options and reporting both importers share; `run` does the loading."""

    lod: IntProperty(name="LOD", default=0, min=0,
                     description="Which detail level to import")
    with_armature: BoolProperty(name="Build armature", default=True,
                                description="Create bones from the model's nodes")
    with_textures: BoolProperty(
        name="Load textures", default=True,
        description="Resolve each material and load the textures it names")

    def execute(self, context):
        try:
            result = self.run()
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        parts = len(result["parts"])
        if self.with_textures and result["source"] is None:
            self.report({"WARNING"}, "Imported %d parts, but found no assets for textures" % parts)
        else:
            self.report({"INFO"}, "Imported %d parts" % parts)
        return {"FINISHED"}


class FC2_OT_import_xbg(bpy.types.Operator, FC2ImportBase):
    bl_idname = "import_scene.fc2_xbg"
    bl_label = "Import Far Cry 2 Mesh"
    bl_options = {"REGISTER", "UNDO"}

    filename_ext = ".xbg"
    filter_glob: StringProperty(default="*.xbg", options={"HIDDEN"})
    game_root: StringProperty(
        name="Game root", default="", subtype="DIR_PATH",
        description="Where to look for materials and textures; found from the "
                    "model's own path when left empty")

    def run(self):
        return import_xbg.load(self.filepath, self.lod, self.with_armature,
                               self.with_textures, self.game_root or None)


class FC2_OT_import_bundle(bpy.types.Operator, FC2ImportBase):
    bl_idname = "import_scene.fc2_bundle"
    bl_label = "Import Far Cry 2 Model Bundle"
    bl_options = {"REGISTER", "UNDO"}

    filename_ext = EXTENSION
    filter_glob: StringProperty(default="*" + EXTENSION, options={"HIDDEN"})

    def run(self):
        return import_xbg.load_bundle(self.filepath, self.lod, self.with_armature,
                                      self.with_textures)


class FC2_OT_import_mab(bpy.types.Operator, ImportHelper):
    """Load an animation onto the selected armature"""

    bl_idname = "import_scene.fc2_mab"
    bl_label = "Import Far Cry 2 Animation"
    bl_options = {"REGISTER", "UNDO"}

    filename_ext = ".mab"
    filter_glob: StringProperty(default="*.mab", options={"HIDDEN"})
    skeleton: StringProperty(
        name="Skeleton", default="", subtype="FILE_PATH",
        description="The .skeleton the clip names its bones by; found from the "
                    "clip's own path when left empty")
    set_frame_range: BoolProperty(
        name="Set frame range", default=True,
        description="Point the scene's frame range and rate at the clip")

    def execute(self, context):
        armature = context.object
        if armature is None or armature.type != "ARMATURE":
            armature = next((o for o in context.scene.objects if o.type == "ARMATURE"), None)
        if armature is None:
            self.report({"ERROR"}, "Select the armature to animate")
            return {"CANCELLED"}
        try:
            result = import_mab.load(self.filepath, armature, self.skeleton or None)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        if self.set_frame_range:
            import_mab.apply_to_scene(context.scene, result["clip"])
        if result["unmatched"]:
            self.report({"WARNING"}, "%d keys on %d bones; %d tracks name no bone here"
                        % (result["keys"], result["bones"], result["unmatched"]))
        else:
            self.report({"INFO"}, "%d keys on %d bones" % (result["keys"], result["bones"]))
        return {"FINISHED"}


class FC2_OT_export_xbg(bpy.types.Operator, ExportHelper):
    """Write the edited parts back into the model they were imported from"""

    bl_idname = "export_scene.fc2_xbg"
    bl_label = "Export Far Cry 2 Mesh"
    bl_options = {"REGISTER"}

    filename_ext = ".xbg"
    filter_glob: StringProperty(default="*.xbg", options={"HIDDEN"})
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
        self.report({"INFO"}, "Wrote %d parts at LOD%d, %d rebuilt"
                    % (result["parts"], result["lod"], result["resized"]))
        return {"FINISHED"}


def menu_import(self, _context):
    self.layout.operator(FC2_OT_import_bundle.bl_idname,
                         text="Far Cry 2 Model Bundle (%s)" % EXTENSION)
    self.layout.operator(FC2_OT_import_xbg.bl_idname, text="Far Cry 2 Mesh (.xbg)")
    self.layout.operator(FC2_OT_import_mab.bl_idname, text="Far Cry 2 Animation (.mab)")


def menu_export(self, _context):
    self.layout.operator(FC2_OT_export_xbg.bl_idname, text="Far Cry 2 Mesh (.xbg)")


CLASSES = (FC2_OT_import_xbg, FC2_OT_import_bundle, FC2_OT_import_mab, FC2_OT_export_xbg)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.TOPBAR_MT_file_import.append(menu_import)
    bpy.types.TOPBAR_MT_file_export.append(menu_export)


def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(menu_export)
    bpy.types.TOPBAR_MT_file_import.remove(menu_import)
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
