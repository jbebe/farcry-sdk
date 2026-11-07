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

# Seconds for the gun to go and the map to come up, or for the sheet to turn over.
GADGET_SECONDS = 4.0
# Each spot's name, x, y, a z 2 m above the ground, and the yaw the player faces.
SPOTS = [("hill south-east of Pala", 2678.0, 2365.0, 53.2, 37.0),
         ("Pala, by the bus stop", 2566.0, 2420.0, 20.0, 0.0)]
# The Datsun on the flat of the airstrip, facing west, so its driver's door is on the south side, where the
# player stands facing it; the ground there is at 20.4 m.
CAR = (2620.0, 1200.0, 20.4)
CAR_DOOR = 1.8
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

        game.lua(f'SpawnEntityFromArchetype("{CAR_ARCHETYPE}", {CAR[0]:.3f}, {CAR[1]:.3f}, {CAR[2] + 0.5:.3f}, 0, 0, 90)')
        game.goto(CAR[0], CAR[1] - CAR_DOOR, CAR[2] + 2.0, 0.0)
        time.sleep(3.0)
        run.shot(game, "Datsun: beside it")
        gadget(game, "use")
        time.sleep(2.0)
        run.shot(game, "Datsun: the dashboard")
        run.note(f"after use, the game's state: {game.state()}")
        read_map(game, run, "Datsun")


if __name__ == "__main__":
    main()
