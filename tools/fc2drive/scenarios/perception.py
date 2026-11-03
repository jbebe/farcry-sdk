"""Difficulty Overhaul's perception on the test range: from how far one rifleman notices and recognises
the player, case by case, read off the combat log's perception lines.

    python perception.py [--only day-upright,night-upright]

Each case spawns a fresh rifleman, places the player in the open, behind the low wall or in the grass
field, and walks him toward the soldier, holds him still at a distance, or sidesteps him to and fro
there. The table at the end puts what the log shows beside what the model expects.
"""

import argparse
import math
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import GAME_BIN, Run, launch

LOGS = GAME_BIN / "difficulty-overhaul-logs"
RIFLEMAN = "enemy_archetypes.Red_Faction.Assault_Caucasian"
# The save behind RunDebug_demorange, which starts on the test range: changing world to it from
# another save crashed in the frame after.
SAVE = "demorange"
# The range's ground, the height above it the player is put at, as the Range tab puts him, and a drop
# onto it for a soldier; then how far the range reaches, and how near its edge a walk stops.
GROUND = 16.0625
PLAYER_ABOVE = 0.44
SOLDIER_DROP = 0.5
RANGE_SIZE = 512.0
EDGE = 10.0
# Removes what the last case spawned, which the TestRange table holds, as the Range tab does.
CLEAR = ("TestRange = TestRange or {n = 0} for i = 1, TestRange.n do RemoveEntity(TestRange[i]) end "
         "TestRange = {n = 0} ")
PERCEPTION = re.compile(r"perception: (notices|recognises) the player at (\S+) m, [^,]+, "
                        r"((?:in his focus|at his side|behind him)(?:, in grass|, hidden by the camo suit)?)")
# The bonus plan that is the camo suit.
CAMO_SUIT = "stealth_camouflage"
# Seconds a crouched player waits before the soldier spawns, past the second still in grass he needs to count as hidden.
CROUCH = 3.0
# Seconds: a soldier's settling after he spawns, a held player's wait, a sidestep's each way, and an
# approach's longest.
SETTLE = 3.0
HOLD = 6.0
SWING = 1.5
APPROACH_LIMIT = 40.0
# An approach stops this close to the soldier, or once it has drawn this much farther from him than
# it has been.
CLOSEST = 4.0
WRONG_WAY = 3.0

# The lane is open ground through (256, 110), south of the hub. The player is put this far back along
# the way he faces from it, so a soldier as far ahead as any case puts him stays on the range. The
# low wall's barriers lie across (100, 40), with the player's start south of them.
LANE_MIDDLE = (256.0, 110.0)
LANE_BACK = 65.0
SIDE_ANGLE = 60.0
WALL_START = (100.0, 37.0)
WALL_SOLDIER = (100.0, 70.0)
# The grass field covers x 192 to 320 and y 320 to 384; a case there puts the player and the soldier
# either side of its middle.
FIELD_MIDDLE = (256.0, 352.0)

