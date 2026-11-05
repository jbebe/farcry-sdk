"""Difficulty Overhaul's weapon damage on the test range: the player shoots a rifleman who stands 10 m
off with his back turned, a single shot at a time, with each of a few guns, standing (the shot lands
high) and crouched (it lands on the body); then a rifleman shoots the player, invincibility off, until
two hits have landed. Reports each hit as the combat log has it, its level, body part and health, beside
the level and health mods\\difficulty-overhaul\\data\\weapons.py gives the gun.

    python damage.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from perception import CLEAR, LANE_MIDDLE, SAVE, Log, put, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "mods" / "difficulty-overhaul" / "data"))
from weapons import curve_damage, gun, impact_level

TARGET = 10.0
SHOOTER = 20.0
TRIES = 4
TRY_SECONDS = 1.0
SETTLE = 2.0
PLAYER_HITS = 2
SHOOTER_LIMIT = 30.0
GUNS = ("weapons.Secondary.Makarov", "weapons.Secondary.Uzi", "weapons.Primary.AK47", "weapons.Primary.FNFAL",
        "weapons.Primary.SPAS12", "weapons.Primary.AS50")
SOLDIERS_GUN = "weapons.Primary.AK47"
SOLDIER_HIT = re.compile(r"soldier \w+ hit: \w+ level (\d+), ([\w ]+), at (\S+) m, (.*?) \(")
PLAYER_HIT = re.compile(r"player hit: \w+ level (\d+), ([\w ]+), at (\S+) m, health (\S+) -> (\S+) of ")


def expected(archetype):
    """The gun's level and the health it takes, a shotgun's a pellet."""
    level = impact_level(*gun(archetype.partition(".")[2]))
    return f"level {level}, {curve_damage(level):.0f}"


def first_match(log, pattern, seconds):
    """The first line `pattern` finds in the log within `seconds`, or None."""
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        if hit := next((m for m in map(pattern.search, log.new_lines()) if m), None):
            return hit
        time.sleep(0.05)
    return None


def shoot_at(game, log, crouched):
    """Single shots at a rifleman until one hits him; the hit's line, or None."""
    game.lua(CLEAR)
    put(game, *LANE_MIDDLE, spawn_yaw(1.0, 0.0))
    if crouched:
        game.action("crouch", 1.0)
    spawn(game, (LANE_MIDDLE[0] + TARGET, LANE_MIDDLE[1]), spawn_yaw(1.0, 0.0))
    time.sleep(SETTLE)
    log.new_lines()
    hit = None
    for _ in range(TRIES):
        game.fire(0.05)
        if hit := first_match(log, SOLDIER_HIT, TRY_SECONDS):
            break
    if crouched:
        game.action("crouch", 1.0)
    return hit


def main():
    run = Run("damage")
    log = Log()
    with launch(SAVE) as game:
        game.option("Invincibility", 1)
        for archetype in GUNS:
            game.give(archetype)
            for crouched in (False, True):
                hit = shoot_at(game, log, crouched)
                got = "no hit" if hit is None else \
                    f"level {hit.group(1)}, {hit.group(2)} at {hit.group(3)} m, {hit.group(4)}"
                run.note(f"{archetype:<28} {'crouched' if crouched else 'standing':<9} | expected "
                         f"{expected(archetype)} a torso hit | {got}")
        game.lua(CLEAR)
        put(game, *LANE_MIDDLE, spawn_yaw(1.0, 0.0))
        game.option("Invincibility", 0)
        log.new_lines()
        spawn(game, (LANE_MIDDLE[0] + SHOOTER, LANE_MIDDLE[1]), spawn_yaw(-1.0, 0.0))
        hits = []
        end = time.monotonic() + SHOOTER_LIMIT
        while len(hits) < PLAYER_HITS and time.monotonic() < end:
            hits += [m for m in map(PLAYER_HIT.search, log.new_lines()) if m]
            time.sleep(0.05)
        game.lua(CLEAR)
        game.option("Invincibility", 1)
        for hit in hits[:PLAYER_HITS]:
            taken = float(hit.group(4)) - float(hit.group(5))
            run.note(f"rifleman on the player | expected {expected(SOLDIERS_GUN)} anywhere | level "
                     f"{hit.group(1)}, {hit.group(2)} at {hit.group(3)} m, took {taken:.0f}")
        if not hits:
            run.note("rifleman on the player | never hit him")
    print(run.folder)


if __name__ == "__main__":
    main()
