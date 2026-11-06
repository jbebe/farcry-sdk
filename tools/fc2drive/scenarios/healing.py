"""Difficulty Overhaul's healing on the test range, which plays Hardcore: a rifleman 20 m off shoots the
player, invincibility off, until a hit lands, and is cleared; WAIT seconds later a second rifleman's
first hit shows how far health came back, beside the game's own refill of the bar the first hit left.

    python healing.py
"""

import math
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from damage import BARS, PLAYER_HIT, SHOOTER_LIMIT, SOLDIERS_GUN, face_shooter, first_match
from perception import CLEAR, SAVE, Log

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

WAIT = 30.0


def take_a_hit(game, log):
    """A rifleman's first hit on the player, or None; he is cleared after it."""
    face_shooter(game, log)
    hit = first_match(log, PLAYER_HIT, SHOOTER_LIMIT)
    game.lua(CLEAR)
    return hit


def refill(game, log):
    """What the run saw of the refill, as notes."""
    first = take_a_hit(game, log)
    if first is None:
        return ["never hit"]
    after, maximum = float(first.group(5)), float(first.group(6))
    bar = maximum / BARS
    expected = math.ceil(after / bar) * bar
    notes = [f"first hit: health {first.group(4)} -> {first.group(5)} of {first.group(6)}, one bar {bar:.0f}"]
    time.sleep(WAIT)
    second = take_a_hit(game, log)
    if second is None:
        return notes + ["second rifleman never hit him"]
    return notes + [f"second hit {WAIT:.0f} s later: health before it {second.group(4)}, "
                    f"the top of the bar {expected:.0f}"]


def main():
    run = Run("healing")
    log = Log()
    with launch(SAVE) as game:
        game.lua(CLEAR)
        game.give(SOLDIERS_GUN)
        game.option("Invincibility", 0)
        for note in refill(game, log):
            run.note(note)
    print(run.folder)


if __name__ == "__main__":
    main()
