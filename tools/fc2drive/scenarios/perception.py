"""Difficulty Overhaul's perception on the test range: from how far one rifleman notices and recognises
the player, case by case, read off the combat log's perception lines.

    python perception.py [--only day-upright,night-upright]

Each case spawns a fresh rifleman, places the player in the open, behind the low wall or in the grass
field, facing him, and walks him toward the soldier, holds him still at a distance, or sidesteps him to
and fro there. A case is about one place in the soldier's view, in his focus, at his side or behind
him, and counts only while the soldier keeps the player there: the plugin's trace says where he looks,
and a case he looks away from is tried again. The table at the end puts what the log shows beside what
the model expects.
"""

import argparse
import math
import re
import sys
import time
from collections import namedtuple
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
# Where in the soldier's view each place's cases are about.
ZONES = {"side": "at his side", "away": "behind him"}
FOCUS = "in his focus"
ZONE = "(" + "|".join([FOCUS, *ZONES.values()]) + ")"
PERCEPTION = re.compile(r"perception: (notices|recognises) the player at (\S+) m, [^,]+, "
                        rf"({ZONE}(?:, in grass|, hidden by the camo suit)?)")
TRACE = re.compile(rf"perception trace: (\S+) m, [^,]+, {ZONE}")
# The switch that has Difficulty Overhaul trace every check of the player.
TRACE_SWITCH = "-perceptiontrace"
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
# Tries at a case before it is given up as one he never kept his eyes on; the trace samples a never
# needs, a second of looking; and how far in metres a distance may be off the model's and match.
ATTEMPTS = 3
LOOKS = 4
TOLERANCE = 1.5
# Seconds from his last look to the end of a try within which he still counts as looking.
LAST_LOOK = 1.0

# The lane is open ground through (256, 110), south of the hub, run along LANE_YAW from LANE_BACK
# behind its middle, so the farthest soldier stays on the range. The low wall's barriers lie across
# (100, 40), with the player's start south of them.
LANE_MIDDLE = (256.0, 110.0)
LANE_YAW = 90.0
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
# from, None for never, TURNS for what he does once he has heard the player and turned to him.
TURNS = "turns"
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
    ("behind-upright", 12, "away", 30, False, "approach", 20, TURNS),
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


# A noticing or recognising: how far, where in his view, and whether he had kept the player in the
# case's zone until then.
Sighting = namedtuple("Sighting", "distance zone kept")


class Watch:
    """What one try at a case saw, and whether and how near he looked at the player in its zone."""

    def __init__(self, zone):
        self.zone = zone
        self.looks = 0
        self.looked = 0.0
        self.away = False
        self.closest = math.inf
        self.seen = {}
        self.ended = 0.0

    def read(self, lines):
        for line in lines:
            if trace := TRACE.search(line):
                if trace.group(2) == self.zone:
                    self.looks += 1
                    self.looked = time.monotonic()
                    self.closest = min(self.closest, float(trace.group(1)))
                else:
                    self.away = True
            elif match := PERCEPTION.search(line):
                # Recognised at once, he noticed him there too.
                sighting = Sighting(float(match.group(2)), match.group(3), not self.away and match.group(4) == self.zone)
                self.seen.setdefault("notices", sighting)
                if match.group(1) == "recognises":
                    self.seen.setdefault("recognises", sighting)

    def judged(self, level):
        if level in self.seen:
            return self.seen[level].kept
        # A soldier who dies stops looking, so a never needs him looking to the end.
        return not self.away and self.looks >= LOOKS and self.ended - self.looked <= LAST_LOOK


def direction(yaw):
    """The way a yaw faces, the remote control's and a spawn's alike: 0 faces +y, 90 faces -x."""
    yaw = math.radians(yaw)
    return -math.sin(yaw), math.cos(yaw)


def spawn_yaw(gx, gy):
    """The yaw that faces (gx, gy)."""
    return math.degrees(math.atan2(-gx, gy))


def put(game, x, y, yaw=None):
    game.goto(x, y, GROUND + PLAYER_ABOVE, yaw)
    time.sleep(1.0)


