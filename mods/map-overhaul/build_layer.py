# Builds layer/ from the sheet and the game's own files: the sheet in place of world 1's map textures,
# its named places drawn again as town plans, the sheet as the model held in the hand, and the archetype
# fragments that put that model on the map, take the GPS out of the vehicles and the player's arrow off the
# map, and leave the map only the mission targets' icons.
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
# The map shows only the mission targets, the markers its visibility tables call Objective: the sheet's
# print and notes show the rest. The town plans' icons of what lies about and the diamonds' go too.
OTHER_MARKERS = ("PGP", "SafeHouse", "CellTower", "WeaponShop", "BusStation", "Underground", "HQ", "MikesPlace",
                 "Other", "City")
OTHER_ICONS = ("archMedicKit", "archAmmoPile", "archExplosivePile", "archFuelPile", "archMountedWeapon",
               "archSniperPoint", "archLandVehicle", "archWaterVehicle", "archAirVehicle", "archDiamondMarker")
# Lettering and lines on a town plan, against the sheet's; its ground is drawn 1024 px across.
PLAN_SYMBOLS = 3
TILE = 1024
# The sheet in the hand: its files, the size its texture is drawn at, and the map's paper material it
# copies. The game puts the area map on the material named MAPTOP, so a copy under another name keeps its
# own texture. The copy loses MAPTOP's Map flag, which lights a map with half the sky's light whichever way
# it faces and never lets its shade fall below a fifth of the sun, so the sheet takes the world's light as
# the hands do; PAPER tones the paper down instead, which without the flag came out overbright.
SHEET = Path(r"graphics\objects\mapcompass\sheet")
SHEET_FILES = ("sheet.xbg", "sheet_ref.skeleton", "sheet.xbm")
SHEET_TEXTURE = 4096
MAPTOP = r"GRAPHICS\_MATERIALS\JFCARRIER2-M-1708200746783136.xbm"
PAPER = 0.5
# The map's banks on foot, which hold.py remakes for the sheet in both hands, in place.
BANK_FOLDER = Path(r"graphics\characters\_common\animations\equipment\equipped_gadget")
BANKS = ("aimcyclecrh_+000fw_nowep_eqmpcp_i1", "aimoffset_+090rt_nowep_i1_eqmpcp", "aimoffset_-090lt_nowep_i1_eqmpcp",
         "aimoffsetcrh_+090rt_nowep_i1_eqmpcp", "aimoffsetcrh_-090lt_nowep_i1_eqmpcp", "draw_+000fw_nowep_eqmpcp_i1",
         "holster_+000fw_nowep_eqmpcp_i1", "idle_+000fw_nowep_eqmpcp_i1", "jumpend_+000fw_nowep_i1_eqmpcp",
         "jumpidle_+000fw_nowep_i1_eqmpcp", "jumpstart_+000fw_nowep_i1_eqmpcp", "run_+000fw_nowep_eqmpcp_i1",
         "slideend_+000fw_nowep_i1_eqmpcp", "slidepose_+000fw_nowep_i1_eqmpcp", "slidestart_+000fw_nowep_i1_eqmpcp",
         "walk_+000fw_nowep_i1_eqmpcp", "walkcrh_+000fw_nowep_i1_eqmpcp")
# The map's model, its materials, the banks and the body the export carries, by their paths in the worlds archive.
VANILLA_FOLDERS = (Path("graphics/objects/mapcompass"), BANK_FOLDER, Path("graphics/actors/principal_yabeck"))
VANILLA_FILES = (Path("graphics/characters/_common/pelvis_ref.skeleton"),
                 *(Path("graphics/_materials") / f"JFCARRIER2-M-{n}.xbm"
                   for n in ("1708200746783136", "0307200865578952", "0307200863141764")))


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


def vanilla_tree(source, work):
    """What the map's export needs from the extracted worlds archive, copied apart: the map, the banks, the
    protagonist's body and the human rig. Not the install, whose patch holds the banks this layer last
    deployed, already remade; and not the whole archive, whose animals' rigs share the human rig's name, which
    a loose export takes in its place."""
    root, tree = source / "worlds/worlds", work / "vanilla"
    for folder in VANILLA_FOLDERS:
        shutil.copytree(root / folder, tree / folder, dirs_exist_ok=True)
    for name in VANILLA_FILES:
        (tree / name).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy(root / name, tree / name)
    return tree


