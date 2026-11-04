"""The test range's crest along the grass field's north edge: pictures from the Grass crest arena's
start, then whether a rifleman past its grassy part, and one past its bare part, makes out the player
standing or crouched still, read off the combat log's perception lines and traces.

    python crest.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from perception import (ATTEMPTS, CLEAR, FOCUS, GROUND, LOOKS, PLAYER_ABOVE, SAVE, SETTLE, TRACE_SWITCH, Log, spawn,
                        spawn_yaw)

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

# The arena's start on open ground north of the crest, the rifleman past its grassy part and the one
# past its bare part, each 36 m off; then the crest's top on the grassy one's line, a metre up.
START = (316.0, 392.0)
GRASSY = (296.0, 362.0)
BARE = (336.0, 362.0)
TOP = (305.3, 376.0)
CREST = 1.0
PERCEPTION = re.compile(r"perception: (notices|recognises) the player at (\S+) m")
TRACE = re.compile(r"perception trace: (\S+) m, ([^;]*);")
HOLD = 10.0

# name, where the player stands and how high its ground is above the range's, the soldier, crouched,
# then what the model expects: whether he recognises the player.
CASES = [
    ("grassy-standing", START, 0.0, GRASSY, False, False),
    ("grassy-crouched", START, 0.0, GRASSY, True, False),
    ("bare-standing", START, 0.0, BARE, False, True),
    ("bare-crouched", START, 0.0, BARE, True, True),
    ("on-the-crest", TOP, CREST, GRASSY, False, True),
]


def stand(game, at, raised, toward):
    game.goto(at[0], at[1], GROUND + raised + PLAYER_ABOVE, spawn_yaw(toward[0] - at[0], toward[1] - at[1]))
    time.sleep(1.0)


def hold(game, log, case):
    _, at, raised, soldier, crouched, _ = case
    game.lua(CLEAR)
    stand(game, at, raised, soldier)
    if crouched:
        game.action("crouch", 1.0)
        time.sleep(1.5)
    log.new_lines()
    spawn(game, soldier, spawn_yaw(at[0] - soldier[0], at[1] - soldier[1]))
    time.sleep(SETTLE + HOLD)
    lines = log.new_lines()
    if crouched:
        game.action("crouch", 1.0)
    seen = [m.group(1) + " at " + m.group(2) + " m" for m in map(PERCEPTION.search, lines) if m]
    looks = [m.group(2) for m in map(TRACE.search, lines) if m and FOCUS in m.group(2)]
    return seen, len(looks), sum("behind grass" in look for look in looks)


def judge(game, log, case):
    """Up to ATTEMPTS tries until the case is judged: a never needs LOOKS of his looks on the player."""
    for attempt in range(1, ATTEMPTS + 1):
        seen, looks, behind = hold(game, log, case)
        recognised = any(s.startswith("recognises") for s in seen)
        if recognised or looks >= LOOKS:
            break
    verdict = ("matches" if recognised == case[5] else "differs") if recognised or looks >= LOOKS else "unjudged"
    return (f"{case[0]:<16} try {attempt} | {', '.join(seen) or 'nothing':<24} "
            f"{looks} looks on him, {behind} behind grass | {verdict}")


def main():
    run = Run("crest")
    log = Log()
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        for label, soldier in (("grassy", GRASSY), ("bare", BARE)):
            game.lua(CLEAR)
            spawn(game, soldier, spawn_yaw(START[0] - soldier[0], START[1] - soldier[1]))
            stand(game, START, 0.0, soldier)
            time.sleep(2.0)
            run.shot(game, f"standing, toward the {label} crest")
            game.action("crouch", 1.0)
            time.sleep(1.5)
            run.shot(game, f"crouched, toward the {label} crest")
            game.action("crouch", 1.0)

        for case in CASES:
            row = judge(game, log, case)
            print(row)
            run.note(row)
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
