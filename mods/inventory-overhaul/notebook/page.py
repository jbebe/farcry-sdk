"""The notebook's pages as a model: two pages meeting at the spine at TILT degrees each, each a chain
of STRIPS rigid strips on hinge bones (Map -> PageL1..PageL4, Map -> PageR1..PageR4), so the clips can
bend a page by turning its hinges. Built into a copy of the map's pack, which keeps its banks and body.

The sheet the plugin captures maps onto the pages: the Magma spread's own pixels x 0..1012, y TOP..716,
the picture's main sheets (x 81..931) as wide as the old dossier's pages, its spiral (x 503) at the spine.
The mesh is left with no material for `fc2model add-material` to add the pages' own as its first. Each
node's place in the model goes to <nodes.json>, for the archetype.

    python page.py <map.fc2model> <out.fc2model> <nodes.json>
"""
import json
import math
import sys
import zipfile
import zlib

SOURCE, TARGET, PLACES = sys.argv[1], sys.argv[2], sys.argv[3]
MODEL = "graphics/objects/equipment/notebook/notebook.xbg"
RIG = "graphics/objects/equipment/notebook/notebook_ref.skeleton"

# The spread's pixels: the sheet, the main sheets' edges, the spiral, and the row at the map's y 0.
SHEET_W, TOP, BOTTOM = 1012, -40, 716
MAIN_LEFT, MAIN_RIGHT, SPINE, MIDDLE = 81, 931, 503, 352
# Where the old dossier's pages were in the map's frame: their left edge, width and plane.
LEFT_EDGE, WIDTH, PLANE = -0.150, 0.552, 0.051
TILT = 5.0
STRIPS = 4
# A spot of plain paper, for the backs.
BLANK = (300, 660)

SCALE = WIDTH / (MAIN_RIGHT - MAIN_LEFT)
TILT_R = math.radians(TILT)
SPINE_X = LEFT_EDGE + (SPINE - MAIN_LEFT) * SCALE
SPINE_Z = PLANE - (SPINE - MAIN_LEFT) * SCALE * math.sin(TILT_R)


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def sub(a, b):
    return [x - y for x, y in zip(a, b)]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def quaternion(x_axis, y_axis, z_axis):
    """[x, y, z, w] of the rotation whose columns are the three axes."""
    m = [[x_axis[i], y_axis[i], z_axis[i]] for i in range(3)]
    trace = m[0][0] + m[1][1] + m[2][2]
    if trace > 0:
        s = math.sqrt(trace + 1.0) * 2
        return [(m[2][1] - m[1][2]) / s, (m[0][2] - m[2][0]) / s, (m[1][0] - m[0][1]) / s, 0.25 * s]
    i = max(range(3), key=lambda k: m[k][k])
    j, k = (i + 1) % 3, (i + 2) % 3
    s = math.sqrt(1.0 + m[i][i] - m[j][j] - m[k][k]) * 2
    q = [0.0, 0.0, 0.0, 0.0]
    q[i] = 0.25 * s
    q[j] = (m[j][i] + m[i][j]) / s
    q[k] = (m[k][i] + m[i][k]) / s
    q[3] = (m[k][j] - m[j][k]) / s
    return q


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
    """The centre and radius of the sphere around a box."""
    return ([(lo + hi) / 2 for lo, hi in zip(low, high)],
            math.sqrt(sum(((hi - lo) / 2) ** 2 for lo, hi in zip(low, high))))


def uv(x, y):
    return [x / SHEET_W, (BOTTOM - y) / (BOTTOM - TOP)]


with zipfile.ZipFile(SOURCE) as pack:
    files = {name: pack.read(name) for name in pack.namelist()}
template = json.loads(files["model/mesh.json"])
rig = json.loads(files["model/rig.json"])
root_bone = rig["bones"][0]

