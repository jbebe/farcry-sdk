# The Far Cry 2 sidebar: what the model is, what is wrong with it, and what its
# animations do to it.
#
# Two rules run through everything here.
#
# `draw` is called on every redraw, so it only ever reads state that is already
# in the scene. Checking, and opening the pack to list its clips, both happen in
# an operator and land in a PropertyGroup - a panel that opened a zip or ran an
# export from `draw` is how an add-on makes the viewport stutter.
#
# Selecting a finding is a button, not an `update=` callback on the active index.
# Calling `bpy.ops` from a property update runs it during UI event handling,
# which is a known way to crash Blender.

import bpy
from bpy.props import (BoolProperty, CollectionProperty, EnumProperty, FloatProperty,
                       IntProperty, PointerProperty, StringProperty)

from . import export_xbg, import_mab, import_xbg, motion, validate
from .pack import Pack
from .rules import ERROR, INFO, WARNING

ICONS = {ERROR: "ERROR", WARNING: "INFO", INFO: "INFO"}


class FC2_PG_finding(bpy.types.PropertyGroup):
    """One row of the last check. Never rebuilt in draw."""
    severity: StringProperty()
    code: StringProperty()
    message: StringProperty()
    hint: StringProperty()
    target_object: StringProperty()
    target_kind: StringProperty()
    target_name: StringProperty()
    target_index: IntProperty(default=-1)


class FC2_PG_bone(bpy.types.PropertyGroup):
    """One row of the motion table: how far a bone travels across the clips."""
    name: StringProperty()
    rotation: FloatProperty()
    translation: FloatProperty()
    clip: StringProperty()


class FC2_PG_state(bpy.types.PropertyGroup):
    findings: CollectionProperty(type=FC2_PG_finding)
    active: IntProperty(default=0)
    checked: BoolProperty(default=False)
    summary: StringProperty(default="")

    bones: CollectionProperty(type=FC2_PG_bone)
    active_bone: IntProperty(default=0)
    motion_summary: StringProperty(default="")


class FC2_UL_findings(bpy.types.UIList):
    def draw_item(self, _context, layout, _data, item, _icon, _active, _prop):
        row = layout.row(align=True)
        row.label(text="", icon=ICONS.get(item.severity, "INFO"))
        row.label(text=item.message)


class FC2_UL_bones(bpy.types.UIList):
    def draw_item(self, _context, layout, _data, item, _icon, _active, _prop):
        row = layout.row(align=True)
        row.label(text=item.name)
        row.label(text="%6.1f deg" % item.rotation)
        row.label(text="%6.3f m" % item.translation)


class FC2_OT_check(bpy.types.Operator):
    """Check this model against what the format allows"""

    bl_idname = "object.fc2_check"
    bl_label = "Check"
    bl_options = {"REGISTER"}

    @classmethod
    def poll(cls, context):
        return _pack_path(context) is not None

    def execute(self, context):
        state = context.scene.fc2
        state.findings.clear()
        try:
            found = validate.check_scene(context)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

        for finding in found:
            row = state.findings.add()
            row.severity = finding.severity
            row.code = finding.code
            row.message = finding.message
            row.hint = finding.hint
            row.target_object = finding.target.object
            row.target_kind = finding.target.kind
            row.target_name = finding.target.name
            row.target_index = finding.target.index

        state.active = 0
        state.checked = True
        blocking = len(validate.blocking(found))
        state.summary = ("Ready to export." if not found else
                         "%d problem(s), %d of them blocking." % (len(found), blocking))
        self.report({"INFO"}, state.summary)
        return {"FINISHED"}


