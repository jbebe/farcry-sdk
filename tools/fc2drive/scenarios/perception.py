"""Difficulty Overhaul's perception on the test range: from how far one rifleman notices and recognises
the player, case by case, read off the combat log's perception lines.

    python perception.py [--only day-upright,night-upright]

Each case spawns a fresh rifleman, places the player in the open, behind the low wall or in the grass
field, facing him, and walks him toward the soldier, holds him still at a distance, or sidesteps him to
and fro there. The walk-in cases have the soldier come to the player instead, along one of the range's
patrol routes. A case is about one place in the soldier's view, in his focus, at his side or behind
him: the plugin's trace says where he looks, a sighting counts in the zone it happened in, a look of
his there from within the model's distance that saw nothing counts against it, and a case left
unjudged is tried again. The table at the end puts what the log shows beside what the model expects.
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
DEATH = re.compile(r"soldier \S+ died, not by the player")
# The switch that has Difficulty Overhaul trace every check of the player.
TRACE_SWITCH = "-perceptiontrace"
# The bonus plan that is the camo suit.
CAMO_SUIT = "stealth_camouflage"
# Seconds a crouched player waits before the soldier spawns, for the crouch and the grass to settle.
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
# Tries at a case before it is given up as one he never kept his eyes on; the looks in its zone a
# never needs, a second of looking; and how far in metres a distance may be off the model's and match.
ATTEMPTS = 3
LOOKS = 4
TOLERANCE = 1.5
# How near an approach's looks have to come for a never to count.
APPROACHED = 10.0

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
    ("camo-still-4", 12, "camo", 4, True, "hold", 3, 3),
    ("camo-still-2.5", 12, "camo", 2.5, True, "hold", 3, 3),
]
WARM_UP = ("warm-up", 12, "toward", 30, False, "hold", 48, 40)

# The range's patrol routes: where each starts, at its patrol point, and the way it runs from there.
ROUTES = {"lane": ((140.0, 110.0), (1.0, 0.0)), "grass": ((318.0, 352.0), (-1.0, 0.0)),
          "wall": ((100.0, 125.0), (0.0, -1.0))}
# Seconds a spawned soldier may take to die of a fall the game is sure of, and tries at a spawn that
# outlives it; how far closer than his farthest look he has to come to be on his way, which his idle
# wandering does not reach; the longest wait for that, and for a whole walk-in. Then how much farther
# than his nearest look he has to draw to have passed, and how close the player walks up to him.
FALL_DEATH = 2.5
SPAWNS = 3
ON_HIS_WAY = 20.0
WAIT_LIMIT = 120.0
WALK_LIMIT = 240.0
PASSED = 5.0
WALK_UP = 6.0
# name, route, hour, how far ahead along it and to its left the player stands, crouched, what he does
# once the soldier is on his way (stays still, walks to him, or sidesteps), the zone the case is about,
# then what the model expects: notices and recognises from, None for never, and for a never how near
# a look of his in the zone has to come to count.
WALK_CASES = [
    ("walk-day-still", "lane", 12, 75, 0, False, "still", FOCUS, 48, 40, None),
    ("walk-day-crouched-still", "lane", 12, 75, 0, True, "still", FOCUS, 48, 40, None),
    ("walk-day-upright", "lane", 12, 140, 0, False, "walk", FOCUS, 100, 60, None),
    ("walk-day-crouched", "lane", 12, 140, 0, True, "walk", FOCUS, 70, 50, None),
    ("walk-night-upright", "lane", 0, 80, 0, False, "walk", FOCUS, 30, 25, None),
    ("walk-night-crouched-still", "lane", 0, 60, 0, True, "still", FOCUS, 30, 25, None),
    ("walk-side-35", "lane", 12, 75, 35, False, "still", "at his side", None, None, 37),
    ("walk-grass-crouched-still", "grass", 12, 100, 0, True, "still", FOCUS, 24, 20, None),
    ("walk-grass-crouched-moving", "grass", 12, 100, 0, True, "sidestep", FOCUS, 30, 25, None),
    ("walk-wall-crouched", "wall", 12, 88, 0, True, "still", FOCUS, None, None, 20),
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


# A noticing or recognising: how far, where in his view as logged and as the bare zone, and how many
# of his looks came before it.
Sighting = namedtuple("Sighting", "distance zone where before")


class Watch:
    """What one try at a case saw of the soldier's looks, sightings and death, and every line it read."""

    def __init__(self, zone):
        self.zone = zone
        self.looks = []
        self.seen = {}
        self.on_way = True
        self.dead = False
        self.lines = []

    def read(self, lines):
        self.lines += lines
        for line in lines:
            if trace := TRACE.search(line):
                self.looks.append((float(trace.group(1)), trace.group(2)))
            elif match := PERCEPTION.search(line):
                # Recognised at once, he noticed him there too.
                sighting = Sighting(float(match.group(2)), match.group(3), match.group(4), len(self.looks))
                self.seen.setdefault("notices", sighting)
                if match.group(1) == "recognises":
                    self.seen.setdefault("recognises", sighting)
            elif DEATH.search(line):
                self.dead = True

    def distances(self):
        return [distance for distance, _ in self.looks]

    def in_zone(self, looks):
        return [distance for distance, zone in looks if zone == self.zone]

    def before_any_sighting(self):
        """His looks before he first made anything of the player, while the case was as set up."""
        first = min((sighting.before for sighting in self.seen.values()), default=len(self.looks))
        return self.looks[:first]


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


