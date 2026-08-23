# Import a pack into Blender, export it back, and require the game file it came
# from.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_export.py
#
# The comparison is made on the .xbg, not on the pack: JackAll writes the pack
# and JackAll turns it back into a container, so checking the JSON would only
# ever confirm that this side agrees with itself. Going the whole way round -
# Blender to pack to game file - is what says the two codebases still agree.
#
# An exporter that cannot reproduce a file it did not change is not going to be
# trusted with one it did, so an edit is checked too.

import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

# _corpus first: it evicts any copy of the add-on that an installed extension
# already put in sys.modules.
from _corpus import CLI, describe_difference, find, pack, require_pack

import bmesh
import bpy

from addon import export_xbg, import_xbg, model as fc2model

MODELS = (
    ("ak47", "graphics/weapons/primary/ak47/ak47.xbg"),
    ("buggy", "graphics/vehicles/land/buggy/buggy.xbg"),
    ("character", "graphics/actors/buddy_andrehyppolite/andrehyppolite.xbg"),
)


def fail(message):
    print("FAIL %s" % message)
    return 1


def apply_pack(path, directory):
    """Turn a pack back into game files with JackAll, and read what it wrote."""
    out = os.path.join(directory, "applied")
    result = subprocess.run([CLI, "fc2model", "extract", path, "-o", out, "--all"],
                            capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("jackall could not apply the pack:\n%s\n%s"
                           % (result.stdout, result.stderr))
    written = {os.path.basename(f).lower(): f for f in find(".xbg", out)}
    return written


def source_bytes(game_path):
    """The shipped file a pack was built from, out of the corpus."""
    name = os.path.basename(game_path).lower()
    for found in find(".xbg"):
        if os.path.basename(found).lower() == name:
            return open(found, "rb").read()
    return None


def round_trip(label, game_path, directory):
    """Import, export untouched, and require the source file back byte for byte."""
    path = pack(game_path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    out = os.path.join(directory, label + ".fc2model")
    written = export_xbg.save(out, result["collection"])

    original = source_bytes(game_path)
    if original is None:
        print("%s: not in the corpus, skipped" % label)
        return 0

    produced = open(apply_pack(out, directory)[os.path.basename(game_path).lower()], "rb").read()
    if produced != original:
        return fail("%s: %s" % (label, describe_difference(original, produced)))
    print("%s: %d parts exported, %d bytes identical" % (label, written["parts"], len(produced)))
    return 0


def edit_shows_up(label, game_path, directory):
    """Move one vertex and check exactly that much moves in the file."""
    path = pack(game_path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = import_xbg.load(path, lod=0, with_textures=False)
    mesh = result["mesh"]
    # Positions are quantised on the way back into the container, so the edit has
    # to be several steps to be distinguishable from rounding.
    step = mesh["pos_compress"][1]

    obj = result["parts"][0]
    moved = obj.data.vertices[0]
    before = tuple(moved.co)
    moved.co = (before[0] + step * 64, before[1], before[2])

    out = os.path.join(directory, label + "_edited.fc2model")
    written = export_xbg.save(out, result["collection"])
    if not written["moved"]:
        return fail("%s: moving a vertex did not mark the mesh edited" % label)

    produced = apply_pack(out, directory)
    if os.path.basename(game_path).lower() not in produced:
        return fail("%s: applying the edited pack wrote no mesh" % label)

    source = fc2model.parts_at(mesh, 0, place=False)[0]
    after = _reimport(produced[os.path.basename(game_path).lower()], directory, label)
    if len(after.positions) != len(source.positions):
        return fail("%s: vertex count changed" % label)

    deltas = [max(abs(a[i] - b[i]) for i in range(3))
              for a, b in zip(source.positions, after.positions)]
    if abs(max(deltas) - step * 64) > step:
        return fail("%s: moved a vertex by %g, file shows %g"
                    % (label, step * 64, max(deltas)))
    if sum(1 for d in deltas if d > step / 2) != 1:
        return fail("%s: %d vertices moved, expected 1"
                    % (label, sum(1 for d in deltas if d > step / 2)))
    print("%s: a one-vertex edit lands, and nothing else moves" % label)
    return 0


def _reimport(xbg_path, directory, label):
    """Read a written .xbg back by packing it again - the only reader here."""
    out = os.path.join(directory, label + "_applied.fc2model")
    result = subprocess.run(
        [CLI, "fc2model", "export", xbg_path, "-o", out], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("jackall could not re-pack %s:\n%s" % (xbg_path, result.stderr))
    return fc2model.parts_at(import_xbg.Pack.load(out).mesh(), 0, place=False)[0]


def topology_change(label, game_path, directory):
    """Subdivide a part, so the file has to carry a vertex count it never had."""
    path = pack(game_path)
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

    out = os.path.join(directory, label + "_subdivided.fc2model")
    written = export_xbg.save(out, result["collection"])
    errors = 0

    if written["resized"] != 1:
        errors += fail("%s: %d parts resized, expected 1" % (label, written["resized"]))
    if grown <= before:
        return fail("%s: subdividing did not add vertices" % label)

    produced = apply_pack(out, directory)
    if os.path.basename(game_path).lower() not in produced:
        return fail("%s: applying the subdivided pack wrote no mesh" % label)

    part = _reimport(produced[os.path.basename(game_path).lower()], directory, label)
    if len(part.positions) != grown:
        errors += fail("%s: file has %d vertices, Blender had %d"
                       % (label, len(part.positions), grown))

    # Culling reads the bounds, so a stale one makes the part disappear in game.
    bounds = written["mesh"]["parts"][written["mesh"]["lods"][0]["geometry"][
        obj["fc2_submesh"]]["part"]]["bounds"]
    low, high = bounds[4:7], bounds[7:]
    for point in part.positions:
        if any(not low[a] - 1e-3 <= point[a] <= high[a] + 1e-3 for a in range(3)):
            errors += fail("%s: a vertex sits outside the part's refitted box" % label)
            break
    if not errors:
        print("%s: %d vertices became %d, bounds refitted" % (label, before, grown))
    return errors


def main():
    if not require_pack():
        return 0
    errors = 0
    with tempfile.TemporaryDirectory() as directory:
        for label, game_path in MODELS:
            try:
                errors += round_trip(label, game_path, directory)
            except RuntimeError as error:
                print("%s: %s" % (label, error))
        errors += edit_shows_up("ak47", MODELS[0][1], directory)
        errors += topology_change("ak47", MODELS[0][1], directory)
    print("blender export: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
