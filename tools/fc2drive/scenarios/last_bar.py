"""Difficulty Overhaul's last health bar on the test range's Hardcore: a shotgunner 20 m off shoots the
player, invincibility off, until he dies, each pellet a hit of 117. Each hit is reported with what it
took beside its level's full damage, so a hit on the last bar shows whether it was quartered.

    python last_bar.py
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from damage import BARS, PLAYER_HIT, SOLDIERS_GUN, face_shooter
from perception import CLEAR, SAVE, Log

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "mods" / "difficulty-overhaul" / "data"))
from weapons import curve_damage

SHOTGUNNER = "enemy_archetypes.Red_Faction.ShotgunMan_Caucasian"
LIMIT = 120.0


def main():
    run = Run("last_bar")
    log = Log()
    with launch(SAVE) as game:
        game.lua(CLEAR)
        game.give(SOLDIERS_GUN)
        game.option("Invincibility", 0)
        face_shooter(game, log, SHOTGUNNER)
        end = time.monotonic() + LIMIT
        died = False
        while not died and time.monotonic() < end:
            for line in log.new_lines():
                died |= "player died:" in line
                if hit := PLAYER_HIT.search(line):
                    level, _, distance, *health = hit.groups()
                    before, after, maximum = map(float, health)
                    where = "last bar" if before < maximum / BARS else "above it"
                    run.note(f"level {level} at {distance} m, {where}: {before:.0f} -> {after:.0f}, took "
                             f"{before - after:.0f} of its full {curve_damage(int(level)):.0f}")
            time.sleep(0.05)
        run.note("died" if died else f"alive after {LIMIT:.0f} s")
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
