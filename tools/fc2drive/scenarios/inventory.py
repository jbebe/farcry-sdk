"""Inventory Overhaul's notebook: filled, opened, every page turned, then a gun picked up.

    python inventory.py [--save world2]

Needs Inventory Overhaul in the install. Its notebook exists only in world1 and world2.
"""

import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import GAME_BIN, GameError, Run, launch

PAGES = ["inventory", "papers", "journal", "contacts"]
OPEN_SECONDS = 2.5
FLIP_SECONDS = 1.0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--save", default="world2")
    options = parser.parse_args()
    if not list((GAME_BIN / "plugins").rglob("inventoryoverhaul*.dll")):
        raise GameError("Inventory Overhaul is not in the install")

    run = Run("inventory")
    with launch(options.save, env={"INVENTORY_OVERHAUL_FILL": "1"}) as game:
        game.option("Invincibility", 1)

        game.action("toggle_notebook", 1.0)
        time.sleep(OPEN_SECONDS)
        for page in PAGES:
            run.shot(game, page)
            game.action("flipside", 1.0)
            time.sleep(FLIP_SECONDS)
        game.action("toggle_notebook", 1.0)
        time.sleep(OPEN_SECONDS)

        game.spawn_pickup("pickups.Weapons.AK47_new")
        time.sleep(1.5)
        run.shot(game, "a gun on the ground")
        run.note(f"picked up: {game.pick_up_gun()}")
        time.sleep(1.5)
        run.shot(game, "after the pickup")
    print(run.folder)


if __name__ == "__main__":
    main()
