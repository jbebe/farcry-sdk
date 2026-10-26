# The Far Cry 2 panel in the Material properties: the material's document, laid
# out the way its shader's own property list groups it.
#
# `draw` reads only what is already cached; the pack's manifest and shader list
# are read once per file change, the way the clip list is.

import os

import bpy
from bpy.props import EnumProperty, IntProperty, StringProperty

from . import material_data, panel
from .import_xbg import PROP_SOURCE
from .material_data import BROKEN, CLEAN, FLOAT, INTEGER, TEXTURE
from .pack import OWNED, read_schema, same_path

# Each pack's manifest and shader list against the mtime they were read at.
_PACKS = {}

# The shader enum's items, kept alive for Blender the way the clip list is.
_SHADERS = []


def _pack(context):
    """The manifest and shader list of the pack the active object came from."""
    obj = context.object
    path = next((c.get(PROP_SOURCE) for c in (obj.users_collection if obj else ())
                 if c.get(PROP_SOURCE)), None) or panel.pack_path(context)
    if not path:
        return {}, {}
    try:
        stamp = os.path.getmtime(path)
    except OSError:
        return {}, {}
    if path not in _PACKS or _PACKS[path][0] != stamp:
        try:
            _PACKS[path] = (stamp,) + read_schema(path)
        except Exception:
            _PACKS[path] = (stamp, {}, {})
    return _PACKS[path][1], _PACKS[path][2]


def _record(manifest, path):
    return next((entry for entry in manifest.get("entries", ())
                 if same_path(entry["path"], path)), None)


def _parameter(context, key):
    descriptor = _pack(context)[1].get(context.material.fc2.shader)
    return next((p for p in descriptor["parameters"] if p["name"] == key), None) if descriptor else None


class FC2_OT_material_add(bpy.types.Operator):
    """Give the material this property, at the shader's default"""

    bl_idname = "material.fc2_add_value"
    bl_label = "Add"
    bl_options = {"REGISTER", "UNDO"}

    key: StringProperty()

    @classmethod
    def poll(cls, context):
        return material_data.held(context.material)

    def execute(self, context):
        parameter = _parameter(context, self.key)
        if parameter is None:
            self.report({"ERROR"}, "%s takes no %s" % (context.material.fc2.shader, self.key))
            return {"CANCELLED"}
        material = context.material
        material_data.add(material.fc2, parameter["section"], self.key, _default(parameter))
        material_data.refresh(material)
        return {"FINISHED"}


def _default(parameter):
    """A new value as the shader's descriptor gives it, else white for a colour and zero."""
    section, width = parameter["section"], parameter["width"]
    if section == TEXTURE:
        return {"path": ""}
    numbers = [float(n) for n in (parameter.get("default") or "").split(",") if n.strip()]
    if section == INTEGER:
        return {"value": int(numbers[0]) if numbers else 0}
    if len(numbers) == 1:
        numbers *= width
    if len(numbers) != width:
        numbers = [1.0 if parameter.get("is_color") else 0.0] * width
    return {"value": numbers}


class FC2_OT_material_remove(bpy.types.Operator):
    """Drop this property, so the shader's own default applies"""

    bl_idname = "material.fc2_remove_value"
    bl_label = "Remove"
    bl_options = {"REGISTER", "UNDO"}

    index: IntProperty()

    @classmethod
    def poll(cls, context):
        return material_data.held(context.material)

    def execute(self, context):
        material = context.material
        material.fc2.values.remove(self.index)
        material_data.refresh(material)
        return {"FINISHED"}


def _shader_items(_self, context):
    global _SHADERS
    shaders = _pack(context)[1]
    _SHADERS = [(name, name, "") for name in sorted(shaders) if shaders[name]["name"] == name]
    return _SHADERS or [("", "This pack carries no shader list", "")]


