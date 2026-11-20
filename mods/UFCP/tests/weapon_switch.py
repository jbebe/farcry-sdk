"""The secondary drawn right after a shot from the M1903, while its bolt is worked, and the M1903 firing
again once drawn back. Runs in world1, whose save carries the Makarov alone.
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "tools" / "fc2drive"))
from fc2drive import Run, eventually

RIFLE = "M1903"
PISTOL = "weapons.Secondary.Makarov"
# Seconds after the shot the switch is pressed: past the shot's own animation, which drops a press
# for about 0.35 s, and inside the bolt's cycle, which the game as shipped holds a switch for until
# about 1.8 s. Then how long the pistol has to come up.
PRESS_AFTER = 0.6
DRAWN_WITHIN = 2.0
# Seconds for the rifle to come back up, work its bolt and settle.
BACK = 3.0

run = Run("weapon switch", mods=["UFCP"])
with run.launch("world1") as game:
    game.option("Invincibility", 1)
    rifle = next(gun["archetype"] for gun in game.guns() if gun["label"] == RIFLE)
    game.give(rifle)
    game.call("rounds 20")
    time.sleep(1.0)

    game.fire(0.1)
    time.sleep(PRESS_AFTER)
    game.action("select_secondary_weapon", 1.0)
    game.action("select_secondary_weapon", 0.0)
    drawn = eventually(lambda: game.state()["gun"] == PISTOL, DRAWN_WITHIN)
    run.expect("the pistol comes up mid-cycle", bool(drawn), game.state()["gun"])

    game.action("select_primary_weapon", 1.0)
    game.action("select_primary_weapon", 0.0)
    eventually(lambda: game.state()["gun"] == rifle, DRAWN_WITHIN)
    time.sleep(BACK)
    before = game.state()["magazine"]
    game.fire(0.1)
    time.sleep(1.0)
    run.expect("the rifle fires once drawn back", game.state()["magazine"] == before - 1,
               f"magazine {before} -> {game.state()['magazine']}")
run.finish()
