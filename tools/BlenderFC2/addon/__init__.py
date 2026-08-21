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

    def execute(self, context):
        try:
            result = import_xbg.load(self.filepath, self.lod, self.with_armature)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
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
