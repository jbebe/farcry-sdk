"""Difficulty Overhaul's perception on the test range: from how far one rifleman notices and recognises
the player, case by case, read off the combat log's perception lines.

    python perception.py [--save world1] [--only day-upright,night-upright]

Each case spawns a fresh rifleman, places the player, and either walks him toward the soldier or holds
him still at a distance. The table at the end puts what the log shows beside what the model expects.
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
TRAVEL = 'GameChangeWorld("testrange", "2054037628277228842")'
# The range's ground, and a drop onto it for the player and for a soldier.
GROUND = 16.0625
PLAYER_DROP = 2.0
SOLDIER_DROP = 0.5
# Removes what the last case spawned, which the TestRange table holds, as the Range tab does.
CLEAR = ("TestRange = TestRange or {n = 0} for i = 1, TestRange.n do RemoveEntity(TestRange[i]) end "
         "TestRange = {n = 0} ")
PERCEPTION = re.compile(r"perception: (notices|recognises) the player at (\S+) m")
# Seconds: a soldier's settling after he spawns, a held player's wait, and an approach's longest.
SETTLE = 3.0
HOLD = 6.0
APPROACH_LIMIT = 40.0
# An approach stops this close to the soldier.
CLOSEST = 4.0

# The duel lane runs east from (30, 110); the low wall's barriers lie across (100, 40) with the
# player's start south of them.
LANE = (30.0, 110.0)
WALL_START = (100.0, 37.0)
WALL_SOLDIER = (100.0, 70.0)

# name, hour, soldier facing (toward the player, away, or north across the lane), start distance,
# crouched, mode, then what the model expects: notices and recognises from, None for never.
CASES = [
    ("day-upright", 12, "toward", 130, False, "approach", 100, 60),
    ("day-crouched", 12, "toward", 110, True, "approach", 70, 50),
    ("day-still-45", 12, "toward", 45, False, "hold", 48, 40),
    ("day-still-35", 12, "toward", 35, False, "hold", 48, 40),
    ("day-crouched-still-35", 12, "toward", 35, True, "hold", 48, 40),
    ("night-upright", 0, "toward", 60, False, "approach", 30, 25),
    ("night-crouched-still-20", 0, "toward", 20, True, "hold", 30, 25),
    ("side-still-15", 12, "north", 15, False, "hold", 24, 20),
    ("side-still-30", 12, "north", 30, False, "hold", 24, 20),
    ("behind-upright", 12, "away", 30, False, "approach", 20, None),
    ("behind-crouched", 12, "away", 30, True, "approach", None, None),
    ("wall-crouched", 12, "wall", 33, True, "hold", None, None),
    ("wall-standing", 12, "wall", 33, False, "hold", 48, 40),
]


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
    for line in lines:
        match = PERCEPTION.search(line)
        if match and match.group(1) not in seen:
            seen[match.group(1)] = float(match.group(2))


def place(game, case):
    name, hour, facing, distance, crouched, mode, *_ = case
    game.lua(CLEAR + f"CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay({hour}, 0)")
    if facing == "wall":
        player, soldier, yaw, player_yaw = WALL_START, WALL_SOLDIER, 180, 0
    else:
        player = LANE
        soldier = (LANE[0] + distance, LANE[1])
        yaw = {"toward": 90, "away": 270, "north": 0}[facing]
        player_yaw = 270
    game.goto(player[0], player[1], GROUND + PLAYER_DROP, player_yaw)
    game.lua(f'TestRange.n = TestRange.n + 1 TestRange[TestRange.n] = SpawnEntityFromArchetype('
             f'"{RIFLEMAN}", {soldier[0]:.3f}, {soldier[1]:.3f}, {GROUND + SOLDIER_DROP:.3f}, 0, 0, {yaw})')
    return soldier


def run_case(game, log, case):
    name, hour, facing, distance, crouched, mode, *_ = case
    soldier = place(game, case)
    if crouched:
        game.action("crouch", 1.0)
    time.sleep(SETTLE)
    log.new_lines()
    seen = {}
    if mode == "hold":
        deadline = time.monotonic() + HOLD
        while time.monotonic() < deadline:
            perceived(log.new_lines(), seen)
            time.sleep(0.2)
    else:
        game.action("move", 1.0, 0.0)
        deadline = time.monotonic() + APPROACH_LIMIT
        while time.monotonic() < deadline and "recognises" not in seen:
            perceived(log.new_lines(), seen)
            x, y, _ = game.state()["pos"]
            if math.hypot(soldier[0] - x, soldier[1] - y) <= CLOSEST:
                break
            time.sleep(0.1)
        game.action("move", 0.0, 0.0)
        time.sleep(0.5)
        perceived(log.new_lines(), seen)
    if crouched:
        game.action("crouch", 1.0)
    return seen


def shown(value):
    return "never" if value is None else f"{value:.0f} m"


def expected(case, model):
    """Where the model says the log should show it: on an approach from where it starts, held still
    at the hold's distance if it reaches that far."""
    distance, mode = case[3], case[5]
    if model is None:
        return None
    if mode == "hold":
        return distance if distance <= model else None
    return min(model, distance)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--save", default="world1")
    parser.add_argument("--only", help="case names, comma separated")
    options = parser.parse_args()
    only = options.only.split(",") if options.only else None

    run = Run("perception")
    log = Log()
    with launch(options.save) as game:
        game.option("Invincibility", 1)
        game.call("weather clear")
        game.lua(TRAVEL)
        game.wait_ready()
        for case in CASES:
            if only is not None and case[0] not in only:
                continue
            seen = run_case(game, log, case)
            # Recognised at once, he never logged noticing first.
            seen.setdefault("notices", seen.get("recognises"))
            row = (f"{case[0]:<26} notices {shown(seen.get('notices')):>7} "
                   f"(model {shown(expected(case, case[6]))}), "
                   f"recognises {shown(seen.get('recognises')):>7} "
                   f"(model {shown(expected(case, case[7]))})")
            print(row)
            run.note(row)
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
