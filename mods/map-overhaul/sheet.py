"""The sheet as a model: SIZE metres square, open in 3x3 panels, each a rigid plate on a hinge bone at its
fold, so the clips can flex a fold by turning its hinge. The centre panel hangs on the map's root bone,
the middle row's and column's on it, and the corners on the middle row's; each rests a few degrees off
flat, and pillows out a little between creases that lie flat. The map's four corner bones stay, at the
sheet's corners, for the strategy's markers. The front draws the whole sheet, the back plain paper.

Built into a copy of the map's pack; the mesh is left with no material for `fc2model add-material` to add
the sheet's own as its first. The model's box and each node's place in it go to <nodes.json>, for the
archetype.

    python sheet.py <map.fc2model> <out.fc2model> <nodes.json>
"""
import json
import math
import sys
import zipfile
import zlib

SOURCE, TARGET, PLACES = sys.argv[1], sys.argv[2], sys.argv[3]
MODEL = "graphics/objects/mapcompass/sheet/sheet.xbg"
RIG = "graphics/objects/mapcompass/sheet/sheet_ref.skeleton"

# The sheet at 1:10 000, its left edge where the hand holds the map, centred on the map's height and plane.
SIZE = 0.512
PANEL = SIZE / 3
LEFT_EDGE, PLANE = -0.150, 0.061
CENTRE = [LEFT_EDGE + SIZE / 2, 0.0, PLANE]
# How far each panel pillows toward the eye, and the cells across a panel's front.
PILLOW = 0.0015
CELLS = 8
# A spot of plain paper on the sheet, for the backs: its top-left margin.
BLANK = (0.02, 0.98)
# How far the markers stand in front of the paper, as on the map.
MARKER_LIFT = 0.004

H = PANEL / 2
# Each panel: its parent, its hinge in the parent's frame, the axis and degrees it turns about there, and
# where it lies from its hinge, x then y. A positive turn about y brings a left panel's outer edge toward
# the eye, about x tips a top panel back.
PANELS = {
    "PanelC": ("Map", CENTRE, None, 0, (-H, H), (-H, H)),
    "PanelT": ("PanelC", [0, H, 0], "x", -4, (-H, H), (0, PANEL)),
    "PanelB": ("PanelC", [0, -H, 0], "x", -3, (-H, H), (-PANEL, 0)),
    "PanelL": ("PanelC", [-H, 0, 0], "y", 6, (-PANEL, 0), (-H, H)),
    "PanelR": ("PanelC", [H, 0, 0], "y", -6, (0, PANEL), (-H, H)),
    "PanelTL": ("PanelT", [-H, 0, 0], "y", 6, (-PANEL, 0), (0, PANEL)),
    "PanelTR": ("PanelT", [H, 0, 0], "y", -6, (0, PANEL), (0, PANEL)),
    "PanelBL": ("PanelB", [-H, 0, 0], "y", 6, (-PANEL, 0), (-PANEL, 0)),
    "PanelBR": ("PanelB", [H, 0, 0], "y", -6, (0, PANEL), (-PANEL, 0)),
}
CORNERS = {
    "UpperLeftCorner": (-1, 1), "LowerLeftCorner": (-1, -1), "LowerRightCorner": (1, -1), "UpperRightCorner": (1, 1),
}


def turn(axis, degrees):
    """[x, y, z, w] of a turn about the x or y axis."""
    half = math.radians(degrees) / 2
    if axis is None:
        return [0.0, 0.0, 0.0, 1.0]
    return [math.sin(half), 0.0, 0.0, math.cos(half)] if axis == "x" else [0.0, math.sin(half), 0.0, math.cos(half)]


def multiply(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


def rotate(q, v):
    x, y, z, w = q
    t = [2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0])]
    return [v[0] + w * t[0] + (y * t[2] - z * t[1]), v[1] + w * t[1] + (z * t[0] - x * t[2]),
            v[2] + w * t[2] + (x * t[1] - y * t[0])]


def sphere(low, high):
    return ([(lo + hi) / 2 for lo, hi in zip(low, high)],
            math.sqrt(sum(((hi - lo) / 2) ** 2 for lo, hi in zip(low, high))))


def sheet_origin(name):
    """Where the panel's hinge lies on the flat sheet, from the sheet's centre."""
    parent, hinge = PANELS[name][0], PANELS[name][1]
    if parent == "Map":
        return [0.0, 0.0]
    origin = sheet_origin(parent)
    return [origin[0] + hinge[0], origin[1] + hinge[1]]


def panel(name):
    """The panel's front and back, in its hinge's frame: positions, uvs, normals and triangles of each."""
    (x0, x1), (y0, y1) = PANELS[name][4], PANELS[name][5]
    ox, oy = sheet_origin(name)
    front = {"positions": [], "uvs": [], "normals": [], "indices": []}
    for i in range(CELLS + 1):
        for j in range(CELLS + 1):
            a, b = i / CELLS, j / CELLS
            x, y = x0 + (x1 - x0) * a, y0 + (y1 - y0) * b
            z = PILLOW * math.sin(math.pi * a) * math.sin(math.pi * b)
            dzdx = PILLOW * math.pi / (x1 - x0) * math.cos(math.pi * a) * math.sin(math.pi * b)
            dzdy = PILLOW * math.pi / (y1 - y0) * math.sin(math.pi * a) * math.cos(math.pi * b)
            length = math.sqrt(dzdx * dzdx + dzdy * dzdy + 1)
            front["positions"] += [x, y, z]
            front["uvs"] += [(x + ox) / SIZE + 0.5, (y + oy) / SIZE + 0.5]
            front["normals"] += [-dzdx / length, -dzdy / length, 1 / length]
    for i in range(CELLS):
        for j in range(CELLS):
            a = i * (CELLS + 1) + j
            b, c, d = a + 1, a + CELLS + 2, a + CELLS + 1
            # Clockwise seen from +z, the side facing the eye.
            front["indices"] += [a, b, c, c, d, a]
    back = {"positions": [x0, y0, 0, x1, y0, 0, x1, y1, 0, x0, y1, 0], "uvs": list(BLANK) * 4,
            "normals": [0, 0, -1] * 4, "indices": [0, 1, 2, 2, 3, 0]}
    return front, back