# name, hour, where (on the lane with the soldier facing toward the player, away, or with him at his
# side; behind the wall; in the grass field facing him, there in the camo suit), start distance,
# crouched, mode (approach, hold or sidestep), then what the model expects: notices and recognises
# from, None for never.
CASES = [
    ("day-upright", 12, "toward", 130, False, "approach", 100, 60),
    ("day-crouched", 12, "toward", 110, True, "approach", 70, 50),
    ("day-still-45", 12, "toward", 45, False, "hold", 48, 40),
    ("day-still-35", 12, "toward", 35, False, "hold", 48, 40),
    ("day-crouched-still-35", 12, "toward", 35, True, "hold", 48, 40),
    ("night-upright", 0, "toward", 60, False, "approach", 30, 25),
    ("night-crouched-still-20", 0, "toward", 20, True, "hold", 30, 25),
    ("side-still-15", 12, "side", 15, False, "hold", 24, 20),
    ("side-still-30", 12, "side", 30, False, "hold", 24, 20),
    ("behind-upright", 12, "away", 30, False, "approach", 20, None),
    ("behind-crouched", 12, "away", 30, True, "approach", None, None),
    ("wall-crouched", 12, "wall", 33, True, "hold", None, None),
    ("wall-standing", 12, "wall", 33, False, "hold", 48, 40),
    ("grass-crouched-still-15", 12, "grass", 15, True, "hold", 24, 20),
    ("grass-crouched-still-22", 12, "grass", 22, True, "hold", 24, 20),
    ("grass-crouched-still-30", 12, "grass", 30, True, "hold", 24, 20),
    ("grass-crouched-moving-22", 12, "grass", 22, True, "sidestep", 30, 25),
    ("grass-crouched-moving-27", 12, "grass", 27, True, "sidestep", 30, 25),
    ("grass-upright-still-30", 12, "grass", 30, False, "hold", 48, 40),
    ("camo-still-10", 12, "camo", 10, True, "hold", 3, 3),
    ("camo-still-2.5", 12, "camo", 2.5, True, "hold", 3, 3),
]
WARM_UP = ("warm-up", 12, "toward", 30, False, "hold", 48, 40)


class Log:
    """The newest session file in the combat logs, read from where the last read stopped."""

    def __init__(self):
        self.path = None
        self.offset = 0

    def new_lines(self):
        sessions = sorted(LOGS.glob("combat-*.log"))
        if not sessions:
            return []
        if sessions[-1] != self.path:
            self.path, self.offset = sessions[-1], 0
        with open(self.path, "rb") as file:
            file.seek(self.offset)
            data = file.read()
        self.offset += len(data)
        return data.decode("utf-8", "replace").splitlines()


def perceived(lines, seen):
    """The first noticing and recognising in `lines`; recognised at once, he noticed him there too."""
    for line in lines:
        match = PERCEPTION.search(line)
        if match:
            sighting = (float(match.group(2)), match.group(3))
            seen.setdefault("notices", sighting)
            if match.group(1) == "recognises":
                seen.setdefault("recognises", sighting)


def put(game, x, y):
    # A goto leaves the player's facing as it was, whatever yaw it is given.
    game.goto(x, y, GROUND + PLAYER_ABOVE)
    time.sleep(1.0)


def facing(game):
    """The way the player faces, from his yaw, which turns as a spawn's does."""
    yaw = math.radians(game.state()["yaw"])
    return -math.sin(yaw), math.cos(yaw)


def spawn_yaw(gx, gy):
    """SpawnEntityFromArchetype's yaw for a soldier facing (gx, gy): 0 faces +y, 90 faces -x."""
    return math.degrees(math.atan2(-gx, gy))


def place(where, distance, ahead):
    """Where a case puts the player and the soldier, and the soldier's yaw."""
    if where == "wall":
        return WALL_START, WALL_SOLDIER, 180.0
    fx, fy = ahead
    if where in ("grass", "camo"):
        player = (FIELD_MIDDLE[0] - fx * distance / 2, FIELD_MIDDLE[1] - fy * distance / 2)
    else:
        player = (LANE_MIDDLE[0] - fx * LANE_BACK, LANE_MIDDLE[1] - fy * LANE_BACK)
    soldier = (player[0] + fx * distance, player[1] + fy * distance)
    # At his side: he faces 60 degrees off the player, inside his view whichever way he glances.
    side = math.radians(SIDE_ANGLE)
    gx, gy = {"away": (fx, fy),
              "side": (-fx * math.cos(side) + fy * math.sin(side),
                       -fx * math.sin(side) - fy * math.cos(side))}.get(where, (-fx, -fy))
    return player, soldier, spawn_yaw(gx, gy)


