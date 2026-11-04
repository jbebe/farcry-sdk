"""The test range's crest along the grass field's north edge: pictures from the Grass crest arena, then
whether a rifleman past its grassy part, one past its bare part, and one past the bush on its top makes
out the player standing or crouched still, read off the combat log's perception lines and traces.

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
# The rifleman 30 m south of the spot straight behind the bush on the crest's top at (340, 376), and a
# spot 6 m east of that one, whose line passes 2.8 m clear of the bush.
BUSHY = (340.0, 362.0)
BEHIND_BUSH = (340.0, 392.0)
BESIDE_BUSH = (346.0, 392.0)
PERCEPTION = re.compile(r"perception: (notices|recognises) the player at (\S+) m")
TRACE = re.compile(r"perception trace: (\S+) m, ([^;]*); cover (\S+);")
HOLD = 10.0

# name, where the player stands and how high its ground is above the range's, the soldier, crouched,
# then what the model expects: whether he recognises the player.
CASES = [
    ("grassy-standing", START, 0.0, GRASSY, False, False),
    ("grassy-crouched", START, 0.0, GRASSY, True, False),
    ("bare-standing", START, 0.0, BARE, False, True),
    ("bare-crouched", START, 0.0, BARE, True, True),
    ("on-the-crest", TOP, CREST, GRASSY, False, True),
    ("bush-standing", BEHIND_BUSH, 0.0, BUSHY, False, False),
    ("bush-crouched", BEHIND_BUSH, 0.0, BUSHY, True, False),
    ("beside-the-bush", BESIDE_BUSH, 0.0, BUSHY, False, True),
]

# Where the pictures are taken from, and toward which soldier.
VIEWS = [("grassy crest", START, GRASSY), ("bare crest", START, BARE), ("bush", BEHIND_BUSH, BUSHY)]


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
    looks = [m for m in map(TRACE.search, lines) if m and FOCUS in m.group(2)]
    covers = sorted(float(m.group(3)) for m in looks)
    return seen, len(looks), sum("behind grass" in m.group(2) for m in looks), covers


def judge(game, log, case):
    """Up to ATTEMPTS tries until the case is judged: a never needs LOOKS of his looks on the player."""
    for attempt in range(1, ATTEMPTS + 1):
        seen, looks, behind, covers = hold(game, log, case)
        recognised = any(s.startswith("recognises") for s in seen)
        if recognised or looks >= LOOKS:
            break
    verdict = ("matches" if recognised == case[5] else "differs") if recognised or looks >= LOOKS else "unjudged"
    cover = f"cover {covers[0]:.2f} to {covers[-1]:.2f}" if covers else "no cover read"
    return (f"{case[0]:<16} try {attempt} | {', '.join(seen) or 'nothing':<24} "
            f"{looks} looks on him, {behind} behind grass, {cover} | {verdict}")


def main():
    run = Run("crest")
    log = Log()
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=12)
        for label, at, soldier in VIEWS:
            game.lua(CLEAR)
            spawn(game, soldier, spawn_yaw(at[0] - soldier[0], at[1] - soldier[1]))
            stand(game, at, 0.0, soldier)
            time.sleep(2.0)
            run.shot(game, f"standing, toward the {label}")
            game.action("crouch", 1.0)
            time.sleep(1.5)
            run.shot(game, f"crouched, toward the {label}")
            game.action("crouch", 1.0)

        for case in CASES:
            row = judge(game, log, case)
            print(row)
            run.note(row)
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
