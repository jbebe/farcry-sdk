"""Difficulty Overhaul's regeneration on the test range: a rifleman 20 m off shoots the player,
invincibility off, until a hit lands, and is cleared; the climb back to full health starts, and WAIT
seconds later a second rifleman's first hit shows how far health came back, beside what one bar to full
in mods\\difficulty-overhaul\\src\\regeneration.cpp's 300 s gives.

    python regeneration.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from damage import PLAYER_HIT, SHOOTER, SHOOTER_LIMIT, SOLDIERS_GUN, first_match
from perception import CLEAR, LANE_MIDDLE, SAVE, Log, put, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

WAIT = 30.0
START_LIMIT = 15.0
ONE_BAR_TO_FULL = 300.0
REGENERATING = re.compile(r"regenerating: health (\S+) of (\S+), one bar (\S+), full in (\S+) s")


def take_a_hit(game, log):
    """A rifleman's first hit on the player, or None; he is cleared after it."""
    west = spawn_yaw(-1.0, 0.0)
    put(game, *LANE_MIDDLE, west)
    log.new_lines()
    spawn(game, (LANE_MIDDLE[0] + SHOOTER, LANE_MIDDLE[1]), west)
    # A shot away from him alerts him: an idle soldier does not open fire.
    game.fire(0.05)
    hit = first_match(log, PLAYER_HIT, SHOOTER_LIMIT)
    game.lua(CLEAR)
    return hit


def climb(game, log):
    """What the run saw of the climb, as notes."""
    first = take_a_hit(game, log)
    if first is None:
        return ["never hit"]
    hit_at = time.monotonic()
    start = first_match(log, REGENERATING, START_LIMIT)
    if start is None:
        return [f"hit to {first.group(5)}, but no climb started within {START_LIMIT:.0f} s"]
    started = time.monotonic()
    health, maximum, bar, full = map(float, start.groups())
    notes = [f"first hit: health {first.group(4)} -> {first.group(5)}; climb started "
             f"{started - hit_at:.1f} s later at {health:.0f} of {maximum:.0f}, one bar {bar:.0f}, "
             f"full in {full:.0f} s"]
    time.sleep(WAIT)
    second = take_a_hit(game, log)
    if second is None:
        return notes + ["second rifleman never hit him"]
    climbed = time.monotonic() - started
    expected = min(maximum, health + (maximum - bar) / ONE_BAR_TO_FULL * climbed)
    return notes + [f"second hit {climbed:.1f} s into the climb: health before it {second.group(4)}, "
                    f"expected {expected:.0f}"]


def main():
    run = Run("regeneration")
    log = Log()
    with launch(SAVE) as game:
        game.lua(CLEAR)
        game.give(SOLDIERS_GUN)
        game.option("Invincibility", 0)
        for note in climb(game, log):
            run.note(note)
    print(run.folder)


if __name__ == "__main__":
    main()
