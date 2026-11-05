"""Aiming Overhaul's look of being hit, on the test range: a rifleman 20 m off shoots the player,
invincibility off, and is taken away at his first hit, as the combat log reports it, for a burst of
shots; then another, until two hits have landed. The look itself is in the shots.

    python hit.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from perception import CLEAR, LANE_MIDDLE, SAVE, Log, put, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

SHOOTER = 20.0
HITS = 2
LIMIT = 40.0
# Seconds after the hit is read in the log that each shot is asked for.
SHOTS = (0.0, 0.25, 0.6, 1.2, 2.5, 4.5)
PLAYER_HIT = re.compile(r"player hit: \w+ level (\d+), ([\w ]+), at (\S+) m, health (\S+) -> (\S+) of ")


def shots_after(game, run, label):
    start = time.monotonic()
    for after in SHOTS:
        time.sleep(max(0.0, start + after - time.monotonic()))
        run.shot(game, f"{label} +{time.monotonic() - start:.2f}s")


def main():
    run = Run("hit")
    log = Log()
    with launch(SAVE) as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        game.lua(CLEAR)
        put(game, *LANE_MIDDLE, spawn_yaw(1.0, 0.0))
        time.sleep(1.0)
        run.shot(game, "before")
        game.option("Invincibility", 0)
        for hits in range(1, HITS + 1):
            log.new_lines()
            spawn(game, (LANE_MIDDLE[0] + SHOOTER, LANE_MIDDLE[1]), spawn_yaw(-1.0, 0.0))
            hit = None
            end = time.monotonic() + LIMIT
            while hit is None and time.monotonic() < end:
                hit = next((m for m in map(PLAYER_HIT.search, log.new_lines()) if m), None)
                time.sleep(0.02)
            game.lua(CLEAR)
            if hit is None:
                run.note(f"hit {hits}: the rifleman never hit the player")
                break
            run.note(f"hit {hits}: level {hit.group(1)}, {hit.group(2)} at {hit.group(3)} m, "
                     f"health {hit.group(4)} -> {hit.group(5)}")
            shots_after(game, run, f"hit {hits} {hit.group(2)}")
        game.option("Invincibility", 1)
    print(run.folder)


if __name__ == "__main__":
    main()
