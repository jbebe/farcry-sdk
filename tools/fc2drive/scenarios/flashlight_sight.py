"""Difficulty Overhaul's soldiers and the player's flashlight, at night on the test range, Flashlight installed:
the player stands still on the open ground and a rifleman is spawned ahead of him, first 40 m off with the
light off, then with it on, then 20 m off with his back turned and the beam on him, then with the beam
turned away. Reports each case's looks: where the player was in his view, whether he saw the flashlight,
the distance he recognises from, and the visibility.

    python flashlight_sight.py
"""

import re
import sys
import time
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from perception import BLIND, CLEAR, HOLD, SAVE, TRACE_SWITCH, ZONE, Log, put, spawn

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

PLAYER = (440.0, 180.0)
HOUR = 1
# Each case: its name, whether the light is on, the rifleman's distance ahead, his yaw, and the
# player's yaw, the beam pointing where he looks.
NORTH, SOUTH, EAST = 0.0, 180.0, -90.0
CASES = [("off, 40 m, facing him", False, 40.0, SOUTH, NORTH),
         ("on, 40 m, facing him", True, 40.0, SOUTH, NORTH),
         ("on, 20 m, back turned, beam on him", True, 20.0, NORTH, NORTH),
         ("on, 20 m, back turned, beam away", True, 20.0, NORTH, EAST)]
TRACE = re.compile(rf"soldier (\w{{8}}) perception trace: (\S+) m, [^,]+, {ZONE}(.*?), "
                   r"(idle|alert|fighting); cover \S+; visibility (\S+),.*recognises from (\S+) m")


def main():
    run = Run("flashlight-sight")
    log = Log()
    lit = False
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=HOUR)
        for name, on, ahead, yaw, facing in CASES:
            game.lua(CLEAR)
            spawn(game, BLIND, 180.0)
            if on != lit:
                game.action("toggle_flashlight", 1)
                lit = on
            put(game, *PLAYER, facing)
            time.sleep(1.0)
            log.new_lines()
            spawn(game, (PLAYER[0], PLAYER[1] + ahead), yaw)
            time.sleep(HOLD)
            run.shot(game, name)
            looks = Counter()
            for m in map(TRACE.search, log.new_lines()):
                if m and abs(float(m.group(2)) - ahead) < 3.0:
                    seen = "sees the flashlight" if "sees his flashlight" in m.group(4) else "no flashlight"
                    looks[f"{m.group(3)}, {seen}, {m.group(5)}, recognises from {float(m.group(7)):.0f} m, "
                          f"visibility {m.group(6)}"] += 1
            run.note(f"{name:<36} | " + ("; ".join(f"{count} x {look}" for look, count in looks.most_common(3))
                                          or "no look traced"))
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
