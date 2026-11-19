"""One perftest session played by script in the session's game copy: the save from 16 October, a turn
to the right, 40 m on foot, another turn to the right, a wait there, then a quit: about 60 s in play.
With --check it writes no session log and takes a picture after each step instead, for seeing that
the route is the one meant.

    python -I session.py [--check]
"""

import argparse
import math
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "tools" / "fc2drive"))
from fc2drive import GameError, Run, eventually, launch

SAVE = "39532284313.sav"
WALK_METRES = 40.0
WALK_TIMEOUT = 30.0
TURN_SECONDS = 1.5
# Seconds held still before the route, for the world around the save to stream in, and at its end,
# which fills a session to about 60 s in play.
SETTLE = 3.0
HOLD = 40.0


def turn_right(game):
    """Turns the view 90 degrees right over TURN_SECONDS; yaw falls to the right."""
    state = game.state()
    start, pitch = state["yaw"], state["pitch"]
    began = time.monotonic()
    while (share := min(1.0, (time.monotonic() - began) / TURN_SECONDS)) < 1.0:
        game.look(start - 90.0 * share, pitch)
        time.sleep(0.02)
    game.look(start - 90.0, pitch)


def walk_forward(game):
    """Walks ahead until WALK_METRES from where it started, measured on the ground. Returns the
    time.monotonic() the walk began."""
    x, y, _ = game.state()["pos"]
    game.action("move", 1.0, 0.0)
    began = time.monotonic()
    try:
        walked = eventually(lambda: math.dist((x, y), game.state()["pos"][:2]) >= WALK_METRES, WALK_TIMEOUT)
    finally:
        game.action("move", 0.0, 0.0)
    if not walked:
        raise GameError(f"the player did not cover {WALK_METRES:.0f} m in {WALK_TIMEOUT:.0f} s")
    return began


def route(game, run=None):
    """Plays the route from the loaded save, with a picture after each step when given a Run. Returns
    the time.monotonic() the walk began, the moment a session log is lined up by."""

    def picture(label):
        if run:
            state = game.state()
            run.shot(game, f"{label}: at {state['pos'][0]:.1f}, {state['pos'][1]:.1f}, yaw {state['yaw']:.0f}")

    game.call("weather clear")
    time.sleep(SETTLE)
    began = time.monotonic()
    picture("loaded")
    turn_right(game)
    picture("turned right")
    walked = walk_forward(game)
    picture("walked")
    turn_right(game)
    picture("turned right again")
    print(f"route: {time.monotonic() - began:.1f} s")
    return walked


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="pictures instead of a session log")
    check = parser.parse_args().check

    run = Run("perftest route") if check else None
    with launch(SAVE, args=() if check else ["-perftest"]) as game:
        route(game, run)
        if not check:
            time.sleep(HOLD)
    if run:
        print(run.folder / "index.html")


if __name__ == "__main__":
    try:
        main()
    except GameError as error:
        raise SystemExit(f"session failed: {error}")
