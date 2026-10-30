"""Visits every spot in a list and takes a picture, at one hour and in clear weather, so runs compare.

    python locations.py [spots/world1.json] [--hour 12] [--clip]

A spots file names the save to start from and each spot's x, y, z and yaw, which `fc2drive.py state`
reads off wherever the player stands.
"""

import argparse
import json
import sys
import time
from contextlib import nullcontext
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

HOLD_SECONDS = 3.0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("spots", nargs="?", default=str(Path(__file__).parent / "spots" / "world1.json"))
    parser.add_argument("--hour", type=int, default=12)
    parser.add_argument("--clip", action="store_true", help="also record the tour with NVIDIA")
    options = parser.parse_args()
    spots = json.loads(Path(options.spots).read_text(encoding="utf-8"))

    run = Run(Path(options.spots).stem)
    with launch(spots["save"]) as game:
        game.option("Invincibility", 1)
        game.fix_light(options.hour)
        with run.record(game, "tour") if options.clip else nullcontext():
            for spot in spots["spots"]:
                game.goto(spot["x"], spot["y"], spot["z"], spot.get("yaw"))
                game.wait_ready()
                time.sleep(HOLD_SECONDS)
                run.shot(game, spot["name"])
    print(run.folder)


if __name__ == "__main__":
    main()
