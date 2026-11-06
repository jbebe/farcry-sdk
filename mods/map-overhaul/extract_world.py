# Reads world 1 out of an extracted worlds.fat into work/world1: the ground's heights and water, the
# road, river and path splines, and every placed entity with a model, a map rectangle or a marker role.
import argparse
import json
import struct
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
SECTORS = 80
QUADS = 64


def decode(cli, fcb, out):
    xml = out / f"{fcb.parent.parent.parent.name}_{fcb.stem}.xml"
    result = subprocess.run([cli, "fcb", "decode", str(fcb), "-o", str(xml)], capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"jackall-cli fcb decode {fcb} failed:\n{result.stdout}{result.stderr}")
    return xml


def objects(el, kind):
    return [o for o in el.iter("object") if o.get("type") == kind]


def children(el, kind):
    return [o for o in el if o.tag == "object" and o.get("type") == kind]


def value(el, name):
    return el.find(f"value[@name='{name}']")


def vector(el, name):
    v = value(el, name)
    return None if v is None else [float(c.text) for c in v]


def text(el, name):
    v = value(el, name)
    return None if v is None else v.text


def model_and_box(el):
    model = next((v.text for v in el.iter("value") if v.get("name") == "text_objModel" and v.text), None)
    boxes = [(r.get("bboxMin"), r.get("bboxMax")) for r in el.iter("resource") if r.get("bboxMin")]
    if not boxes:
        return model, None
    lo = np.array([[float(x) for x in a.split(",")] for a, _ in boxes]).min(0)
    hi = np.array([[float(x) for x in b.split(",")] for _, b in boxes]).max(0)
    return model, [round(float(x), 3) for x in (*lo, *hi)]


def terrain(levels):
    heights = np.zeros((SECTORS * QUADS + 1,) * 2, np.float32)
    water = np.zeros((3, SECTORS, SECTORS), np.float32)
    for f in levels.glob("w1_*/generated/sdat/sd*.sdat"):
        data = f.read_bytes()
        row, col = divmod(int(f.stem[2:]), SECTORS)
        cells = np.frombuffer(data, "<u2", 65 * 65 * 2, 0x250)[0::2].reshape(65, 65)
        heights[row * QUADS:row * QUADS + 65, col * QUADS:col * QUADS + 65] = cells / 128
        water[:, row, col] = struct.unpack_from("<IIf", data, 0x34)
    return heights, water


def library(xml):
    archetypes = {}
    for _, el in ET.iterparse(xml):
        if el.tag == "object" and el.get("type") == "Entity":
            model, box = model_and_box(el)
            if box:
                archetypes[text(el, "hidName")] = (model, box)
            el.clear()
    return archetypes


def entities(xml, archetypes, kind):
    out = []
    for layer in objects(ET.parse(xml).getroot(), "MissionLayer"):
        path = text(layer, "text_PathId")
        for el in children(layer, "Entity"):
            record = {"name": text(el, "hidName"), "tpl": text(el, "tplCreatureType"),
                      "cls": text(el, "text_hidEntityClass"), "layer": path, "file": kind,
                      "pos": vector(el, "hidPos"), "ang": vector(el, "hidAngles")}
            model, box = model_and_box(el)
            if box is None and record["tpl"] in archetypes:
                model, box = archetypes[record["tpl"]]
            if box:
                record.update(model=model, box=box)
            if value(el, "fWidth") is not None:
                record.update(w=float(text(el, "fWidth")), h=float(text(el, "fHeight")),
                              tex=text(el, "text_texTexture"))
            if box or "w" in record or kind == "mapsdata":
                out.append(record)
    return out


def splines(xml):
    out = {}
    root = ET.parse(xml).getroot()
    for kind in ("RoadSplines", "RiverSplines", "PathSplines"):
        out[kind] = [[{"pos": vector(p, "Position"), "tan": vector(p, "Tangent"), "widths": vector(p, "Widths")}
                      for p in children(spline, "ControlPoints")[0]]
                     for group in objects(root, kind) for spline in objects(group, "Spline")]
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=str(REPO / "tmp/gamefiles/worlds/worlds"),
                        help="an extracted worlds.fat")
    parser.add_argument("--cli", default=str(REPO / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe"))
    args = parser.parse_args()
    source = Path(args.source)
    out = HERE / "work" / "world1"
    out.mkdir(parents=True, exist_ok=True)

    heights, water = terrain(source / "levels")
    np.savez_compressed(out / "terrain.npz", heights=heights, still=water[0], river=water[1], level=water[2])
    print(f"Terrain: {heights.min():.1f} to {heights.max():.1f} m")

    generated = source / "worlds/world1/generated"
    sectors = sorted((source / "levels").glob("w1_*/generated/worldsectors/*.data.fcb"))
    with tempfile.TemporaryDirectory() as work:
        work = Path(work)
        archetypes = library(decode(args.cli, generated / "entitylibrary.fcb", work))
        mapsdata = decode(args.cli, generated / "world1.mapsdata.fcb", work)
        features = splines(mapsdata)
        features["entities"] = entities(mapsdata, archetypes, "mapsdata")

        def sector(fcb):
            xml = decode(args.cli, fcb, work)
            kind = "worldsector" if fcb.name.startswith("worldsector") else "landmark"
            found = entities(xml, archetypes, kind)
            xml.unlink()
            return found

        with ThreadPoolExecutor(8) as pool:
            for i, found in enumerate(pool.map(sector, sectors), 1):
                features["entities"] += found
                if i % 1000 == 0:
                    print(f"{i} of {len(sectors)} sector files")

    (out / "features.json").write_text(json.dumps(features, separators=(",", ":")))
    print(f"{len(features['entities'])} entities, " +
          ", ".join(f"{len(features[k])} {k}" for k in ("RoadSplines", "RiverSplines", "PathSplines")))


if __name__ == "__main__":
    main()
