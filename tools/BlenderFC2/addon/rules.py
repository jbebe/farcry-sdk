# What a Far Cry 2 model is allowed to be, checked against a Blender scene.
#
# The rules run on the scene plus the mesh document an export would write, so a
# rule cannot fire on something export would not write, or miss something it
# would. Nothing here hardcodes a format constant: the ceilings come from the
# pack's own `limits`, so there is no second place for them to drift.
#
# An ERROR blocks the export. Everything else is a warning, because retail
# itself breaks plenty of guidelines and refusing those would make the plugin
# wrong about the game it is for.
#
# The gate that keeps this honest is that **every rule, warnings included,
# produces nothing on a shipped model**. Retail is the definition of valid, so a
# rule that fires on it is a wrong rule - that already killed the "vertex colour
# RGB is ignored" rule before a line of it was written, since it would have
# fired on 45.3% of retail LOD0 vertices.

from dataclasses import dataclass, field

ERROR = "ERROR"
WARNING = "WARNING"
INFO = "INFO"


@dataclass(frozen=True)
class Target:
    """What a finding is about, so a row can select it."""
    object: str = ""
    # scene | object | vertex | face | bone | material | image | part | clip
    kind: str = "scene"
    name: str = ""
    index: int = -1


@dataclass(frozen=True)
class Finding:
    severity: str
    code: str
    message: str
    target: Target = field(default_factory=Target)
    hint: str = ""


# ----------------------------------------------------------------- the scene

def check_scene(collection, objects, limits):
    """Rules about the collection and the objects in it.

    `objects` is a list of (object, part name, submesh) for everything export
    will look at, resolved by the caller so this file needs no bpy.
    """
    out = []
    out += _duplicate_parts(objects)
    out += _unknown_objects(collection, objects)
    return out


def _unknown_objects(collection, objects):
    """A mesh object export will silently skip.

    Export writes the parts it can name and passes over the rest, so a new
    object a modeler adds simply vanishes without a word - the purest form of
    "it looked right in Blender".
    """
    known = {entry[0] for entry in objects}
    return [Finding(
        ERROR, "part.unknown-object",
        "'%s' is not one of this model's parts, so exporting would skip it." % obj.name,
        Target(object=obj.name, kind="object"),
        "The format has a fixed part list. Edit an existing part, or delete this object.")
        for obj in collection.objects
        if obj.type == "MESH" and obj not in known]


def _duplicate_parts(objects):
    """Two objects claiming one part; export writes both and the last wins."""
    seen = {}
    out = []
    for obj, name, submesh in objects:
        if submesh in seen:
            out.append(Finding(
                ERROR, "part.duplicate",
                "'%s' and '%s' both write part %s, and only the last would survive."
                % (seen[submesh], obj.name, name),
                Target(object=obj.name, kind="part", name=name),
                "Delete or rename one of them."))
        seen[submesh] = obj.name
    return out


# ------------------------------------------------------------- the geometry

def check_geometry(mesh, entries, limits):
    """Rules about the geometry an export would write.

    `entries` is a list of (object name, submesh index, geometry dict) for the
    parts that were rebuilt, so every count here is the one that ships.
    """
    out = []
    max_triangles = limits.get("max_cluster_triangles", 21845)
    max_vertices = limits.get("max_buffer_vertices", 65535)
    max_slots = limits.get("max_palette_slots", 48)

    per_buffer = {}
    for name, submesh, geometry in entries:
        target = Target(object=name, kind="part", index=submesh)
        triangles = len(geometry["indices"]) // 3
        if triangles == 0:
            out.append(Finding(
                ERROR, "cluster.zero-triangles",
                "'%s' draws nothing. No shipped cluster does." % name, target,
                "Give it geometry, or delete the object and let the part keep what it had."))
        elif triangles > max_triangles:
            out.append(Finding(
                ERROR, "cluster.too-many-triangles",
                "'%s' draws %d triangles; the limit is %d."
                % (name, triangles, max_triangles), target,
                "Split it across parts, or decimate. The highest shipped is 21,351."))

        per_buffer.setdefault(geometry["buffer"], []).append((name, geometry["vertex_count"]))

    for buffer, parts in per_buffer.items():
        total = sum(count for _name, count in parts)
        if total > max_vertices:
            out.append(Finding(
                ERROR, "buffer.too-many-vertices",
                "%d parts share buffer %d with %d vertices between them; the limit is %d."
                % (len(parts), buffer, total, max_vertices),
                Target(kind="scene"),
                "The index is 16-bit. Decimate one of: %s."
                % ", ".join(name for name, _count in sorted(
                    parts, key=lambda p: -p[1])[:3])))

    out += _palette_rules(mesh, entries, max_slots)
    return out


