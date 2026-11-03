"""The test range's grass field: pictures of it, then whether a rifleman's perception counts a crouched
player in it as in grass, at the distances checklist row 10 names and one in the open beside it.

    python grass.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from perception import CLEAR, LANE_YAW, SAVE, SETTLE, Log, forward, put, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

# Inside the field, which covers x 192 to 320 and y 320 to 384, with room ahead along the way the
# player faces at LANE_YAW; then a spot on open ground south of the hub.
FIELD = (290.0, 352.0)
OPEN = (256.0, 110.0)
PERCEPTION = re.compile(r"perception: (notices|recognises) the player at (\S+) m, ([^;]*);")
HOLD = 8.0

# name, where the player crouches, the soldier's distance ahead.
CASES = [
    ("grass-15", FIELD, 15),
    ("grass-20", FIELD, 20),
    ("grass-50", FIELD, 50),
    ("open-20", OPEN, 20),
]


def hold(game, log, at, distance, ahead):
    game.lua(CLEAR)
    put(game, *at)
    game.action("crouch", 1.0)
    spawn(game, (at[0] + ahead[0] * distance, at[1] + ahead[1] * distance), spawn_yaw(-ahead[0], -ahead[1]))
    log.new_lines()
    time.sleep(SETTLE + HOLD)
    lines = [m for m in map(PERCEPTION.search, log.new_lines()) if m]
    game.action("crouch", 1.0)
    return lines


def main():
    run = Run("grass")
    log = Log()
    with launch(SAVE) as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        ahead = forward(game)
        run.note(f"the player faces ({ahead[0]:.2f}, {ahead[1]:.2f}) at yaw {LANE_YAW}")

        put(game, *FIELD)
        time.sleep(1.0)
        run.shot(game, "standing in the field")
        game.action("crouch", 1.0)
        time.sleep(1.5)
        run.shot(game, "crouched in the field")
        game.action("crouch", 1.0)

        for name, at, distance in CASES:
            lines = hold(game, log, at, distance, ahead)
            seen = ", ".join(f"{m.group(1)} at {m.group(2)} m{' in grass' if 'in grass' in m.group(3) else ''}"
                             for m in lines)
            row = f"{name:<10} {seen or 'nothing logged'}"
            print(row)
            run.note(row)
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
