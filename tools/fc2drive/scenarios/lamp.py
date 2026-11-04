"""A lamp at night on the test range: one of tmpla's point lights spawned 3 m up at (200, 180), the player
standing still 2 m beside it and then 15 m north of it, a rifleman 15 m east each time. Reports which of
his looks find the player lit by a lamp, and the distances he recognises him from: by the lamp the night's
25 m reaches 31 m.

    python lamp.py
"""

import re
import sys
import time
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from perception import CLEAR, SAVE, TRACE_SWITCH, Log, put, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

LAMP = "IGE_Archetypes.AutoPrefab.OmniLight_62"
LAMP_AT = (200.0, 180.0)
LAMP_ABOVE = 3.0
CASES = [("beside the lamp", (202.0, 180.0)), ("away from it", (202.0, 195.0))]
WATCHER = 15.0
HOLD = 9.0
TRACE = re.compile(r"perception trace: (\S+) m, [^;]*?(, lit by a lamp)?, (alert|idle);.*recognises from (\S+) m")


def main():
    run = Run("lamp")
    log = Log()
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=1)
        # Straight from the save's start to the lamp's sector, the first goto stalled; nearby first.
        put(game, 256.0, 110.0, 180.0)
        put(game, 202.0, 230.0, 180.0)
        for name, at in CASES:
            game.lua(CLEAR)
            spawn(game, LAMP_AT, 0.0, LAMP, LAMP_ABOVE)
            soldier = (at[0] + WATCHER, at[1])
            put(game, *at, spawn_yaw(soldier[0] - at[0], soldier[1] - at[1]))
            log.new_lines()
            spawn(game, soldier, spawn_yaw(at[0] - soldier[0], at[1] - soldier[1]))
            time.sleep(HOLD)
            run.shot(game, name)
            looks = Counter()
            for m in map(TRACE.search, log.new_lines()):
                if m and abs(float(m.group(1)) - WATCHER) < 2.0:
                    looks[f"{'lit' if m.group(2) else 'not lit'}, {m.group(3)}, recognises from {m.group(4)} m"] += 1
            run.note(f"{name:<16} | " + "; ".join(f"{count} {look}" for look, count in looks.items()))
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