def spawn(game, at, yaw, archetype=RIFLEMAN, above=SOLDIER_DROP):
    """A rifleman, or `archetype`, put `above` the range's ground at `at`, kept in the TestRange table CLEAR
    empties."""
    game.lua(f'TestRange.n = TestRange.n + 1 TestRange[TestRange.n] = SpawnEntityFromArchetype('
             f'"{archetype}", {at[0]:.3f}, {at[1]:.3f}, {GROUND + above:.3f}, 0, 0, {yaw:.1f})')


def on_range(x, y):
    return EDGE <= x <= RANGE_SIZE - EDGE and EDGE <= y <= RANGE_SIZE - EDGE


def run_case(game, log, case):
    """One try at a case."""
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
    while time.monotonic() < start + SETTLE:
        watch.read(log.new_lines())
        time.sleep(0.2)
    if mode in ("hold", "sidestep"):
        start = time.monotonic()
        way = None
        while time.monotonic() < start + HOLD:
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
        while time.monotonic() < deadline and "recognises" not in watch.seen:
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
    if crouched:
        game.action("crouch", 1.0)
    if where == "camo":
        game.lua(f'Game:RemoveBonusPlan("{CAMO_SUIT}")')
    return watch


def models(case):
    """Each level of a case, what the model expects of it, and how near his looks need to come."""
    distance, mode = case[3], case[5]

    def close(model):
        if mode != "approach":
            return distance + TOLERANCE
        return APPROACHED if model is None else None

    return [(level, model, close(model)) for level, model in (("notices", case[6]), ("recognises", case[7]))]


def shown(watch, level, model):
    sighting = watch.seen.get(level)
    logged = f"{sighting.distance:.1f} m {sighting.zone}" if sighting else "never"
    expected = "-" if model == TURNS else "never" if model is None else f"{model:g} m"
    return f"{logged:<36} model {expected:<6}"


def verdict(watch, level, model, close):
    """Whether a level matches the model, judged by his looks and sightings in the case's zone."""
    if model == TURNS:
        return "left to his turn"
    if watch.dead:
        return "unjudged, he died"
    if not watch.on_way and not watch.seen:
        return "unjudged, he never set off"
    sighting = watch.seen.get(level)
    if sighting is not None and sighting.where == watch.zone:
        distance = sighting.distance
        if model is None:
            return "differs"
        missed = [d for d in watch.in_zone(watch.looks[:sighting.before]) if distance + 0.3 < d <= model - TOLERANCE]
        if missed:
            return f"differs, missed him at {max(missed):.1f} m"
        return "matches" if distance <= model + TOLERANCE else "differs"
    looks = watch.in_zone(watch.before_any_sighting())
    if model is not None and looks and min(looks) <= model - TOLERANCE:
        return f"differs, missed him at {min(looks):.1f} m"
    if close is not None and sum(d <= close for d in looks) >= LOOKS:
        return "matches"
    return "unjudged, he never came close" if model is None else "unjudged, not in the zone within reach"