class FC2_OT_select_finding(bpy.types.Operator):
    """Select what this row is about"""

    bl_idname = "object.fc2_select_finding"
    bl_label = "Select"
    bl_options = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        state = context.scene.fc2
        return 0 <= state.active < len(state.findings)

    def execute(self, context):
        row = context.scene.fc2.findings[context.scene.fc2.active]
        obj = bpy.data.objects.get(row.target_object)
        if obj is None:
            self.report({"INFO"}, "Nothing to select for this one.")
            return {"CANCELLED"}

        if context.object and context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        context.view_layer.objects.active = obj

        if row.target_kind == "vertex" and row.target_index >= 0:
            self._select_vertex(context, obj, row.target_index)
        elif row.target_kind == "material" and row.target_name:
            self._select_material(obj, row.target_name)
        return {"FINISHED"}

    @staticmethod
    def _select_vertex(context, obj, index):
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="DESELECT")
        bpy.ops.object.mode_set(mode="OBJECT")
        if index < len(obj.data.vertices):
            obj.data.vertices[index].select = True
        bpy.ops.object.mode_set(mode="EDIT")
        context.tool_settings.mesh_select_mode = (True, False, False)

    @staticmethod
    def _select_material(obj, name):
        for index, slot in enumerate(obj.material_slots):
            if slot.material and slot.material.name == name:
                obj.active_material_index = index
                return


class FC2_OT_motion_table(bpy.types.Operator):
    """Measure how far each bone travels across every clip the pack carries"""

    bl_idname = "object.fc2_motion_table"
    bl_label = "Measure motion"
    bl_options = {"REGISTER"}

    @classmethod
    def poll(cls, context):
        return _pack_path(context) is not None

    def execute(self, context):
        state = context.scene.fc2
        state.bones.clear()
        try:
            pack = Pack.load(_pack_path(context))
            table = motion.table(pack)
        except Exception as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}

        if not table:
            state.motion_summary = "This pack carries no animation."
            self.report({"INFO"}, state.motion_summary)
            return {"FINISHED"}

        for entry in table:
            row = state.bones.add()
            row.name = entry["bone"]
            row.rotation = entry["rotation"]
            row.translation = entry["translation"]
            row.clip = entry["clip"]

        state.motion_summary = "%d bone(s) over %d bank(s)" % (len(table), len(pack.clips))
        self.report({"INFO"}, state.motion_summary)
        return {"FINISHED"}


class FC2_PT_base(bpy.types.Panel):
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Far Cry 2"


class FC2_PT_model(FC2_PT_base):
    bl_idname = "FC2_PT_model"
    bl_label = "Model"

    def draw(self, context):
        layout = self.layout
        path = _pack_path(context)
        if path is None:
            layout.label(text="No model pack imported.", icon="INFO")
            layout.operator("import_scene.fc2_pack", icon="IMPORT")
            return

        collection = _collection(context)
        layout.label(text=collection.name, icon="OUTLINER_COLLECTION")
        layout.label(text="LOD %d" % collection.get(import_xbg.PROP_LOD, 0))

        parts = [obj for obj in collection.objects
                 if obj.type == "MESH" and import_xbg.PROP_SUBMESH in obj]
        triangles = sum(len(obj.data.polygons) for obj in parts)
        vertices = sum(len(obj.data.vertices) for obj in parts)

        # Export's own list, so the panel cannot announce a part it would skip.
        added = export_xbg.new_parts(collection)

        box = layout.box()
        box.label(text="%d parts, %d triangles, %d vertices" % (len(parts), triangles, vertices))
        if added:
            box.label(text="%d to be added: %s"
                      % (len(added), ", ".join(obj[import_xbg.PROP_NEW_PART] for obj in added)),
                      icon="PLUS")
        # Counts only. The ceilings are per cluster and per buffer, and a bar
        # against a total would say a model is fine when one part is not - which
        # is what the check is for.
        box.label(text="Check for the per-part limits", icon="CHECKMARK")
        layout.operator("object.fc2_add_part", icon="PLUS")


