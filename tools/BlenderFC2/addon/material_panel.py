# The Far Cry 2 material editor, in the Material properties and in the 3D view's sidebar: the
# material's document, laid out the way its shader's own property list groups it.
#
# `draw` reads only what is already cached; the pack's manifest and shader list are read once per
# file change, the way the clip list is.

import bpy
from bpy.props import EnumProperty, IntProperty, StringProperty

from . import material_data, panel
from .material_data import BROKEN, CLEAN, FLOAT, INTEGER, TEXTURE
from .pack import Pack, read_schema, same_path

# Each pack's entries and shader list, until the file changes.
_PACKS = {}

# The shader enum's items, kept alive for Blender the way the clip list is.
_SHADERS = []


def _read(path):
    manifest, shaders = read_schema(path)
    return Pack(manifest, {}), shaders


def _pack(context):
    """The entries and shader list of the pack the active object came from."""
    origin, _collection = panel.pack_of(context)
    return panel.cached(_PACKS, origin, _read, (None, {})) if origin else (None, {})


def _material(context):
    """The material being edited: the Material properties' own, or the active object's in the 3D view."""
    material = getattr(context, "material", None)
    if material is None and context.object is not None:
        material = context.object.active_material
    return material


def _owned(material, pack):
    """Whether export may write the material: one it adds, or one only this model draws with."""
    if material_data.is_new(material):
        return True
    entry = pack.entry(material.get(material_data.PROP_MATERIAL_PATH, "")) if pack else None
    return entry is not None and entry.owned


def _parameter(context, key):
    descriptor = _pack(context)[1].get(_material(context).fc2.shader)
    return next((p for p in descriptor["parameters"] if p["name"] == key), None) if descriptor else None


class _OnMaterial:
    """Something that acts on a Far Cry 2 material, there only when one is being edited."""

    @classmethod
    def poll(cls, context):
        return material_data.held(_material(context))


class FC2_OT_material_add(_OnMaterial, bpy.types.Operator):
    """Give the material this property, at the shader's default"""

    bl_idname = "material.fc2_add_value"
    bl_label = "Add"
    bl_options = {"REGISTER", "UNDO"}

    key: StringProperty()

    def execute(self, context):
        material = _material(context)
        parameter = _parameter(context, self.key)
        if parameter is None:
            self.report({"ERROR"}, "%s takes no %s" % (material.fc2.shader, self.key))
            return {"CANCELLED"}
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


class FC2_OT_material_remove(_OnMaterial, bpy.types.Operator):
    """Drop this property, so the shader's own default applies"""

    bl_idname = "material.fc2_remove_value"
    bl_label = "Remove"
    bl_options = {"REGISTER", "UNDO"}

    index: IntProperty()

    def execute(self, context):
        material = _material(context)
        material.fc2.values.remove(self.index)
        material_data.refresh(material)
        return {"FINISHED"}


def _shader_items(_self, context):
    global _SHADERS
    shaders = _pack(context)[1]
    _SHADERS = [(name, name, "") for name in sorted(shaders) if shaders[name]["name"] == name]
    return _SHADERS or [("", "This pack carries no shader list", "")]


