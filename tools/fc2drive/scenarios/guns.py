"""Every gun of the Armory in turn: one shot, then a longer burst, in one NVIDIA clip.

    python guns.py [--save guntest] [--only AK-47,M79] [--no-clip]
"""

import argparse
import sys
import time
from contextlib import nullcontext
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import GameError, Run, launch

SHOT_SECONDS = 0.1
BURST_SECONDS = 3.0
GAP_SECONDS = 1.5


def rounds_left(game):
    state = game.state()
    return state["magazine"] + state["spare"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--save", default="guntest")
    parser.add_argument("--only", help="labels, comma separated")
    parser.add_argument("--no-clip", action="store_true")
    options = parser.parse_args()
    only = options.only.split(",") if options.only else None

    run = Run("guns")
    with launch(options.save) as game:
        game.option("Invincibility", 1)
        guns = [gun for gun in game.guns() if only is None or gun["label"] in only]
        start = time.monotonic()
        with run.record(game, "guns") if not options.no_clip else nullcontext():
            for gun in guns:
                try:
                    game.give(gun["archetype"])
                except GameError as error:
                    run.note(f"{gun['label']}: {error}")
                    continue
                at = time.monotonic() - start
                full = rounds_left(game)
                game.fire(SHOT_SECONDS)
                time.sleep(GAP_SECONDS)
                after_shot = rounds_left(game)
                game.fire(BURST_SECONDS)
                time.sleep(GAP_SECONDS)
                run.note(f"{at:6.1f} s  {gun['label']}: {full - after_shot} on the short press, "
                         f"{after_shot - rounds_left(game)} on the long one")
    print(run.folder)


if __name__ == "__main__":
    main()