def sheet_model(source, game, tools, blender, work):
    # sheet.py's panels, drawn with a copy of the map's paper material that shows the whole sheet, and the
    # map's banks remade by hold.py for both hands.
    cli, texconv = tools
    clips = [a for bank in BANKS for a in ("--clip", (BANK_FOLDER / f"1stge_uppb_{bank}.mab").as_posix())]
    model = vanilla_tree(source, work) / "graphics/objects/mapcompass/map.xbg"
    run(cli, "fc2model", "export", model, *clips, "-o", work / "map.fc2model")
    run(sys.executable, HERE / "sheet.py", work / "map.fc2model", work / "sheet.fc2model", work / "sheet.json")
    run(cli, "fc2model", "add-material", work / "sheet.fc2model", SHEET / "sheet.xbm", "--from", MAPTOP, "--name", "SHEET")
    # Blender exits 0 when the script fails, so only the pack it writes tells.
    held = subprocess.run([blender, "-b", "--python", HERE / "hold.py", "--", work / "sheet.fc2model",
                           work / "held.fc2model"], capture_output=True, text=True)
    print("\n".join(l for l in held.stdout.splitlines() if "frames" in l or "Error" in l or "Traceback" in l))
    if not (work / "held.fc2model").exists():
        sys.exit(f"hold.py failed:\n{held.stdout[-3000:]}{held.stderr[-3000:]}")
    run(cli, "fc2model", "extract", work / "held.fc2model", "-o", work / "sheet", "--all")
    # The pack preview_hold.py draws.
    shutil.copy(work / "held.fc2model", HERE / "work" / "held.fc2model")
    out = LAYER / SHEET
    out.mkdir(parents=True, exist_ok=True)
    for name in SHEET_FILES:
        shutil.copy(work / "sheet" / "mods" / SHEET / name, out / name)
    (LAYER / BANK_FOLDER).mkdir(parents=True, exist_ok=True)
    for bank in BANKS:
        name = f"1stge_uppb_{bank}.mab"
        shutil.copy(work / "sheet" / "mods" / BANK_FOLDER / name, LAYER / BANK_FOLDER / name)
    run(cli, "xbm", "set", out / "sheet.xbm", f"DiffuseTexture1={SHEET / 'sheet.xbt'}", f"DiffuseColor1={PAPER}",
        "--remove", "Map", "-g", game)

    # The paper to its trimmed edge, which lies symmetric about the world's middle.
    png = work / "sheet.png"
    low, high = render_map.EDGE[0], render_map.WORLD - render_map.EDGE[0]
    paper = Image.open(HERE / "assets/world1-sheet.png").convert("RGB").crop((low, low, high, high))
    paper.resize((SHEET_TEXTURE,) * 2, Image.LANCZOS).save(png)
    shutil.copy(png, HERE / "work" / "sheet.png")
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


def targets_only(text):
    start = text.index('<object type="MarkerVisibility">')
    end = text.index('<object type="MarkerRepository">', start)
    kinds = "|".join(OTHER_MARKERS)
    tables = re.sub(rf'(<value name="(?:{kinds})" type="Bool">)True(</value>)', r"\1False\2", text[start:end])
    icons = "|".join(OTHER_ICONS)
    return re.sub(rf'(<value name="(?:{icons})" type="String">)[^<]*(</value>)', r"\1\2",
                  text[:start] + tables + text[end:])


def fragments(source, cli, work, sheet):
    for library, extracted in LIBRARIES.items():
        xml = work / f"{library[1]}.xml"
        run(cli, "fcb", "decode", source / extracted, "-o", xml)
        lines = xml.read_text(encoding="utf-8").splitlines()
        for name, start, end in prototypes(lines):
            text = "\n".join(lines[start:end])
            if name == "gadgets.Equipped.Map":
                text = targets_only(held_sheet(text.replace(*PLAYER_ARROW), sheet))
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
    parser.add_argument("--blender", default=r"C:\Programs\Blender 5.2\blender.exe")
    args = parser.parse_args()
    source, tools = Path(args.source), (args.cli, args.texconv)
    if LAYER.exists():
        shutil.rmtree(LAYER)
    with tempfile.TemporaryDirectory() as work:
        textures(source, tools, Path(work))
        sheet = sheet_model(source, args.game, tools, args.blender, Path(work))
        fragments(source, args.cli, Path(work), sheet)
    print(f"Wrote {LAYER}")


if __name__ == "__main__":
    main()