def spawn_alive(game, log, at, yaw):
    """A rifleman spawned at `at` who outlives a fall the game is sure of, and the lines since."""
    for _ in range(SPAWNS):
        game.lua(CLEAR)
        log.new_lines()
        game.lua(f'TestRange.n = TestRange.n + 1 TestRange[TestRange.n] = SpawnEntityFromArchetype('
                 f'"{RIFLEMAN}", {at[0]:.3f}, {at[1]:.3f}, {GROUND:.4f}, 0, 0, {yaw:.1f})')
        time.sleep(FALL_DEATH)
        lines = log.new_lines()
        if not any(DEATH.search(line) for line in lines):
            return lines
    return lines


def run_walk(game, log, case):
    """One walk-in: the soldier spawned at his route's start, the player ahead, until he recognises the
    player, passes him, or dies."""
    _, route, hour, ahead, aside, crouched, motion, zone, *_ = case
    (sx, sy), (dx, dy) = ROUTES[route]
    player = (sx + dx * ahead - dy * aside, sy + dy * ahead + dx * aside)
    game.lua(CLEAR + f"CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay({hour}, 0)")
    put(game, *player, spawn_yaw(sx - player[0], sy - player[1]))
    if crouched:
        game.action("crouch", 1.0)
        time.sleep(CROUCH)
    watch = Watch(zone)
    watch.read(spawn_alive(game, log, (sx, sy), spawn_yaw(dx, dy)))
    start = time.monotonic()
    on_way = None
    way = None
    while time.monotonic() < start + WALK_LIMIT and not watch.dead and "recognises" not in watch.seen:
        watch.read(log.new_lines())
        distances = watch.distances()
        if on_way is None:
            if distances and max(distances) - distances[-1] >= ON_HIS_WAY:
                on_way = len(distances)
                if motion == "walk":
                    game.action("move", 1.0, 0.0)
            elif time.monotonic() > start + WAIT_LIMIT:
                break
        else:
            if distances[-1] >= min(distances[on_way - 1:]) + PASSED:
                break
            if motion == "walk" and distances[-1] <= WALK_UP:
                game.action("move", 0.0, 0.0)
            if motion == "sidestep":
                # A sidestep is sent once a swing, right then left.
                turn = int(time.monotonic() / SWING) % 2
                if turn != way:
                    way = turn
                    game.action("move", 0.0, 1.0 - 2.0 * way)
        time.sleep(0.2)
    game.action("move", 0.0, 0.0)
    time.sleep(1.0)
    watch.read(log.new_lines())
    if crouched:
        game.action("crouch", 1.0)
    watch.on_way = on_way is not None
    return watch


def walk_models(case):
    """Each level of a walk-in with what the model expects of it, and how near a never needs him."""
    _, *_, notices, recognises, close = case
    return [("notices", notices, close), ("recognises", recognises, close)]


def judge(game, run, log, case, run_one, levels):
    """Up to ATTEMPTS tries at a case until every level is judged; the row of the one that judged most."""
    def judged(watch):
        return sum(not verdict(watch, level, model, close).startswith("unjudged") for level, model, close in levels)

    tries = []
    while len(tries) < ATTEMPTS:
        tries.append(run_one(game, log, case))
        if judged(tries[-1]) == len(levels):
            break
    watch = max(tries, key=judged)
    row = f"{case[0]:<26} try {tries.index(watch) + 1} of {len(tries)}"
    for level, model, close in levels:
        row += f" | {level} {shown(watch, level, model)} {verdict(watch, level, model, close)}"
    print(row)
    run.note(row)


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
            if only is None or case[0] in only:
                judge(game, run, log, case, run_case, models(case))
        for case in WALK_CASES:
            if only is None or case[0] in only:
                judge(game, run, log, case, run_walk, walk_models(case))
        game.lua(CLEAR)
    print(run.folder)


if __name__ == "__main__":
    main()
