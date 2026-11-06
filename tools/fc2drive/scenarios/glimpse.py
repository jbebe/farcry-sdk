"""Difficulty Overhaul's fire at a glimpse on the test range: a rifleman 15 m off sees the player and
starts fighting him; then the player moves to 55 m and crouches still, past the 50 m a fighting
soldier recognises him from but within the 60 m he notices him from. A soldier fighting the player
should keep firing at what he notices, at a low chance, where before he held his fire until he
recognised him again. Reports his fire discipline and shots in that window.

    python glimpse.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from damage import SOLDIERS_GUN, first_match
from perception import CLEAR, LANE_MIDDLE, SAVE, TRACE_SWITCH, WARM_UP, Log, put, run_case, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

NEAR = 15.0
FAR = 55.0
START_LIMIT = 15.0
WATCH = 8.0
FIRST_SHOTS = 4
SHOT = re.compile(r"shot at player: chance (\S+) .*? at (\S+) m, .*?, (hit|miss); visibility ([\d.]+)")
LOOK = re.compile(r"perception trace: (\S+) m, .*?; visibility (\S+), lines (\S+) and (\S+);")


def glimpse(run, game, log):
    game.lua(CLEAR)
    game.give(SOLDIERS_GUN)
    east = spawn_yaw(1.0, 0.0)
    put(game, *LANE_MIDDLE, east)
    spawn(game, (LANE_MIDDLE[0] + NEAR, LANE_MIDDLE[1]), spawn_yaw(-1.0, 0.0))
    if first_match(log, SHOT, START_LIMIT) is None:
        run.note(f"he never opened fire at {NEAR:.0f} m")
        return
    put(game, LANE_MIDDLE[0] + NEAR - FAR, LANE_MIDDLE[1], east)
    game.action("crouch", 1.0)
    log.new_lines()
    time.sleep(WATCH)
    lines = log.new_lines()
    shots = [m for m in map(SHOT.search, lines) if m]
    holds = sum("fire discipline: holds his fire" in line for line in lines)
    if look := next((m for m in map(LOOK.search, reversed(lines)) if m), None):
        run.note(f"his lines: notices above {look.group(3)}, recognises above {look.group(4)}; "
                 f"last look at {look.group(1)} m, visibility {look.group(2)}")
    run.note(f"in {WATCH:.0f} s at {FAR:.0f} m crouched still: {len(shots)} shots, "
             f"{sum(s.group(3) == 'hit' for s in shots)} hits, {holds} holds")
    for shot in shots[:FIRST_SHOTS]:
        run.note(f"shot at {shot.group(2)} m, visibility {shot.group(4)}, chance {shot.group(1)}, "
                 f"{shot.group(3)}")


def main():
    run = Run("glimpse")
    log = Log()
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        # The first soldier spawned after the load sees nothing.
        run_case(game, log, WARM_UP)
        glimpse(run, game, log)
    print(run.folder)


if __name__ == "__main__":
    main()
