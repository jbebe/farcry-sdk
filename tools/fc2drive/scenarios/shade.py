"""Shade on the test range: the player crouched still at eight spots around the bush on the crest's top
and around a Datsun, and on open ground, with a rifleman 25 m out watching him. Each spot reports how
many of the soldier's looks found the player in shade and the distances he recognises him from, read
off the perception trace. Shade is of the player's head, so only a spot whose line to the sun crosses
the bush or the car is shaded: at 9 the bush's west one, at 17 its east one, and none by the car,
whose ends stand lower than a crouched head.

    python shade.py [--hour 17]
"""

import argparse
import math
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from crest import stand
from perception import CLEAR, GROUND, PLAYER_ABOVE, SAVE, SETTLE, TRACE_SWITCH, Log, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

# The bush on the crest's top and how high its ground stands; where the Datsun is spawned; open ground.
BUSH = (340.0, 376.0)
CREST = 1.0
CAR = (250.0, 244.0)
CAR_ARCHETYPE = "vehicle.Land.Datsun"
OPEN = (256.0, 110.0)
# How far from each object's middle the spots are, and the soldier beyond them.
RINGS = [("bush", BUSH, CREST, 1.8), ("Datsun", CAR, 0.0, 2.2)]
SPOTS = 8
WATCHER = 25.0
HOLD = 5.0
TRACE = re.compile(r"perception trace: [^;]*?(, in shade)?, (?:alert|idle);.*recognises from (\S+) m")


def look(game, log, at, raised, outward):
    """The player crouched still at `at`, a rifleman WATCHER farther along `outward` facing him."""
    game.lua(CLEAR)
    soldier = (at[0] + outward[0] * WATCHER, at[1] + outward[1] * WATCHER)
    stand(game, at, raised, soldier)
    game.action("crouch", 1.0)
    time.sleep(1.5)
    log.new_lines()
    spawn(game, soldier, spawn_yaw(at[0] - soldier[0], at[1] - soldier[1]))
    time.sleep(SETTLE + HOLD)
    looks = [m for m in map(TRACE.search, log.new_lines()) if m]
    game.action("crouch", 1.0)
    shaded = sum(1 for m in looks if m.group(1))
    distances = sorted({m.group(2) for m in looks})
    return f"{shaded}/{len(looks)} looks in shade, recognises from {', '.join(distances) or '-'} m"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--hour", type=int, default=17)
    options = parser.parse_args()

    run = Run("shade")
    log = Log()
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        game.fix_light(hour=options.hour)
        game.lua(f'SpawnEntityFromArchetype("{CAR_ARCHETYPE}", {CAR[0]:.3f}, {CAR[1]:.3f}, {GROUND + 0.5:.3f}, '
                 f'0, 0, 90)')
        time.sleep(2.0)
        run.note(f"open ground  | {look(game, log, OPEN, 0.0, (0.0, 1.0))}")
        for name, middle, raised, radius in RINGS:
            for spot in range(SPOTS):
                angle = 2.0 * math.pi * spot / SPOTS
                outward = (math.cos(angle), math.sin(angle))
                at = (middle[0] + outward[0] * radius, middle[1] + outward[1] * radius)
                run.note(f"{name:<6} {round(math.degrees(angle)):>3} deg | {look(game, log, at, raised, outward)}")
            game.goto(middle[0] + 8.0, middle[1], GROUND + raised + PLAYER_ABOVE + 1.0, 90.0)
            time.sleep(1.0)
            run.shot(game, f"the {name} from the east")
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
