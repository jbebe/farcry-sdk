"""The map read close on the hill south-east of Pala: the view looks down at Map Overhaul's sheet, held
below it, left mouse held zooms in, the view looks right, left and down over the still sheet, and let go,
as the map's input set sends it, it zooms out with the sheet back in front; then the map is put away while
still read close, and the view looked down with the gun that comes
up, which should follow the look as it does without the map.

    python map_read.py
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

HILL = (2678.0, 2365.0, 53.2, 37.0)
GADGET_SECONDS = 4.0
# How hard the view turns, right and up positive, for each step, and what the step is called.
LOOKS = [(0.5, 0.0, "looked right over it"), (-1.0, 0.0, "looked left over it"), (0.5, -0.4, "looked down over it")]
TURN = 0.6
# The look down at the sheet, which is held below the level view: about 40 degrees.
LOOK_DOWN = -0.55


def look(game, yaw, pitch):
    game.action("look", yaw, pitch)
    time.sleep(TURN)
    game.action("look", 0.0, 0.0)
    time.sleep(1.0)


def main():
    run = Run("map read")
    with launch("world1") as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        game.goto(*HILL)
        time.sleep(3.0)
        game.action("toggle_gadget", 1.0)
        time.sleep(GADGET_SECONDS)
        run.shot(game, "held")
        look(game, 0.0, LOOK_DOWN)
        run.shot(game, "held, looked down at it")
        game.action("startshooting", 1.0)
        time.sleep(1.0)
        run.shot(game, "read close")
        for yaw, pitch, name in LOOKS:
            look(game, yaw, pitch)
            run.shot(game, name)
        # As the map's own input set answers left mouse let go: toggleicons, not stopshooting.
        game.action("toggleicons", 1.0)
        time.sleep(1.0)
        run.shot(game, "let go")
        look(game, 0.0, 0.4)
        run.shot(game, "held, looking at it again")

        game.action("startshooting", 1.0)
        time.sleep(1.0)
        look(game, 0.5, 0.0)
        game.action("toggle_gadget", 1.0)
        time.sleep(GADGET_SECONDS)
        game.action("stopshooting", 0.0)
        time.sleep(1.0)
        run.shot(game, "put away while read close")
        look(game, 0.0, -0.5)
        run.shot(game, "put away, looking down")
        game.action("toggle_gadget", 1.0)
        time.sleep(GADGET_SECONDS)
        run.shot(game, "out again")


if __name__ == "__main__":
    main()