class FC2_PT_check(FC2_PT_base):
    bl_idname = "FC2_PT_check"
    bl_label = "Check"

    def draw(self, context):
        layout = self.layout
        state = context.scene.fc2

        row = layout.row()
        row.scale_y = 1.3
        row.operator("object.fc2_check", icon="CHECKMARK")
        layout.label(text="As slow as an export, and it regenerates tangents.", icon="INFO")

        if not state.checked:
            return
        if not len(state.findings):
            layout.label(text="Nothing to report.", icon="CHECKMARK")
            return

        layout.label(text=state.summary)
        layout.template_list("FC2_UL_findings", "", state, "findings", state, "active")
        if not 0 <= state.active < len(state.findings):
            return

        row = state.findings[state.active]
        box = layout.box()
        box.label(text=row.code, icon=ICONS.get(row.severity, "INFO"))
        _wrapped(box, row.message)
        if row.hint:
            _wrapped(box, row.hint, icon="LIGHT")
        if row.target_object:
            box.operator("object.fc2_select_finding", icon="RESTRICT_SELECT_OFF")


class FC2_PT_animation(FC2_PT_base):
    bl_idname = "FC2_PT_animation"
    bl_label = "Animation"

    def draw(self, context):
        layout = self.layout
        if _pack_path(context) is None:
            layout.label(text="No model pack imported.", icon="INFO")
            return

        layout.operator("object.fc2_load_clip", icon="ARMATURE_DATA")
        layout.operator("object.fc2_write_clip", icon="REC")
        layout.operator("object.fc2_motion_table", icon="DRIVER_ROTATIONAL_DIFFERENCE")

        state = context.scene.fc2
        if not len(state.bones):
            if state.motion_summary:
                layout.label(text=state.motion_summary)
            return

        layout.label(text=state.motion_summary)
        layout.template_list("FC2_UL_bones", "", state, "bones", state, "active_bone")
        if 0 <= state.active_bone < len(state.bones):
            row = state.bones[state.active_bone]
            box = layout.box()
            box.label(text="%s: %.1f deg, %.3f m" % (row.name, row.rotation, row.translation))
            _wrapped(box, "Worst in %s" % row.clip)
            _wrapped(box, "A bone that does not move is where the body of the model belongs; "
                          "one that swings is a moving part.", icon="LIGHT")


class FC2_PT_export(FC2_PT_base):
    bl_idname = "FC2_PT_export"
    bl_label = "Export"

    def draw(self, context):
        layout = self.layout
        state = context.scene.fc2
        blocking = sum(1 for row in state.findings if row.severity == ERROR)
        if state.checked and blocking:
            layout.label(text="%d blocking problem(s)" % blocking, icon="ERROR")
        row = layout.row()
        row.scale_y = 1.3
        row.operator("export_scene.fc2_pack", icon="EXPORT")


def _wrapped(layout, text, icon="NONE", width=44):
    """A message across as many rows as it takes. Blender does not wrap labels."""
    column = layout.column(align=True)
    line = ""
    for word in text.split():
        if len(line) + len(word) + 1 > width:
            column.label(text=line, icon=icon)
            icon = "NONE"
            line = word
        else:
            line = "%s %s" % (line, word) if line else word
    if line:
        column.label(text=line, icon=icon)


def _collection(context):
    active = context.view_layer.active_layer_collection
    if active and import_xbg.PROP_SOURCE in active.collection:
        return active.collection
    return next((c for c in bpy.data.collections
                 if import_xbg.PROP_SOURCE in c
                 and import_mab.PROP_PROP_OF not in c), None)


def _pack_path(context):
    collection = _collection(context)
    return collection.get(import_xbg.PROP_SOURCE) if collection else None


CLASSES = (
    FC2_PG_finding, FC2_PG_bone, FC2_PG_state,
    FC2_UL_findings, FC2_UL_bones,
    FC2_OT_check, FC2_OT_select_finding, FC2_OT_motion_table,
    FC2_PT_model, FC2_PT_check, FC2_PT_animation, FC2_PT_export,
)


def register():
    # PropertyGroups first: a CollectionProperty cannot reference a type that is
    # not registered yet.
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.fc2 = PointerProperty(type=FC2_PG_state)


def unregister():
    del bpy.types.Scene.fc2
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
