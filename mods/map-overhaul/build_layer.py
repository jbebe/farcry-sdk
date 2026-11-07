# Builds layer/ from the sheet and the game's own files: the sheet in place of world 1's map textures,
# its named places drawn again as town plans, and the archetype fragments that take the GPS out of the
# vehicles and the player's arrow off the map.
import argparse
import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

import render_map

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "scripts"))
from entity_fragments import fragment_path, prototypes, write

LAYER = HERE / "layer" / "mods"
# Each library by its path in the game, then in the extracted files.
LIBRARIES = {
    ("worlds", "world1", "generated", "entitylibrary.fcb"): "worlds/worlds/worlds/world1/generated/entitylibrary.fcb",
    ("worlds", "world2", "generated", "entitylibrary.fcb"): "worlds/worlds/worlds/world2/generated/entitylibrary.fcb",
    ("downloadcontent", "dlc1", "generated", "entitylibrary.fcb"):
        "downloadcontent/dlc1/entitylibrary/downloadcontent/dlc1/generated/entitylibrary.fcb",
}
# The field values each change replaces, and what with.
VEHICLE_GPS = ('<value name="archGPSVehicleArchetype" type="String">gadgets.Equipped.Compass_Vehicle</value>',
               '<value name="archGPSVehicleArchetype" type="String"></value>')
PLAYER_ARROW = ('<value name="archPlayerMarker" type="String">gadgets.ObjectiveIcons.PlayerPosition</value>',
                '<value name="archPlayerMarker" type="String"></value>')
# Lettering and lines on a town plan, against the sheet's; its ground is drawn 1024 px across.
PLAN_SYMBOLS = 3
TILE = 1024


def run(*args):
    result = subprocess.run([str(a) for a in args], capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"{Path(args[0]).name} {' '.join(map(str, args[1:3]))} failed:\n{result.stdout}{result.stderr}")


def texture(tools, original, image, work):
    # The game's file keeps its header and its levels: a _mip0 file is the top level alone, the other the
    # rest of the chain from half that size.
    cli, texconv = tools
    stem = "_".join(original.parts[-2:]).removesuffix(".xbt")
    copy = work / f"{stem}.xbt"
    shutil.copy(original, copy)
    run(cli, "xbt", "extract", copy)
    header = copy.with_suffix(".dds").read_bytes()[:128]
    height, width, _, _, levels = struct.unpack_from("<5I", header, 12)
    png = copy.with_suffix(".png")
    image.resize((width, height), Image.LANCZOS).save(png)
    run(texconv, "-nologo", "-f", "BC1_UNORM", "-m", max(levels, 1), "-dx9", "-y", "-o", work, png)
    out = LAYER / original.relative_to(original.parents[2])
    out.parent.mkdir(parents=True, exist_ok=True)
    run(cli, "xbt", "build", copy.with_suffix(".dds"), copy.with_suffix(".xml"), "-o", out)


def textures(source, tools, work):
    levels = source / "worlds/worlds/levels"
    render_map.draw((0, 0, render_map.WORLD, render_map.WORLD), 1, HERE / "assets/world1-sheet.png", False)
    sheet = Image.open(HERE / "assets/world1-sheet.png").convert("RGB")

    for r, row in enumerate("abcde"):
        for c in range(5):
            tile = sheet.crop((c * TILE, r * TILE, (c + 1) * TILE, (r + 1) * TILE))
            for original in sorted((levels / f"w1_{row}_{c + 1}").glob("mapcompass*.xbt")):
                texture(tools, original, tile, work)

    world = sheet.crop((1024, 1024, 4096, 4096))
    for original in sorted((source / "worlds/worlds/worlds/world1").glob("mapcompass*.xbt")):
        texture(tools, original, world, work)

    _, features, _ = render_map.world()
    planned = set()
    for e in features["entities"]:
        if not e.get("tex") or e["tex"] in planned:
            continue
        planned.add(e["tex"])
        (x, y), half = e["pos"][:2], e["w"] / 2
        png = work / Path(e["tex"]).with_suffix(".png").name
        render_map.draw((x - half, y - half, x + half, y + half), TILE / e["w"], png, False, PLAN_SYMBOLS)
        plan = Image.open(png).convert("RGB")
        for original in sorted(levels.glob(f"{Path(e['tex']).parent.name}/{Path(e['tex']).stem}*.xbt")):
            texture(tools, original, plan, work)


def fragments(source, cli, work):
    for library, extracted in LIBRARIES.items():
        xml = work / f"{library[1]}.xml"
        run(cli, "fcb", "decode", source / extracted, "-o", xml)
        lines = xml.read_text(encoding="utf-8").splitlines()
        for name, start, end in prototypes(lines):
            block = lines[start:end]
            text = "\n".join(block)
            change = PLAYER_ARROW if name == "gadgets.Equipped.Map" else VEHICLE_GPS
            if change[0] in text:
                write(fragment_path(LAYER, library, name), [l.replace(*change) for l in block])
                print(f"{'/'.join(library[:2])}: {name}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=str(REPO / "tmp/gamefiles"), help="the game's archives, extracted")
    parser.add_argument("--cli", default=str(REPO / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe"))
    parser.add_argument("--texconv", default=str(REPO / "tools/third-party/FCBConverter/texconv.exe"))
    args = parser.parse_args()
    source = Path(args.source)
    if LAYER.exists():
        shutil.rmtree(LAYER)
    with tempfile.TemporaryDirectory() as work:
        textures(source, (args.cli, args.texconv), Path(work))
        fragments(source, args.cli, Path(work))
    print(f"Wrote {LAYER}")


if __name__ == "__main__":
    main()
