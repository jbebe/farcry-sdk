# Far Cry 2 format add-on: registration and the file menu entries.

import os
import sys

import bpy
from bpy.props import BoolProperty, IntProperty, StringProperty
from bpy_extras.io_utils import ImportHelper

# fc2fmt sits beside this package and imports no bpy, so it also runs headless.
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from . import import_xbg


class FC2_OT_import_xbg(bpy.types.Operator, ImportHelper):
    bl_idname = "import_scene.fc2_xbg"
    bl_label = "Import Far Cry 2 Mesh"
    bl_options = {"REGISTER", "UNDO"}

    filename_ext = ".xbg"
    filter_glob: StringProperty(default="*.xbg", options={"HIDDEN"})
    lod: IntProperty(name="LOD", default=0, min=0,
                     description="Which detail level to import")
    with_armature: BoolProperty(name="Build armature", default=True,
                                description="Create bones from the model's nodes")
    with_textures: BoolProperty(
        name="Load textures", default=True,
        description="Resolve each material's .xbm and load the .xbt textures it names")
    game_root: StringProperty(
        name="Game root", default="", subtype="DIR_PATH",
        description="Where to look for materials and textures; found from the "
                    "model's own path when left empty")

    def execute(self, context):
        try:
            result = import_xbg.load(self.filepath, self.lod, self.with_armature,
                                     self.with_textures, self.game_root or None)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        if self.with_textures and not result["files"]:
            self.report({"WARNING"},
                        "Imported %d parts, but no game root was found for textures"
                        % len(result["parts"]))
            return {"FINISHED"}
        self.report({"INFO"}, "Imported %d parts" % len(result["parts"]))
        return {"FINISHED"}


def menu_import(self, _context):
    self.layout.operator(FC2_OT_import_xbg.bl_idname, text="Far Cry 2 Mesh (.xbg)")


CLASSES = (FC2_OT_import_xbg,)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.TOPBAR_MT_file_import.append(menu_import)


def unregister():
    bpy.types.TOPBAR_MT_file_import.remove(menu_import)
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
