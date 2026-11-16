"""Writes the generated columns of the asset tables from the retail game files, keeping the hand columns.

Usage: python tools/asset-tags/build.py [--game-files <tmp/gamefiles>] [--cli <jackall-cli.exe>]
"""
import argparse
import collections
import os
import re
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

from tables import HAND, HERE, VARIANT, check, read, shortlist, write

REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "scripts"))
from entity_fragments import prototypes  # noqa: E402

# Archive folders under tmp/gamefiles, the later ones winning as the game's load order does.
ROOTS = [
    ("common", "common"),
    ("worlds", "worlds/worlds"),
    ("worlds", "worlds/worlds_english"),
    ("dlc1", "downloadcontent/dlc1/dlc1"),
    ("dlc_jungle", "downloadcontent/dlc_jungle/dlc_jungle"),
    ("patch", "patch"),
]
EXTENSIONS = (".xbg", ".xbm", ".xbt")
# Per-level map tiles and sector maps live elsewhere and are left out.
TEXTURE_FOLDERS = ("graphics\\", "ui\\")
SECTOR_SIZE = 64
SECTORS_PER_ROW = 80
PLACE_RADIUS = 200
PATH_RE = rb"graphics\\[ -~]+?\.%s"
COMPONENT_TRAITS = {
    "CStaticPhysComponent": "static",
    "CRigidPhysComponent": "rigid",
    "CCompoundPhysComponent": "breakable",
    "CIgnitorComponent": "flammable",
    "CPickupWeapon": "pickup",
    "CVehicle": "vehicle",
    "CPawn": "character",
    "CSimpleAnimationComponent": "animated",
    "CBuildingInfoComponent": "building",
}
NAME_TRAITS = {"bk": "broken-variant", "broken": "broken-variant", "destroyed": "broken-variant",
               "dest": "broken-variant", "lod": "lod", "mp": "mp"}


def game_files(root):
    """Every mesh, material and texture of the extracted archives by game path, with its archive and file."""
    files = {}
    for archive, folder in ROOTS:
        base = str(root / folder)
        for directory, subdirs, names in os.walk(base):
            if directory == base:
                subdirs[:] = [d for d in subdirs if d not in ("levels", "_unknown")]
            for name in names:
                if name.lower().endswith(EXTENSIONS):
                    path = os.path.join(directory, name)
                    files[path[len(base) + 1:].lower()] = (archive, path)
    return files


def paths_in(data, ext):
    return {p.decode() for p in re.findall(PATH_RE % ext, data.lower())}


def model_size(data):
    """The model's box from its XOBB chunk, as width x depth x height in metres."""
    at = data.find(b"XOBB")
    if at < 0:
        return ""
    size, payload = struct.unpack_from("<II", data, at + 8)
    box = struct.unpack_from("<6f", data, at + size - payload)
    return "x".join(f"{box[i + 3] - box[i]:.2f}" for i in range(3))


def texture_size(path):
    """Width x height from an .xbt's DDS header, when it has one."""
    with open(path, "rb") as f:
        head = f.read(256)
    at = head.find(b"DDS ")
    if at < 0:
        return ""
    height, width = struct.unpack_from("<II", head, at + 12)
    return f"{width}x{height}"


def archetypes(cli, library):
    """Archetype name -> (traits, meshes) from the decoded full entity library."""
    with tempfile.TemporaryDirectory() as tmp:
        xml = Path(tmp) / "entitylibrary.xml"
        subprocess.run([str(cli), "fcb", "decode", str(library), "-o", str(xml)], check=True, capture_output=True)
        lines = xml.read_text(encoding="utf-8").splitlines()
    result = {}
    for name, start, end in prototypes(lines):
        block = "\n".join(lines[start:end])
        meshes = set(re.findall(r'graphics\\[^"<]+?\.xbg', block.lower()))
        components = set(re.findall(r'<object type="(\w+)">', block))
        if meshes:
            result[name.removeprefix("IGE_Archetypes.")] = ({COMPONENT_TRAITS[c] for c in components & COMPONENT_TRAITS.keys()}, meshes)
    return result


def gazetteer():
    """(world, label, x, y) of every named place, from the survey's gazetteer tables."""
    page = REPO / "docs/docs/lore/set-dressing/reading-the-world.md"
    row = re.compile(r"^\| ([^|]+?) \| `[^`]+` \| `(w[12])_[^`]+` \| ([\d,]+), ([\d,]+) \|$", re.M)
    places = [(w, label, int(x.replace(",", "")), int(y.replace(",", "")))
              for label, w, x, y in row.findall(page.read_text(encoding="utf-8"))]
    if not places:
        sys.exit(f"no places in the gazetteer tables of {page}")
    return places


def nearest_place(places, world, sector):
    """The named place of the world nearest the sector's centre, within PLACE_RADIUS, or None."""
    row, column = divmod(sector, SECTORS_PER_ROW)
    x, y = (column + 0.5) * SECTOR_SIZE, (row + 0.5) * SECTOR_SIZE
    best, nearest = PLACE_RADIUS**2, None
    for w, label, px, py in places:
        distance = (px - x) ** 2 + (py - y) ** 2
        if w == world and distance <= best:
            best, nearest = distance, label
    return nearest