class FC2_OT_material_shader(_OnMaterial, bpy.types.Operator):
    """Draw the material with another shader. Properties it does not take are listed apart"""

    bl_idname = "material.fc2_shader"
    bl_label = "Change Shader"
    bl_options = {"REGISTER", "UNDO"}

    shader: EnumProperty(name="Shader", items=_shader_items)

    def invoke(self, context, _event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        if not self.shader:
            return {"CANCELLED"}
        _material(context).fc2.shader = self.shader
        return {"FINISHED"}


class FC2_OT_material_copy(_OnMaterial, bpy.types.Operator):
    """Copy this material to a new one of the model's own, and draw the selected parts that use
    it with the copy"""

    bl_idname = "material.fc2_copy"
    bl_label = "Copy to a New Material"
    bl_options = {"REGISTER", "UNDO"}

    name: StringProperty(name="Name", description="The new material's own name")
    path: StringProperty(name="Game path",
                         description="Where the game will find it, beside the model by default")

    def invoke(self, context, _event):
        material = _material(context)
        self.name = material.fc2.name + "_OWN"
        self.path = material_data.beside_model(material, self.name, ".xbm")
        return context.window_manager.invoke_props_dialog(self, width=420)

    def execute(self, context):
        path = self.path.strip()
        if not self.name.strip() or not path.lower().endswith(".xbm"):
            self.report({"ERROR"}, "Give the material a name and a game path ending in .xbm")
            return {"CANCELLED"}
        pack = _pack(context)[0]
        if (pack and pack.entry(path)) or any(
                same_path(m.get(material_data.PROP_MATERIAL_PATH, ""), path) for m in bpy.data.materials):
            self.report({"ERROR"}, "%s is a material already" % path)
            return {"CANCELLED"}

        source = _material(context)
        new = material_data.copy(source, self.name.strip(), path)
        drawn = 0
        for obj in {*context.selected_objects, context.object} - {None}:
            for slot in obj.material_slots if obj.type == "MESH" else ():
                if slot.material == source:
                    slot.material = new
                    drawn += 1
        self.report({"INFO"}, "%s draws %d part(s); export adds it to the pack" % (path, drawn))
        return {"FINISHED"}


class _MaterialPanel(_OnMaterial):
    def draw(self, context):
        _draw(self.layout, context, _material(context))


class FC2_PT_material(_MaterialPanel, bpy.types.Panel):
    bl_idname = "FC2_PT_material"
    bl_label = "Far Cry 2"
    bl_space_type = "PROPERTIES"
    bl_region_type = "WINDOW"
    bl_context = "material"


class FC2_PT_material_sidebar(_MaterialPanel, panel.FC2_PT_base):
    bl_idname = "FC2_PT_material_sidebar"
    bl_label = "Material"


def _draw(layout, context, material):
    fc2 = material.fc2
    pack, shaders = _pack(context)
    owned = _owned(material, pack)

    layout.label(text=material.get(material_data.PROP_MATERIAL_PATH, ""), icon="MATERIAL")
    if not owned:
        panel.wrapped(layout.box(), "Shared with other models, so export refuses an edit. Copy it "
                                    "to change it.", icon="LOCKED", width=36)
    elif material_data.is_new(material):
        layout.label(text="New: export adds it to the pack.", icon="ADD")
    layout.operator(FC2_OT_material_copy.bl_idname, icon="DUPLICATE")

    # Every value's index by key, so the layout below never scans the values per parameter.
    by_key = {}
    for index, value in enumerate(fc2.values):
        by_key.setdefault(value.key, []).append(index)

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
    if any(_twin(by_key, key) is not None for key in by_key):
        body.prop(fc2, "link_clean")

    parameters = descriptor["parameters"] if descriptor else []
    declared = {p["name"]: p for p in parameters}
    groups = {}
    for parameter in parameters:
        if not _is_twin(parameter["name"], declared):
            groups.setdefault(parameter["group"], []).append(parameter)
    for group, members in groups.items():
        names = [p["name"] + suffix for p in members for suffix in ("", CLEAN, BROKEN)]
        if not fc2.show_all and not any(name in by_key for name in names):
            continue
        body.label(text=group)
        for parameter in members:
            _rows(body, fc2, by_key, parameter, parameter["name"])
            for suffix in (CLEAN, BROKEN):
                if parameter["name"] + suffix in declared:
                    _rows(body, fc2, by_key, declared[parameter["name"] + suffix], suffix)

    stray = [index for key, indices in by_key.items() if key not in declared for index in indices]
    if stray:
        body.label(text="Not taken by %s" % fc2.shader if descriptor else "Properties")
        for index in sorted(stray):
            _row(body, fc2, by_key, index, None)


def _is_twin(name, declared):
    """Whether this is the Clean or Broken twin of a value the shader declares."""
    return any(name.endswith(suffix) and name[:-len(suffix)] in declared for suffix in (CLEAN, BROKEN))


def _twin(by_key, key):
    """The index of the Clean twin of a value that carries both twins, as `clean_twin` decides."""
    if key.endswith(CLEAN) or key.endswith(BROKEN) or key + BROKEN not in by_key:
        return None
    return by_key.get(key + CLEAN, [None])[0]


def _rows(layout, fc2, by_key, parameter, label):
    """A parameter's value rows, or its unset row when every property is shown."""
    indices = by_key.get(parameter["name"], ())
    for index in indices:
        _row(layout, fc2, by_key, index, parameter, label)
    if not indices and fc2.show_all:
        _unset(layout, parameter, label)


def _row(layout, fc2, by_key, index, parameter, label=None):
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

    twin = _twin(by_key, value.key) if value.section == FLOAT else None
    if twin is not None and not fc2.link_clean \
            and tuple(fc2.values[twin].number) != tuple(value.number):
        layout.label(text="Differs from %s, which is what the game shows" % fc2.values[twin].key,
                     icon="ERROR")


def _label(layout, text, key):
    """A key, or a twin's suffix indented under the value it belongs to."""
    row = layout.row()
    if text != key:
        row.separator(factor=2.0)
    row.label(text=text)


def _unset(layout, parameter, label):
    split = layout.split(factor=0.42, align=True)
    split.active = False
    _label(split, label, parameter["name"])
    row = split.row(align=True)
    row.label(text="default %s" % parameter["default"] if parameter.get("default") else "unset")
    row.operator(FC2_OT_material_add.bl_idname, text="", icon="ADD").key = parameter["name"]


CLASSES = (FC2_OT_material_add, FC2_OT_material_remove, FC2_OT_material_shader, FC2_OT_material_copy,
           FC2_PT_material, FC2_PT_material_sidebar)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