with zipfile.ZipFile(SOURCE) as pack:
    files = {name: pack.read(name) for name in pack.namelist()}
template = json.loads(files["model/mesh.json"])
rig = json.loads(files["model/rig.json"])
root_bone = rig["bones"][0]

# The root, the four corners and the panels, in that order; each bone's links follow from its parent.
names = ["Map"] + list(CORNERS) + list(PANELS)
parents = [None] + ["Map"] * len(CORNERS) + [PANELS[n][0] for n in PANELS]
local = {"Map": ([0.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0])}
for corner, (sx, sy) in CORNERS.items():
    local[corner] = ([CENTRE[0] + sx * SIZE / 2, CENTRE[1] + sy * SIZE / 2, PLANE + MARKER_LIFT], [0.0, 0.0, 0.0, 1.0])
for name, (parent, hinge, axis, degrees, _, _) in PANELS.items():
    local[name] = (list(hinge), turn(axis, degrees))

nodes, bones = [], []
for index, (name, parent) in enumerate(zip(names, parents)):
    parent_index = names.index(parent) if parent else None
    children = [k for k, p in enumerate(parents) if p == name]
    later = [k for k, p in enumerate(parents) if p == parent and k > index] if parent else []
    translation, rotation = local[name]
    nodes.append({"name": name, "parent": 4294967295 if parent is None else parent_index, "rotation": rotation,
                  "translation": translation, "scale": [1, 1, 1], "skin_index": -1, "extent": 0})
    bones.append(dict(root_bone, name=name, name_hash=zlib.crc32(name.encode()), id=index,
                      parent=65535 if parent is None else parent_index,
                      first_child=children[0] if children else 65535, next_sibling=later[0] if later else 65535,
                      child_to_parent=[-c for c in rotation], local_offset=translation,
                      length=math.sqrt(sum(c * c for c in translation)),
                      animated_translation=1 if parent is None else 0))

places = {}
for name, parent in zip(names, parents):
    translation, rotation = local[name]
    if parent is None:
        places[name] = (translation, rotation)
    else:
        position, facing = places[parent]
        places[name] = ([a + b for a, b in zip(position, rotate(facing, translation))], multiply(facing, rotation))

parts, geometry = [], []
box_low, box_high = [1e9] * 3, [-1e9] * 3
for name in PANELS:
    front, back = panel(name)
    positions, uvs, normals, indices = [], [], [], []
    for face in (front, back):
        base = len(positions) // 3
        positions += face["positions"]
        uvs += face["uvs"]
        normals += face["normals"]
        indices += [base + k for k in face["indices"]]
    count = len(positions) // 3
    lows = [min(positions[i::3]) for i in range(3)]
    highs = [max(positions[i::3]) for i in range(3)]
    centre, radius = sphere(lows, highs)
    parts.append({"name": name + "_LOD0", "lod_metric": 10, "bounds": centre + [radius] + lows + highs,
                  "placement_node": names.index(name),
                  "clusters": [{"material_index": 0, "flags": 3018, "palette": [-1] * 48}]})
    geometry.append({"buffer": 0, "part": len(parts) - 1, "cluster": 0, "vertex_count": count,
                     "positions": positions, "uvs": uvs, "uvs1": [-1.0000305, 2.0000305] * count, "normals": normals,
                     "tangents": [1.0, 0.0, 0.0] * count, "binormals": [0.0, 1.0, 0.0] * count,
                     "colours": [1.0] * (count * 4), "indices": indices})
    position, facing = places[name]
    for i in range(0, len(positions), 3):
        p = [a + b for a, b in zip(position, rotate(facing, positions[i:i + 3]))]
        box_low = [min(a, b) for a, b in zip(box_low, p)]
        box_high = [max(a, b) for a, b in zip(box_high, p)]

centre, radius = sphere(box_low, box_high)
for node in nodes:
    node["extent"] = radius * radius
json.dump({"box": box_low + box_high,
           "nodes": {name: (position, [facing[3], facing[0], facing[1], facing[2]])
                     for name, (position, facing) in places.items()}}, open(PLACES, "w"), indent=1)

mesh = dict(template, materials=[], box=box_low + box_high, sphere=centre + [radius], nodes=nodes,
            parts=parts, lods=[{"distance": 10, "buffer_flags": [3018], "geometry": geometry}])
rig["bones"] = bones
manifest = json.loads(files["manifest.json"])
manifest["model"] = MODEL
for entry in manifest["entries"]:
    if entry["file"] == "model/mesh.json":
        entry["path"] = MODEL
    elif entry["file"] == "model/rig.json":
        entry["path"] = RIG
files["manifest.json"] = json.dumps(manifest, indent=2).encode()
files["model/mesh.json"] = json.dumps(mesh).encode()
files["model/rig.json"] = json.dumps(rig).encode()
with zipfile.ZipFile(TARGET, "w", zipfile.ZIP_DEFLATED) as pack:
    for name, data in files.items():
        pack.writestr(name, data)
print("sheet: %.3f m in 3x3, box %s..%s" % (SIZE, [round(v, 3) for v in box_low], [round(v, 3) for v in box_high]))
