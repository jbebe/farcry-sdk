"""Aiming Overhaul's shot shake on the demo range: for each gun one shot and a burst, from the hip and
down the sights, in one NVIDIA clip.

    python shake.py [--only AK47,Makarov] [--no-clip]
"""

import argparse
import sys
import time
from contextlib import nullcontext
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import GameError, Run, launch

# Full auto at 600 and 1200 rounds a minute, then single shot with a small and a large kick. The
# demo range does not hand over the PKM.
GUNS = ["weapons.Primary.AK47", "weapons.Secondary.MAC10", "weapons.Secondary.Makarov",
        "weapons.Primary.SPAS12"]
SHOT_SECONDS = 0.05
BURST_SECONDS = 1.5
SETTLE_SECONDS = 1.0


def shoot(game, run, label):
    run.note(f"{label}: one shot")
    game.fire(SHOT_SECONDS)
    time.sleep(SETTLE_SECONDS)
    run.note(f"{label}: a burst")
    game.fire(BURST_SECONDS)
    time.sleep(SETTLE_SECONDS)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", help="archetype endings, comma separated, as AK47,Makarov")
    parser.add_argument("--no-clip", action="store_true")
    options = parser.parse_args()
    only = options.only.split(",") if options.only else None
    guns = [gun for gun in GUNS if only is None or gun.rsplit(".", 1)[1] in only]

    run = Run("shake")
    with launch("demorange") as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        with run.record(game, "shake") if not options.no_clip else nullcontext():
            for gun in guns:
                try:
                    game.give(gun)
                except GameError as error:
                    run.note(f"{gun}: {error}")
                    continue
                shoot(game, run, f"{gun} from the hip")
                game.action("startironsight", 1.0)
                time.sleep(SETTLE_SECONDS)
                shoot(game, run, f"{gun} down the sights")
                game.action("stopironsight", 1.0)
                time.sleep(SETTLE_SECONDS)
    print(run.folder)


if __name__ == "__main__":
    main()
