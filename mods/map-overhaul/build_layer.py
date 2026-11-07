# Builds layer/ from the sheet and the game's own files: the sheet in place of world 1's map textures,
# its named places drawn again as town plans, the sheet as the model held in the hand, and the archetype
# fragments that put that model on the map and take the GPS out of the vehicles and the player's arrow
# off the map.
import argparse
import json
import re
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
from entity_fragments import crc, fragment_path, prototypes, replace_hashed, write

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
# The sheet in the hand: its files, the size its texture is drawn at, and the map's paper material it
# copies, Map flag and all, which is how the paper is lit. The game puts the area map on the material
# named MAPTOP, so a copy under another name keeps its own texture.
SHEET = Path(r"graphics\objects\mapcompass\sheet")
SHEET_FILES = ("sheet.xbg", "sheet_ref.skeleton", "sheet.xbm")
SHEET_TEXTURE = 4096
MAPTOP = r"GRAPHICS\_MATERIALS\JFCARRIER2-M-1708200746783136.xbm"


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


def sheet_model(game, tools, work):
    # sheet.py's panels, drawn with a copy of the map's paper material that shows the whole sheet.
    cli, texconv = tools
    run(cli, "fc2model", "export", "-g", game, "graphics/objects/mapcompass/map.xbg", "-o", work / "map.fc2model")
    run(sys.executable, HERE / "sheet.py", work / "map.fc2model", work / "sheet.fc2model", work / "sheet.json")
    run(cli, "fc2model", "add-material", work / "sheet.fc2model", SHEET / "sheet.xbm", "--from", MAPTOP, "--name", "SHEET")
    run(cli, "fc2model", "extract", work / "sheet.fc2model", "-o", work / "sheet", "--all")
    out = LAYER / SHEET
    out.mkdir(parents=True, exist_ok=True)
    for name in SHEET_FILES:
        shutil.copy(work / "sheet" / "mods" / SHEET / name, out / name)
    run(cli, "xbm", "set", out / "sheet.xbm", f"DiffuseTexture1={SHEET / 'sheet.xbt'}", "-g", game)

    png = work / "sheet.png"
    Image.open(HERE / "assets/world1-sheet.png").convert("RGB").resize((SHEET_TEXTURE,) * 2, Image.LANCZOS).save(png)
    run(texconv, "-nologo", "-f", "BC1_UNORM", "-m", 0, "-dx9", "-y", "-o", work, png)
    run(cli, "xbt", "build", work / "sheet.dds", "-o", out / "sheet.xbt")
    return json.loads((work / "sheet.json").read_text())


def held_sheet(text, sheet):
    # The map's archetype pointed at the sheet: its descriptor, a graphic object for each panel, its skeleton.
    def floats(values):
        return ",".join("%g" % v for v in values)

    model, skeleton = SHEET / "sheet.xbg", SHEET / "sheet_ref.skeleton"
    box = 'bboxMin="%s" bboxMax="%s"' % (floats(sheet["box"][:3]), floats(sheet["box"][3:]))
    panels = [name for name in sheet["nodes"] if name.startswith("Panel")]
    objects = "".join(f'<object index="{i}" meshName="{n}" boneName="{n}" {box} />' for i, n in enumerate(panels))
    bones = "".join(f'<bone name="{n}" pos="{floats(p)}" rot="{floats(r)}" />'
                    for n, (p, r) in sheet["nodes"].items() if n != "Map")
    descriptor = (f'<hidDescriptor><component class="GraphicComponent" version="2" detail="0">{objects}'
                  f'<resource fileName="{model}" {box} /><skeleton name="Map" pos="0,0,0" rot="1,-0,-0,-0">'
                  f'{bones}</skeleton></component></hidDescriptor>')
    text, count = re.subn(r"<hidDescriptor>.*?</hidDescriptor>", lambda m: descriptor, text, count=1, flags=re.S)
    assert count == 1

    graphic = re.search(r'( *)<object type="object">\s*<value name="hidIndex".*?</object>', text, flags=re.S)
    pad = graphic.group(1)
    replacement = "\n".join(
        "\n".join([f'{pad}<object type="object">',
                   f'{pad}  <value name="hidIndex" type="UInt32">{i}</value>',
                   f'{pad}  <value name="text_objModel" type="String">{model}</value>',
                   f'{pad}  <value name="objModel" type="Hash">{crc(str(model))}</value>',
                   f'{pad}  <value name="hidMeshName" type="String">{n}</value>',
                   f'{pad}  <value name="text_hidNodeName" type="String">{n}</value>',
                   f'{pad}  <value name="hidNodeName" type="Hash">{crc(n)}</value>',
                   f'{pad}  <value name="text_hidNodeNameLOD0" type="String">{n}_LOD0</value>',
                   f'{pad}  <value name="hidNodeNameLOD0" type="Hash">{crc(n + "_LOD0")}</value>',
                   f'{pad}  <value name="hidDetailObject" type="Bool">False</value>',
                   f"{pad}</object>"])
        for i, n in enumerate(panels))
    text = text[:graphic.start()] + replacement + text[graphic.end():]
    text = replace_hashed(text, "fileName", r"graphics\objects\mapcompass\map.xml", str(SHEET / "sheet.xml"))
    return replace_hashed(text, "fileSkeleton", r"graphics\objects\mapcompass\map_ref.skeleton", str(skeleton))


def fragments(source, cli, work, sheet):
    for library, extracted in LIBRARIES.items():
        xml = work / f"{library[1]}.xml"
        run(cli, "fcb", "decode", source / extracted, "-o", xml)
        lines = xml.read_text(encoding="utf-8").splitlines()
        for name, start, end in prototypes(lines):
            text = "\n".join(lines[start:end])
            if name == "gadgets.Equipped.Map":
                text = held_sheet(text.replace(*PLAYER_ARROW), sheet)
            elif VEHICLE_GPS[0] in text:
                text = text.replace(*VEHICLE_GPS)
            else:
                continue
            write(fragment_path(LAYER, library, name), text.split("\n"))
            print(f"{'/'.join(library[:2])}: {name}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=str(REPO / "tmp/gamefiles"), help="the game's archives, extracted")
    parser.add_argument("--game", default=r"C:\Games\Far Cry 2", help="the install the map's model is read from")
    parser.add_argument("--cli", default=str(REPO / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe"))
    parser.add_argument("--texconv", default=str(REPO / "tools/third-party/FCBConverter/texconv.exe"))
    args = parser.parse_args()
    source, tools = Path(args.source), (args.cli, args.texconv)
    if LAYER.exists():
        shutil.rmtree(LAYER)
    with tempfile.TemporaryDirectory() as work:
        textures(source, tools, Path(work))
        sheet = sheet_model(args.game, tools, Path(work))
        fragments(source, args.cli, Path(work), sheet)
    print(f"Wrote {LAYER}")


if __name__ == "__main__":
    main()
