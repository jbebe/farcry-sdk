# Stitches world 1's 25 area maps, one per level cell, into work/world1-map.png: 5120 px a side at
# 1 m per pixel, row a (the highest Y) at the top and column 1 (the lowest X) at the left.
import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
TILE = 1024


def run(cli, *args):
    result = subprocess.run([cli, *args], capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"jackall-cli {' '.join(args[:2])} failed:\n{result.stdout}{result.stderr}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", default=r"C:\Games\Far Cry 2")
    parser.add_argument("--cli", default=str(
        HERE.parents[1] / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe"))
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as work:
        work = Path(work)
        run(args.cli, "archive", "extract", str(Path(args.game, "Data_Win32/worlds/worlds.fat")),
            "--names", "--filter", "mapcompass_mip0", "-o", str(work))
        full = Image.new("RGB", (5 * TILE, 5 * TILE))
        for r, row in enumerate("abcde"):
            for c in range(5):
                xbt = work / "levels" / f"w1_{row}_{c + 1}" / "mapcompass_mip0.xbt"
                run(args.cli, "xbt", "extract", str(xbt))
                with Image.open(xbt.with_suffix(".dds")) as tile:
                    full.paste(tile.convert("RGB"), (c * TILE, r * TILE))

    out = HERE / "work" / "world1-map.png"
    out.parent.mkdir(exist_ok=True)
    full.save(out, optimize=True)
    print(f"Wrote {out}")


if __name__ == "__main__":
    main()
