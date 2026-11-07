"""Map Overhaul's sheet on the map in the hand: taken out on a hill south-east of Pala and turned to its
back, in Pala's street by the bus stop, where the town's plan shows, and from a Datsun's seat, whose
dashboard should have no GPS.

    python map.py

Needs Map Overhaul's layer in the install.
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

GADGET_SECONDS = 2.5
# Each spot's name, x, y, a z 2 m above the ground, and the yaw the player faces.
SPOTS = [("hill south-east of Pala", 2678.0, 2365.0, 53.2, 37.0),
         ("Pala, by the bus stop", 2566.0, 2420.0, 20.0, 0.0)]
# Where the Datsun stands, across the player's way north; he stands 3 m south of it.
CAR = (2700.0, 2300.0, 24.3)
CAR_ARCHETYPE = "vehicle.Land.Datsun"


def gadget(game, signal):
    game.action(signal, 1.0)
    time.sleep(GADGET_SECONDS)


def read_map(game, run, label):
    gadget(game, "toggle_gadget")
    run.shot(game, f"{label}: the map")
    gadget(game, "flipside")
    run.shot(game, f"{label}: its back")
    gadget(game, "flipside")
    gadget(game, "toggle_gadget")


def main():
    run = Run("map")
    with launch("world1") as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        for name, x, y, z, yaw in SPOTS:
            game.goto(x, y, z, yaw)
            time.sleep(3.0)
            run.shot(game, f"{name}: the view")
            read_map(game, run, name)

        game.goto(CAR[0], CAR[1] - 3.0, CAR[2] + 1.5, 0.0)
        game.lua(f'SpawnEntityFromArchetype("{CAR_ARCHETYPE}", {CAR[0]:.3f}, {CAR[1]:.3f}, {CAR[2] + 0.5:.3f}, 0, 0, 90)')
        time.sleep(2.0)
        gadget(game, "use")
        time.sleep(2.0)
        run.shot(game, "Datsun: the dashboard")
        run.note(f"after use, the game's state: {game.state()}")
        read_map(game, run, "Datsun")


if __name__ == "__main__":
    main()