def place(where, distance):
    """Where a case puts the player and the soldier, and the soldier's yaw."""
    if where == "wall":
        return WALL_START, WALL_SOLDIER, 180.0
    fx, fy = direction(LANE_YAW)
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
    """One try at a case, given up as soon as he looks away from where it is about."""
    _, hour, where, distance, crouched, mode, *_ = case
    game.lua(CLEAR + f"CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay({hour}, 0) " +
             (f'Game:AddBonusPlan("{CAMO_SUIT}")' if where == "camo" else ""))
    player, soldier, yaw = place(where, distance)
    put(game, *player, spawn_yaw(soldier[0] - player[0], soldier[1] - player[1]))
    if crouched:
        game.action("crouch", 1.0)
        time.sleep(CROUCH)
    log.new_lines()
    watch = Watch(ZONES.get(where, FOCUS))
    spawn(game, soldier, yaw)
    start = time.monotonic()
    while time.monotonic() < start + SETTLE and not watch.away:
        watch.read(log.new_lines())
        time.sleep(0.2)
    if mode in ("hold", "sidestep"):
        start = time.monotonic()
        way = None
        while time.monotonic() < start + HOLD and not watch.away:
            # A sidestep is sent once a swing, right then left.
            turn = int((time.monotonic() - start) / SWING) % 2
            if mode == "sidestep" and turn != way:
                way = turn
                game.action("move", 0.0, 1.0 - 2.0 * way)
            watch.read(log.new_lines())
            time.sleep(0.2)
    else:
        game.action("move", 1.0, 0.0)
        deadline = time.monotonic() + APPROACH_LIMIT
        nearest = math.inf
        while time.monotonic() < deadline and "recognises" not in watch.seen and not watch.away:
            watch.read(log.new_lines())
            x, y, _ = game.state()["pos"]
            gap = math.hypot(soldier[0] - x, soldier[1] - y)
            nearest = min(nearest, gap)
            if gap <= CLOSEST or gap > nearest + WRONG_WAY or not on_range(x, y):
                break
            time.sleep(0.1)
    game.action("move", 0.0, 0.0)
    time.sleep(0.5)
    watch.read(log.new_lines())
    watch.ended = time.monotonic()
    if crouched:
        game.action("crouch", 1.0)
    if where == "camo":
        game.lua(f'Game:RemoveBonusPlan("{CAMO_SUIT}")')
    return watch


def models(case):
    """Each level of a case, with what the model expects of it."""
    return (("notices", case[6]), ("recognises", case[7]))


def verdict(watch, level, model, distance):
    """Whether what he did matches the model, with a never only if he never looked from within it."""
    if model == TURNS:
        return "left to his turn"
    if not watch.judged(level):
        return "unjudged, he looked away"
    if level in watch.seen:
        matches = model is not None and abs(watch.seen[level].distance - min(model, distance)) <= TOLERANCE
    else:
        matches = model is None or watch.closest > model - TOLERANCE
    return "matches" if matches else "differs"


def shown(watch, level, model):
    sighting = watch.seen.get(level)
    logged = f"{sighting.distance:.1f} m {sighting.zone}" if sighting else "never"
    expected = "-" if model == TURNS else "never" if model is None else f"{model:g} m"
    return f"{logged:<36} model {expected:<6}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", help="case names, comma separated")
    options = parser.parse_args()
    only = options.only.split(",") if options.only else None

    run = Run("perception")
    log = Log()
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        game.call("weather clear")
        # The first soldier spawned after the load saw nothing; a held case first wakes the AI.
        run_case(game, log, WARM_UP)
        for case in CASES:
            if only is not None and case[0] not in only:
                continue
            levels = [level for level, model in models(case) if model != TURNS]
            tries = []
            while len(tries) < ATTEMPTS:
                tries.append(run_case(game, log, case))
                if all(tries[-1].judged(level) for level in levels):
                    break
            watch = max(tries, key=lambda tried: sum(tried.judged(level) for level in levels))
            row = f"{case[0]:<24} try {tries.index(watch) + 1} of {len(tries)}"
            for level, model in models(case):
                row += f" | {level} {shown(watch, level, model)} {verdict(watch, level, model, case[3])}"
            print(row)
            run.note(row)
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