def placements(root, places):
    """Mesh -> (campaign sector files placing it, Counter of nearby named places)."""
    count = collections.Counter()
    near = collections.defaultdict(collections.Counter)
    for sector_file in (root / "worlds/worlds/levels").glob("w[12]_*/generated/worldsectors/*.data.fcb"):
        place = None
        if sector_file.name.startswith("worldsector"):
            number = int(re.search(r"(\d+)\.data", sector_file.name).group(1))
            place = nearest_place(places, sector_file.parts[-4][:2], number)
        for mesh in paths_in(sector_file.read_bytes(), b"xbg"):
            count[mesh] += 1
            if place:
                near[mesh][place] += 1
    return count, near


def name_traits(path):
    traits = {NAME_TRAITS[suffix] for suffix in VARIANT.findall(Path(path).stem)}
    return traits | {"mp"} if "\\mp\\" in path else traits


def majority(tag_sets):
    """The tags that at least half of the sets carry."""
    counts = collections.Counter(t for tags in tag_sets for t in tags)
    return {t for t, n in counts.items() if 2 * n >= len(tag_sets)}


def read_labels(path):
    """Path -> (category, detail) from the sound labeler's list."""
    rows = (line.split("\t") for line in path.read_text(encoding="utf-8").splitlines() if line and line[0] != "#")
    return {f[0]: (f[1], f[2]) for f in rows if len(f) == 5}


def keep_hand(row, old):
    kept = old.get(row["path"], {})
    row.update({c: kept.get(c, "") for c in HAND})
    return row


def main():
    from modstatus import MAIN_TREE

    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--game-files", type=Path, default=MAIN_TREE / "tmp/gamefiles")
    parser.add_argument("--cli", type=Path, default=MAIN_TREE / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe")
    args = parser.parse_args()

    files = game_files(args.game_files)
    library = archetypes(args.cli, args.game_files / "worlds/worlds/worlds/tmpla/generated/entitylibrary_full.fcb")
    placed, near = placements(args.game_files, gazetteer())

    mesh_archetypes = collections.defaultdict(list)
    mesh_traits = collections.defaultdict(set)
    for name, (traits, meshes) in sorted(library.items()):
        for mesh in meshes:
            if not name.endswith(".Multi"):
                mesh_archetypes[mesh].append(name)
            mesh_traits[mesh] |= traits

    material_textures = {}
    diffuse_siblings = collections.defaultdict(set)
    texture_users = collections.defaultdict(set)
    old_models = read("models")
    models = []
    for path in sorted(p for p in files if p.endswith(".xbg")):
        archive, disk = files[path]
        data = Path(disk).read_bytes()
        materials = paths_in(data, b"xbm") & files.keys()
        for material in materials - material_textures.keys():
            textures = material_textures[material] = paths_in(Path(files[material][1]).read_bytes(), b"xbt")
            for texture in textures - {t for t in textures if t.endswith("_d.xbt")}:
                diffuse_siblings[texture] |= {t for t in textures if t.endswith("_d.xbt")}
        for material in materials:
            for texture in material_textures[material]:
                texture_users[texture].add(path)
        diffuse = sorted({Path(t).stem for m in materials for t in material_textures[m] if t.endswith("_d.xbt")})
        models.append(keep_hand({
            "path": path,
            "size": model_size(data),
            "placed": placed.get(path, 0),
            "places": shortlist(f"{p} {n}" for p, n in near[path].most_common()),
            "archetypes": shortlist(mesh_archetypes[path]),
            "textures": shortlist(diffuse),
            "traits": " ".join(sorted(mesh_traits[path] | name_traits(path))),
            "archive": archive,
        }, old_models))
    write("models", models)

    model_tags = {m["path"]: set(m["tags"].split()) for m in models}
    old_textures = read("textures")
    textures = []
    for path in sorted(p for p in files if p.endswith(".xbt") and p.startswith(TEXTURE_FOLDERS) and not p.endswith("_mip0.xbt")):
        archive, disk = files[path]
        users = sorted(texture_users[path])
        inherited = majority([model_tags[u] for u in users])
        for sibling in diffuse_siblings[path]:
            inherited |= set(old_textures.get(sibling, {}).get("tags", "").split())
        textures.append(keep_hand({
            "path": path,
            "size": texture_size(disk),
            "users": len(users),
            "used_by": shortlist(Path(u).stem for u in users),
            "inherited": " ".join(sorted(inherited)),
            "archive": archive,
        }, old_textures))
    write("textures", textures)

    labels = read_labels(REPO / "tools/JackAll/assets/fc2.soundlabels.tsv")
    old_sounds = read("sounds")
    sounds = [keep_hand({"path": p, "category": c, "label": d}, old_sounds) for p, (c, d) in sorted(labels.items())]
    write("sounds", sounds)

    print(f"{len(models)} models, {len(textures)} textures, {len(sounds)} sounds, {len(library)} archetypes")
    for table, rows in (("models", models), ("textures", textures), ("sounds", sounds)):
        bad = check(table, rows)
        if bad:
            print(f"{table}: {len(bad)} kinds or tags not in the vocabulary, first: {shortlist(f'{p} {w!r}' for p, w in bad)}")


if __name__ == "__main__":
    main()