nodes = [dict(template["nodes"][0])]
bones = [dict(root_bone, first_child=1)]
parts, geometry = [], []
for side, sign, outer in (("L", -1, 0), ("R", 1, SHEET_W)):
    # The page's own frame: x out along it, y up, z = x cross y.
    out = [sign * math.cos(TILT_R), 0.0, math.sin(TILT_R)]
    up = [0.0, 1.0, 0.0]
    frame_z = cross(out, up)
    rotation = quaternion(out, up, frame_z)
    # The side facing the eye, in the page's frame, then the back.
    normal = [-out[2] * sign, 0.0, out[0] * sign]
    front = [dot(normal, out), dot(normal, up), dot(normal, frame_z)]
    faces = (front, [-c for c in front])
    length = abs(outer - SPINE) * SCALE / STRIPS
    first = len(nodes)
    for k in range(STRIPS):
        name = "Page%s%d" % (side, k + 1)
        index = len(nodes)
        parent = 0 if k == 0 else index - 1
        translation = [SPINE_X, 0.0, SPINE_Z] if k == 0 else [length, 0.0, 0.0]
        node_rotation = rotation if k == 0 else [0.0, 0.0, 0.0, 1.0]
        nodes.append({"name": name, "parent": parent, "rotation": node_rotation, "translation": translation,
                      "scale": [1, 1, 1], "skin_index": -1, "extent": 0})
        bones.append(dict(root_bone, name=name, name_hash=zlib.crc32(name.encode()), id=index, parent=parent,
                          first_child=index + 1 if k < STRIPS - 1 else 65535,
                          next_sibling=65535 if k > 0 or side == "R" else first + STRIPS,
                          child_to_parent=[-c for c in node_rotation], local_offset=translation,
                          length=math.sqrt(dot(translation, translation)), animated_translation=0))
        # The strip in its bone's frame, front then back; the spread's x and y at each corner.
        inner_x = SPINE + sign * k * length / SCALE
        outer_x = SPINE + sign * (k + 1) * length / SCALE
        corners = [(0.0, inner_x, BOTTOM), (0.0, inner_x, TOP), (length, outer_x, TOP), (length, outer_x, BOTTOM)]
        positions, uvs, normals, tangents, binormals = [], [], [], [], []
        for face, face_normal in enumerate(faces):
            for along, x, y in corners:
                positions += [along, (MIDDLE - y) * SCALE, 0.0]
                uvs += uv(x, y) if face == 0 else uv(*BLANK)
                normals += face_normal
                tangents += [sign, 0.0, 0.0]
                binormals += [0.0, 1.0, 0.0]
        # Clockwise as seen from the side each face looks at.
        indices = []
        for face, face_normal in enumerate(faces):
            base = face * 4
            a, b, c, d = (positions[(base + i) * 3:(base + i) * 3 + 3] for i in range(4))
            winding = [0, 1, 2, 2, 3, 0]
            if dot(cross(sub(b, a), sub(c, a)), face_normal) > 0:
                winding = [0, 2, 1, 2, 0, 3]
            indices += [base + i for i in winding]
        lows = [min(positions[i::3]) for i in range(3)]
        highs = [max(positions[i::3]) for i in range(3)]
        centre, radius = sphere(lows, highs)
        parts.append({"name": name + "_LOD0", "lod_metric": 10, "bounds": centre + [radius] + lows + highs,
                      "placement_node": index,
                      "clusters": [{"material_index": 0, "flags": 3018, "palette": [-1] * 48}]})
        geometry.append({"buffer": 0, "part": len(parts) - 1, "cluster": 0, "vertex_count": 8, "positions": positions,
                         "uvs": uvs, "uvs1": [-1.0000305, 2.0000305] * 8, "normals": normals, "tangents": tangents,
                         "binormals": binormals, "colours": [1.0] * 32, "indices": indices})

# Each node's place in the model, and the model's box over every strip placed by its node.
places = {}
for node in nodes:
    if node["parent"] < len(nodes):
        position, turn = places[nodes[node["parent"]]["name"]]
        places[node["name"]] = ([a + b for a, b in zip(position, rotate(turn, node["translation"]))],
                                multiply(turn, node["rotation"]))
    else:
        places[node["name"]] = (node["translation"], node["rotation"])
box_low, box_high = [1e9] * 3, [-1e9] * 3
for part, g in zip(parts, geometry):
    position, turn = places[nodes[part["placement_node"]]["name"]]
    for i in range(0, len(g["positions"]), 3):
        p = [a + b for a, b in zip(position, rotate(turn, g["positions"][i:i + 3]))]
        box_low = [min(a, b) for a, b in zip(box_low, p)]
        box_high = [max(a, b) for a, b in zip(box_high, p)]
centre, radius = sphere(box_low, box_high)
for node in nodes:
    node["extent"] = radius * radius
json.dump({name: (position, [turn[3], turn[0], turn[1], turn[2]]) for name, (position, turn) in places.items()},
          open(PLACES, "w"), indent=1)

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
print("pages: spine at x %.3f z %.3f, strips %.3f / %.3f m, box %s..%s" % (
    SPINE_X, SPINE_Z, SPINE * SCALE / STRIPS, (SHEET_W - SPINE) * SCALE / STRIPS,
    [round(v, 3) for v in box_low], [round(v, 3) for v in box_high]))