def spawn(game, at, yaw):
    """A rifleman dropped onto the range at `at`, kept in the TestRange table CLEAR empties."""
    game.lua(f'TestRange.n = TestRange.n + 1 TestRange[TestRange.n] = SpawnEntityFromArchetype('
             f'"{RIFLEMAN}", {at[0]:.3f}, {at[1]:.3f}, {GROUND + SOLDIER_DROP:.3f}, 0, 0, {yaw:.1f})')


def on_range(x, y):
    return EDGE <= x <= RANGE_SIZE - EDGE and EDGE <= y <= RANGE_SIZE - EDGE


def run_case(game, log, case):
    _, hour, where, distance, crouched, mode, *_ = case
    game.lua(CLEAR + f"CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay({hour}, 0) " +
             (f'Game:AddBonusPlan("{CAMO_SUIT}")' if where == "camo" else ""))
    player, soldier, yaw = place(where, distance, facing(game))
    put(game, *player)
    if crouched:
        game.action("crouch", 1.0)
        time.sleep(CROUCH)
    log.new_lines()
    seen = {}
    spawn(game, soldier, yaw)
    time.sleep(SETTLE)
    perceived(log.new_lines(), seen)
    if mode in ("hold", "sidestep"):
        start = time.monotonic()
        way = None
        while time.monotonic() < start + HOLD:
            # A sidestep is sent once a swing, right then left.
            turn = int((time.monotonic() - start) / SWING) % 2
            if mode == "sidestep" and turn != way:
                way = turn
                game.action("move", 0.0, 1.0 - 2.0 * way)
            perceived(log.new_lines(), seen)
            time.sleep(0.2)
        game.action("move", 0.0, 0.0)
    else:
        game.action("move", 1.0, 0.0)
        deadline = time.monotonic() + APPROACH_LIMIT
        nearest = math.inf
        while time.monotonic() < deadline and "recognises" not in seen:
            perceived(log.new_lines(), seen)
            x, y, _ = game.state()["pos"]
            gap = math.hypot(soldier[0] - x, soldier[1] - y)
            nearest = min(nearest, gap)
            if gap <= CLOSEST or gap > nearest + WRONG_WAY or not on_range(x, y):
                break
            time.sleep(0.1)
        game.action("move", 0.0, 0.0)
        time.sleep(0.5)
        perceived(log.new_lines(), seen)
    if crouched:
        game.action("crouch", 1.0)
    if where == "camo":
        game.lua(f'Game:RemoveBonusPlan("{CAMO_SUIT}")')
    return seen


def shown(value):
    return "never" if value is None else f"{value:g} m"


def logged(seen, level):
    """What the log showed, and where in his view the player was then: idle soldiers turn their
    heads, so a case's facing is only where he starts."""
    if level not in seen:
        return "never".ljust(40)
    distance, zone = seen[level]
    return f"{distance:.1f} m {zone}".ljust(40)


def expected(case, model):
    """Where the model says the log should show it: on an approach from where it starts, held or
    sidestepping at the case's distance if it reaches that far."""
    distance, mode = case[3], case[5]
    if model is None:
        return None
    if mode != "approach":
        return distance if distance <= model else None
    return min(model, distance)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", help="case names, comma separated")
    options = parser.parse_args()
    only = options.only.split(",") if options.only else None

    run = Run("perception")
    log = Log()
    with launch(SAVE) as game:
        game.option("Invincibility", 1)
        game.call("weather clear")
        ahead = facing(game)
        run.note(f"the player faces ({ahead[0]:.2f}, {ahead[1]:.2f})")
        # The first soldier spawned after the load saw nothing; a held case first wakes the AI.
        run_case(game, log, WARM_UP)
        for case in CASES:
            if only is not None and case[0] not in only:
                continue
            seen = run_case(game, log, case)
            row = (f"{case[0]:<24} notices {logged(seen, 'notices')} (model {shown(expected(case, case[6]))}), "
                   f"recognises {logged(seen, 'recognises')} (model {shown(expected(case, case[7]))})")
            print(row)
            run.note(row)
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
