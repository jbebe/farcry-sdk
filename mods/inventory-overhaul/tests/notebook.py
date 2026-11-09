"""The notebook's pages against the sky, each matching its approved picture, then a gun picked up at
the feet coming to hand. Runs in world1, where the notebook is, on flat ground a pickup lies still on.
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "tools" / "fc2drive"))
from fc2drive import Run

PAGES = ["inventory", "papers", "journal", "contacts"]
FLAT_GROUND = (2606.9, 2110.0, 21.5)
# Up into the sky.
SKY = (0.0, 60.0)
# Seconds for the notebook to come up and settle, and for a page to turn.
OPEN = 2.5
FLIP = 1.5
# A pickup within use's reach: this far ahead of the feet and above them, looked down at.
PICKUP = "pickups.Weapons.AK47_new"
REACH = (1.2, 0.3)
LOOK_DOWN = -50.0
GUN = "weapons.Primary.AK47"

run = Run("notebook", mods=["inventoryoverhaul"])
with run.launch("world1") as game:
    game.option("Invincibility", 1)
    game.fix_light(hour=12)
    game.goto(*FLAT_GROUND, yaw=0.0)
    time.sleep(3.0)
    x, y, z = game.state()["pos"]

    game.look(*SKY)
    game.action("toggle_notebook", 1.0)
    time.sleep(OPEN)
    for page in PAGES:
        game.pause(True)
        run.expect_picture(game, page)
        game.pause(False)
        game.action("flipside", 1.0)
        time.sleep(FLIP)
    game.action("toggle_notebook", 1.0)
    time.sleep(OPEN)

    game.look(0.0, LOOK_DOWN)
    game.spawn(PICKUP, x, y + REACH[0], z + REACH[1])
    time.sleep(1.5)
    run.expect("a gun picked up comes to hand", game.pick_up_gun() == GUN, game.state()["gun"])
run.finish()
