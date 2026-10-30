"""Inventory Overhaul's notebook: filled, opened, every page turned, then used as a player uses it:
a gun stowed, a gun picked up at the four-gun limit, a stowed gun put in a slot through its menu, a
gun and an item dropped, a note read and a tape played. What the plugin logged goes in the run's notes.

    python inventory.py

Needs Inventory Overhaul in the install. Its notebook exists only in world1 and world2, and a spawned
pickup is only offered on flat ground, so this runs by the round table in world1.
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import GAME_BIN, GameError, Run, launch

PAGES = ["inventory", "papers", "journal", "contacts"]
OPEN_SECONDS = 2.5
FLIP_SECONDS = 1.0
STEP_SECONDS = 0.5
LOG_TAG = "[inventoryoverhaul]"
FLAT_GROUND = (2606.9, 2110.0, 21.5)
# More rows than a page has, to reach its first row from anywhere.
ALL_ROWS = 30
# The rows above Items on the Inventory page: guns A and B, and the machete.
ROWS_ABOVE_ITEMS = 3


def toggle(game):
    game.action("toggle_notebook", 1.0)
    time.sleep(OPEN_SECONDS)


def click(game):
    game.action("startshooting", 1.0)
    time.sleep(STEP_SECONDS)
    game.action("stopshooting", 0.0)
    time.sleep(STEP_SECONDS)


def wheel(game, rows):
    """Moves the selection `rows` down the page, or up when negative."""
    for _ in range(abs(rows)):
        game.action("cycleweapon", -1.0 if rows > 0 else 1.0)
        time.sleep(STEP_SECONDS / 2)


def drop(game):
    game.action("drop_item", 1.0)
    time.sleep(STEP_SECONDS)


def flip(game):
    game.action("flipside", 1.0)
    time.sleep(FLIP_SECONDS)


def pick_up(game, run, label):
    """Takes an AK-47 lying at the feet."""
    game.spawn_pickup("pickups.Weapons.AK47_new")
    time.sleep(1.5)
    run.shot(game, f"{label}: on the ground")
    run.note(f"{label}: {game.pick_up_gun()} came to hand")
    time.sleep(1.5)
    run.shot(game, f"{label}: taken")


def main():
    if not list((GAME_BIN / "plugins").rglob("inventoryoverhaul*.dll")):
        raise GameError("Inventory Overhaul is not in the install")

    run = Run("inventory")
    with launch("world1", env={"INVENTORY_OVERHAUL_FILL": "1"}) as game:
        game.option("Invincibility", 1)
        game.goto(*FLAT_GROUND)
        time.sleep(3.0)

        toggle(game)
        for page in PAGES:
            run.shot(game, page)
            flip(game)

        # Two guns carried and two stowed: stowing moves one and keeps the total at four.
        wheel(game, -ALL_ROWS)
        click(game)
        run.shot(game, "gun A stowed")
        toggle(game)

        # One gun carried and three stowed: a slot is free, but a fifth gun takes the gun in hand's place.
        pick_up(game, run, "at the limit")

        toggle(game)
        wheel(game, -ALL_ROWS)
        wheel(game, ROWS_ABOVE_ITEMS)
        click(game)
        run.shot(game, "a stowed gun's menu")
        click(game)
        run.shot(game, "the stowed gun in slot A")

        drop(game)
        run.shot(game, "a stowed gun dropped")
        wheel(game, 2)
        run.shot(game, "an item selected")
        drop(game)
        run.shot(game, "an item dropped")

        flip(game)
        wheel(game, -ALL_ROWS)
        click(game)
        run.shot(game, "a note read")
        click(game)
        # The Fill's two papers come before the tapes.
        wheel(game, 2)
        click(game)
        run.shot(game, "a tape playing")
        time.sleep(3.0)
        click(game)
        run.shot(game, "the tape stopped")
        toggle(game)

    log = (GAME_BIN / "fcse.log").read_text(encoding="utf-8", errors="replace").splitlines()
    for line in log:
        if LOG_TAG in line and "Hook installed" not in line:
            run.note(line.split(LOG_TAG, 1)[1].strip())
    print(run.folder)


if __name__ == "__main__":
    main()
