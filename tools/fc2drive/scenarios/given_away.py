"""Difficulty Overhaul's shots giving the player away, on the test range: a rifleman faces the player,
who crouches still in the open too far off for him to make out, and fires one round away from him.
Within the gun's hearing the soldier should recognise him, "given away by his shot", and open fire;
beyond it he should not. Reports what he did, and the chance of his first shots, which an alert
soldier's quicker settle should lift off 0.

    python given_away.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from damage import first_match
from perception import CLEAR, LANE_MIDDLE, SAVE, WARM_UP, Log, put, run_case, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

# The gun, how far the soldier stands, and whether its shot carries that far: the AK-47 is heard
# within 75 m, the Dragunov within 150 m.
CASES = (("weapons.Primary.AK47", 60.0, True), ("weapons.Primary.AK47", 100.0, False),
         ("weapons.Primary.Dragunov", 100.0, True))
SETTLE = 2.0
WATCH = 12.0
FIRST_SHOTS = 3
GIVEN_AWAY = re.compile(r"perception: recognises the player at (\S+) m, .*given away by his shot")
SHOT = re.compile(r"shot at player: chance (\S+) .*? at (\S+) m, (\S+) s on him")


def run_shot(run, game, log, archetype, distance, heard):
    game.lua(CLEAR)
    game.give(archetype)
    put(game, *LANE_MIDDLE, spawn_yaw(0.0, 1.0))
    game.action("crouch", 1.0)
    spawn(game, (LANE_MIDDLE[0] + distance, LANE_MIDDLE[1]), spawn_yaw(-1.0, 0.0))
    time.sleep(SETTLE)
    log.new_lines()
    # The round goes north, away from him.
    game.fire(0.05)
    got = "not given away"
    if revealed := first_match(log, GIVEN_AWAY, WATCH):
        shots = []
        while len(shots) < FIRST_SHOTS and (shot := first_match(log, SHOT, WATCH)):
            shots.append(f"{shot.group(1)} at {shot.group(2)} m")
        got = f"given away at {revealed.group(1)} m; first shots: {', '.join(shots) or 'none'}"
    game.action("crouch", 1.0)
    want = "given away" if heard else "not given away"
    run.note(f"{archetype:<26} at {distance:.0f} m | expected {want} | {got}")


def main():
    run = Run("given_away")
    log = Log()
    with launch(SAVE) as game:
        game.option("Invincibility", 1)
        # The first soldier spawned after the load sees nothing.
        run_case(game, log, WARM_UP)
        for case in CASES:
            run_shot(run, game, log, *case)
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