def _palette_rules(mesh, entries, max_slots):
    """A skinned cluster can only address the bones its palette holds."""
    out = []
    for name, submesh, geometry in entries:
        weights = geometry.get("skin_weights")
        slots = geometry.get("skin_slots")
        if not weights or not slots:
            continue

        target = Target(object=name, kind="part", index=submesh)
        used = {slot for slot, weight in zip(slots, weights) if weight > 0.0}
        if len(used) > max_slots:
            out.append(Finding(
                ERROR, "skin.too-many-bones",
                "'%s' is weighted to %d bones; a cluster addresses %d."
                % (name, len(used), max_slots), target,
                "Merge influences, or split the part."))

        stride = max(1, len(weights) // max(1, geometry["vertex_count"]))
        unweighted = [index for index in range(geometry["vertex_count"])
                      if not any(weights[index * stride:(index + 1) * stride])]
        if unweighted:
            out.append(Finding(
                ERROR, "skin.unweighted-vertex",
                "%d vertices of '%s' are in no vertex group, so the engine would "
                "collapse them to the origin." % (len(unweighted), name),
                Target(object=name, kind="vertex", index=unweighted[0]),
                "Weight them, or delete them."))
    return out


# ------------------------------------------------------------- the materials

# Which Principled inputs the format carries, and what it does with them. A
# shader that samples no albedo at all is the trap that cost the most time on
# the first hand-built weapon: the `Weapon` shader has no diffuse slot, and 102
# shipped materials use it.
UNSUPPORTED_INPUTS = {
    "Metallic": ("channel.metallic",
                 "Dunia has no metalness. A PBR albedo measures 0.05-0.12 luma and reads "
                 "as black plastic; band it into 0.13-0.52 before it is compressed."),
    "Roughness": ("channel.roughness",
                  "Roughness is a scalar SpecularPower plus a per-texel SpecularTexture1. "
                  "Invert this into that slot or it is dropped."),
    "Emission Color": ("channel.emission",
                       "Generic and Weapon sample no emissive map. Use Unlit, or bake it "
                       "into the albedo."),
    "Subsurface Weight": ("channel.unsupported", "Nothing in the format carries subsurface."),
    "Transmission Weight": ("channel.unsupported", "Nothing in the format carries transmission."),
    "Coat Weight": ("channel.unsupported", "Nothing in the format carries a coat."),
    "Sheen Weight": ("channel.unsupported", "Nothing in the format carries sheen."),
    "Anisotropic": ("channel.unsupported", "Nothing in the format carries anisotropy."),
}

# The albedo slot each shader samples. A shader absent from here samples none.
ALBEDO_SLOTS = ("DiffuseTexture1", "SkinTexture", "FabricTexture")


def check_material(name, shader, slots, driven, tiling, owned):
    """One material's channels, against the slots it actually carries.

    `driven` maps a Principled input name to whether an image drives it, so this
    file stays free of bpy and the walk over the node graph happens once.
    """
    out = []
    target = Target(kind="material", name=name)

    if driven.get("Base Color") and not any(slot in slots for slot in ALBEDO_SLOTS):
        out.append(Finding(
            WARNING, "channel.weapon-no-albedo" if shader == "Weapon" else "channel.no-slot",
            "'%s' uses the %s shader, which samples no albedo, so a Base Color image "
            "would be dropped." % (name, shader or "unknown"), target,
            "Put the texture in DiffuseTexture1 with DiffuseTiling1 = 1,1, a mask that is "
            "green 0 and blue 1, and the colour in DiffuseColor1."
            if shader == "Weapon" else
            "Move it into a slot this shader samples: %s."
            % ", ".join(ALBEDO_SLOTS)))

    if driven.get("Normal") and "NormalTexture1" not in slots:
        out.append(Finding(
            WARNING, "channel.no-slot",
            "'%s' has no NormalTexture1 slot, so a normal map would be dropped." % name,
            target, "Add the slot in the material, or drop the map."))

    for input_name, (code, hint) in UNSUPPORTED_INPUTS.items():
        if driven.get(input_name):
            out.append(Finding(
                WARNING, code,
                "'%s' drives %s, which the format does not carry." % (name, input_name),
                target, hint))

    # A model-owned texture is painted onto this model's UVs, so tiling it is
    # almost always a mistake. Retail runs 6 to 12 on the shared detail maps
    # because those are meant to tile.
    for slot, path in slots.items():
        if slot.startswith("Diffuse") and owned(path) and tiling.get(
                slot.replace("Texture", "Tiling"), (1.0, 1.0)) != (1.0, 1.0):
            out.append(Finding(
                WARNING, "channel.tiling-mismatch",
                "'%s' tiles %s, which is this model's own texture, so it will repeat "
                "instead of landing on the UVs." % (name, slot), target,
                "Set %s to 1,1." % slot.replace("Texture", "Tiling")))
    return out


def check_edit(entry):
    """Editing something the model shares with others."""
    if entry.owned:
        return []
    return [Finding(
        ERROR, "entry.shared-edited",
        "'%s' is shared%s, so changing it would change every model that uses it."
        % (entry.path, " with %d other files" % (entry.usage - 1) if entry.usage else ""),
        Target(kind="material" if entry.kind == "material" else "image", name=entry.path),
        "Copy it to the model's own folder first, and point the material at the copy.")]
def check_mesh(name, submesh, corners, groups, slot_material, part_material):
    """Rules about one object's own mesh data.

    Everything here is a silent failure: the file has no way to store what the
    modeler did, so export drops it and the model looks subtly wrong in game and
    exactly right in Blender.
    """
    target = Target(object=name, kind="part", index=submesh)
    out = []

    for kind, count, first in corners:
        if not count:
            continue
        out.append(Finding(
            WARNING, "%s.split" % kind,
            "%d vertices of '%s' have corners that disagree about %s. The format stores "
            "one per vertex, so the first corner wins and the seam collapses."
            % (count, name, kind), Target(object=name, kind="vertex", index=first),
            "Split the vertices along the seam (Edge > Mark Sharp then Edge Split), which "
            "changes the topology and so regenerates the tangents."))

    out += _group_rules(name, target, groups)

    # The container keeps each cluster's material index, so pointing the slot at
    # a different material changes what Blender draws and nothing in the file.
    if slot_material and part_material and not _same_path(slot_material, part_material):
        out.append(Finding(
            WARNING, "material.assignment-ignored",
            "'%s' now uses a different material; the file keeps '%s' and the change "
            "would be lost." % (name, part_material),
            Target(object=name, kind="material", name=slot_material),
            "Edit the material the part already names, or point that material at "
            "different textures."))
    return out


def _same_path(a, b):
    return a.replace("\\", "/").lower() == b.replace("\\", "/").lower()


def _group_rules(name, target, groups):
    """`groups` is (vertex index, non-zero weight count, weight sum, limit)."""
    truncated = [entry for entry in groups if entry[1] > entry[3]]
    unnormalised = [entry for entry in groups if abs(entry[2] - 1.0) > 0.02]
    out = []
    if truncated:
        worst = max(entry[1] for entry in truncated)
        out.append(Finding(
            WARNING, "skin.influences-truncated",
            "%d vertices of '%s' are in more than %d vertex groups (up to %d); only the "
            "heaviest survive." % (len(truncated), name, truncated[0][3], worst),
            Target(object=name, kind="vertex", index=truncated[0][0]),
            "Limit the influences (Weight Paint > Weights > Limit Total)."))
    if unnormalised:
        out.append(Finding(
            WARNING, "skin.weights-unnormalised",
            "%d vertices of '%s' have weights that do not sum to 1, so the part would be "
            "drawn smaller or larger than it looks." % (len(unnormalised), name),
            Target(object=name, kind="vertex", index=unnormalised[0][0]),
            "Normalise them (Weight Paint > Weights > Normalize All)."))
    return out


def check_loose(name, submesh, loose):
    """A vertex no triangle references. Every shipped vertex is referenced."""
    if not loose:
        return []
    return [Finding(
        WARNING, "mesh.loose-vertex",
        "%d vertices of '%s' are in no triangle. They cost buffer space and draw nothing."
        % (len(loose), name),
        Target(object=name, kind="vertex", index=loose[0]),
        "Delete them (Mesh > Clean Up > Delete Loose).")]