class FC2_OT_material_shader(bpy.types.Operator):
    """Draw the material with another shader. Properties it does not take are listed apart"""

    bl_idname = "material.fc2_shader"
    bl_label = "Change Shader"
    bl_options = {"REGISTER", "UNDO"}

    shader: EnumProperty(name="Shader", items=_shader_items)

    @classmethod
    def poll(cls, context):
        return material_data.held(context.material)

    def invoke(self, context, _event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        if not self.shader:
            return {"CANCELLED"}
        context.material.fc2.shader = self.shader
        return {"FINISHED"}


class FC2_PT_material(bpy.types.Panel):
    bl_idname = "FC2_PT_material"
    bl_label = "Far Cry 2"
    bl_space_type = "PROPERTIES"
    bl_region_type = "WINDOW"
    bl_context = "material"

    @classmethod
    def poll(cls, context):
        return material_data.held(context.material)

    def draw(self, context):
        layout = self.layout
        material = context.material
        fc2 = material.fc2
        path = material.get(material_data.PROP_MATERIAL_PATH, "")
        manifest, shaders = _pack(context)

        layout.label(text=path, icon="MATERIAL")
        record = _record(manifest, path)
        owned = record is not None and record.get("role") == OWNED
        if not owned:
            panel.wrapped(layout.box(), "Shared with other models, so an edit to it would change "
                                        "them all; export refuses it.", icon="LOCKED")

        body = layout.column()
        body.enabled = owned
        body.prop(fc2, "name")
        row = body.row()
        row.label(text="Shader: %s" % fc2.shader)
        row.operator(FC2_OT_material_shader.bl_idname, text="Change")
        descriptor = shaders.get(fc2.shader)
        if shaders and descriptor is None:
            body.label(text="No shader is named %s, so the game draws this with Error." % fc2.shader,
                       icon="ERROR")
        body.prop(fc2, "show_all")
        if any(material_data.clean_twin(fc2, v.key) for v in fc2.values if v.section == FLOAT):
            body.prop(fc2, "link_clean")

        parameters = descriptor["parameters"] if descriptor else []
        declared = {p["name"]: p for p in parameters}
        twins = {p["name"] for p in parameters if _base(p["name"], declared)}
        groups = {}
        for parameter in parameters:
            if parameter["name"] not in twins:
                groups.setdefault(parameter["group"], []).append(parameter)
        for group, members in groups.items():
            keys = [p["name"] + suffix for p in members for suffix in ("", CLEAN, BROKEN)]
            if not fc2.show_all and not any(v.key in keys for v in fc2.values):
                continue
            body.label(text=group)
            for parameter in members:
                _rows(body, fc2, parameter, parameter["name"])
                for suffix in (CLEAN, BROKEN):
                    if parameter["name"] + suffix in twins:
                        _rows(body, fc2, declared[parameter["name"] + suffix], suffix)

        stray = [i for i, v in enumerate(fc2.values) if v.key not in declared]
        if stray:
            body.label(text="Not taken by %s" % fc2.shader if descriptor else "Properties")
            for index in stray:
                _row(body, fc2, index, None)


def _base(name, declared):
    """The value a Clean or Broken twin belongs to, when the shader declares it."""
    for suffix in (CLEAN, BROKEN):
        if name.endswith(suffix) and name[:-len(suffix)] in declared:
            return name[:-len(suffix)]
    return None


def _rows(layout, fc2, parameter, label):
    """A parameter's value rows, or its unset row when every property is shown."""
    indices = [i for i, v in enumerate(fc2.values) if v.key == parameter["name"]]
    for index in indices:
        _row(layout, fc2, index, parameter, label)
    if not indices and fc2.show_all:
        _unset(layout, parameter, label)


def _row(layout, fc2, index, parameter, label=None):
    value = fc2.values[index]
    split = layout.split(factor=0.42, align=True)
    _label(split, label or value.key, value.key)
    row = split.row(align=True)
    if value.section == TEXTURE:
        row.prop(value, "image", text="")
    elif value.section == INTEGER:
        row.prop(value, "integer", text="")
    elif parameter and parameter.get("is_color") and value.width >= 3:
        row.prop(value, "colour", text="")
        if value.width == 4:
            row.prop(value, "number", index=3, text="")
    else:
        for axis in range(value.width):
            row.prop(value, "number", index=axis, text="")
    row.operator(FC2_OT_material_remove.bl_idname, text="", icon="X").index = index
    if value.section == TEXTURE:
        layout.prop(value, "path", text="")

    twin = material_data.clean_twin(fc2, value.key) if value.section == FLOAT else None
    if twin is not None and not fc2.link_clean and tuple(twin.number) != tuple(value.number):
        layout.label(text="Differs from %s, which is what the game shows" % twin.key, icon="ERROR")


def _label(layout, text, key):
    """A key, or a twin's suffix indented under the value it belongs to."""
    row = layout.row()
    if text != key:
        row.separator(factor=2.0)
    row.label(text=text)
    return row


def _unset(layout, parameter, label):
    split = layout.split(factor=0.42, align=True)
    _label(split, label, parameter["name"]).active = False
    row = split.row(align=True)
    row.active = False
    row.label(text="default %s" % parameter["default"] if parameter.get("default") else "unset")
    row.operator(FC2_OT_material_add.bl_idname, text="", icon="ADD").key = parameter["name"]


CLASSES = (FC2_OT_material_add, FC2_OT_material_remove, FC2_OT_material_shader, FC2_PT_material)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
