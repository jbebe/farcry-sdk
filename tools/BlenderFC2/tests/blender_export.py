# Import a model into Blender, export it straight back, and compare bytes.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_export.py
#
# An exporter that cannot reproduce a file it did not change is not going to be
# trusted with one it did. Editing a vertex then re-exporting is checked too, so
# a writer that simply copied the source would fail here.

import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of these packages that an installed
# extension already put in sys.modules.
from _corpus import GRAPHICS, describe_difference, present

import bmesh
import bpy

from addon import export_xbg, import_xbg
from fc2fmt.mesh import extract
from fc2fmt.xbg import XbgFile

MODELS = (
    ("ak47", os.path.join(GRAPHICS, "weapons", "primary", "ak47", "ak47.xbg")),
    ("buggy", os.path.join(GRAPHICS, "vehicles", "land", "buggy", "buggy.xbg")),
    ("character", os.path.join(GRAPHICS, "actors", "buddy_andrehyppolite",
                               "andrehyppolite.xbg")),
)


def fail(message):
    print("FAIL %s" % message)
    return 1


def round_trip(label, path, directory):
    """Import, export untouched, and require the source file back byte for byte."""
    original = open(path, "rb").read()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    out = os.path.join(directory, label + ".xbg")
    written = export_xbg.save(out, result["collection"])

    produced = open(out, "rb").read()
    if produced != original:
        return fail("%s: %s" % (label, describe_difference(original, produced)))
    print("%s: %d parts exported, %d bytes identical" % (label, written["parts"],
                                                         len(produced)))
    return 0


def edit_shows_up(label, path, directory):
    """Move one vertex and check exactly that much moves in the file."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    model = result["model"]
    step = model.pos_scale

    obj = result["parts"][0]
    moved = obj.data.vertices[0]
    before = tuple(moved.co)
    moved.co = (before[0] + step * 64, before[1], before[2])

    out = os.path.join(directory, label + "_edited.xbg")
    export_xbg.save(out, result["collection"])
    produced = XbgFile.parse(open(out, "rb").read())

    if produced.write() != open(out, "rb").read():
        return fail("%s: the edited file does not re-serialise" % label)

    source = extract(model, 0, place=False)[0]
    written = extract(produced, 0, place=False)[0]
    if len(written.positions) != len(source.positions):
        return fail("%s: vertex count changed" % label)
    deltas = [max(abs(a[i] - b[i]) for i in range(3))
              for a, b in zip(source.positions, written.positions)]
    if abs(max(deltas) - step * 64) > step:
        return fail("%s: moved a vertex by %g, file shows %g"
                    % (label, step * 64, max(deltas)))
    if sum(1 for d in deltas if d > step / 2) != 1:
        return fail("%s: %d vertices moved, expected 1"
                    % (label, sum(1 for d in deltas if d > step / 2)))
    print("%s: a one-vertex edit lands, and nothing else moves" % label)
    return 0


def topology_change(label, path, directory):
    """Subdivide a part, so the file has to carry a vertex count it never had."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    obj = result["parts"][0]
    before = len(obj.data.vertices)

    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    bmesh.ops.subdivide_edges(mesh, edges=mesh.edges[:], cuts=1, use_grid_fill=True)
    mesh.to_mesh(obj.data)
    mesh.free()
    obj.data.update()
    grown = len(obj.data.vertices)

    out = os.path.join(directory, label + "_subdivided.xbg")
    written = export_xbg.save(out, result["collection"])
    produced = XbgFile.parse(open(out, "rb").read())
    errors = 0

    if written["resized"] != 1:
        errors += fail("%s: %d parts resized, expected 1" % (label, written["resized"]))
    if grown <= before:
        return fail("%s: subdividing did not add vertices" % label)
    if produced.write() != open(out, "rb").read():
        errors += fail("%s: the subdivided file does not re-serialise" % label)

    parts = extract(produced, 0, place=False)
    if len(parts[0].positions) != grown:
        errors += fail("%s: file has %d vertices, Blender had %d"
                       % (label, len(parts[0].positions), grown))

    # Culling reads the bounds, so a stale one makes the part disappear in game.
    desc = produced.skin_descs[parts[0].submesh]
    low, high = desc.aabb
    for point in parts[0].positions:
        if any(not low[a] - 1e-3 <= point[a] <= high[a] + 1e-3 for a in range(3)):
            errors += fail("%s: a vertex sits outside the part's refitted box" % label)
            break
    if not errors:
        print("%s: %d vertices became %d, bounds refitted" % (label, before, grown))
    return errors


def main():
    if not present():
        print("corpus not present, skipping")
        return 0
    errors = 0
    with tempfile.TemporaryDirectory() as directory:
        for label, path in MODELS:
            if not os.path.exists(path):
                continue
            errors += round_trip(label, path, directory)
        errors += edit_shows_up("ak47", MODELS[0][1], directory)
        errors += topology_change("ak47", MODELS[0][1], directory)
    print("blender export: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
