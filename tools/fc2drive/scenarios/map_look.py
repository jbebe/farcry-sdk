"""The map held while the view looks down and then up, on the hill south-east of Pala: a picture at each
pitch, where Map Overhaul's sheet should stay level as the view moves past it.

    python map_look.py
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

HILL = (2678.0, 2365.0, 53.2, 37.0)
# How hard and how long the view is turned for each step, and what the step is called.
STEPS = [(0.0, "level"), (-0.5, "down"), (-0.5, "further down"), (1.0, "back level"), (0.5, "up"), (0.5, "further up")]
TURN = 0.6


def main():
    run = Run("map look")
    with launch("world1") as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        game.goto(*HILL)
        time.sleep(3.0)
        game.action("toggle_gadget", 1.0)
        time.sleep(4.0)
        for rate, name in STEPS:
            if rate:
                game.action("look", 0.0, rate)
                time.sleep(TURN)
                game.action("look", 0.0, 0.0)
            time.sleep(1.5)
            run.shot(game, name)
        game.action("toggle_gadget", 1.0)
        time.sleep(2.0)


if __name__ == "__main__":
    main()
