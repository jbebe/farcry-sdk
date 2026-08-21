# Far Cry 2 format add-on: registration and the file menu entries.

import os
import sys

import bpy
from bpy.props import BoolProperty, IntProperty, StringProperty
from bpy_extras.io_utils import ImportHelper

# fc2fmt sits beside this package and imports no bpy, so it also runs headless.
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from fc2fmt.bundle import EXTENSION

from . import import_xbg


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


def menu_import(self, _context):
    self.layout.operator(FC2_OT_import_bundle.bl_idname,
                         text="Far Cry 2 Model Bundle (%s)" % EXTENSION)
    self.layout.operator(FC2_OT_import_xbg.bl_idname, text="Far Cry 2 Mesh (.xbg)")


CLASSES = (FC2_OT_import_xbg, FC2_OT_import_bundle)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.TOPBAR_MT_file_import.append(menu_import)


def unregister():
    bpy.types.TOPBAR_MT_file_import.remove(menu_import)
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
