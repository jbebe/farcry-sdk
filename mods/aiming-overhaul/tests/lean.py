"""Leaning on the test range: held right and then left, the view rolls about 15 degrees and moves out to
that side, and comes back when let go.
"""

import math
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "tools" / "fc2drive"))
from fc2drive import Run

# Open ground on the range.
LANE = (256.0, 110.0, 16.5)
# Seconds a lean takes to settle, and to come back.
SETTLE = 1.0
# The bend's roll in degrees, and how far out the eye goes: the 10 cm slide plus the bend.
ROLL = (12.0, 18.0)
OUT = (0.1, 0.5)
STILL = 0.02


def view(game):
    camera = game.state()["camera"]
    return camera["pos"], camera["roll"], math.radians(camera["yaw"])


def lean(game, side):
    """The view held leaned to `side`, then let go: how far it rolled and went out to that side, and
    how far from where it started it came back to."""
    start, _, yaw = view(game)
    game.action(f"start_lean_{side}", 1.0)
    time.sleep(SETTLE)
    leaned, roll, _ = view(game)
    game.action(f"stop_lean_{side}", 0.0)
    time.sleep(SETTLE)
    back, _, _ = view(game)
    # Right of the facing, as a yaw of 0 faces +y.
    right = (math.cos(yaw), math.sin(yaw))
    out = ((leaned[0] - start[0]) * right[0] + (leaned[1] - start[1]) * right[1]) * (1.0 if side == "right" else -1.0)
    return abs(roll), out, math.dist(start, back)


run = Run("lean", mods=["aiming-overhaul"])
with run.launch("demorange") as game:
    game.option("Invincibility", 1)
    game.setting("AimingOverhaul", "Sway", False)
    game.goto(*LANE, yaw=0.0)
    game.look(0.0, 0.0)
    time.sleep(2.0)

    for side in ("right", "left"):
        roll, out, back = lean(game, side)
        run.expect_between(f"leaning {side} rolls the view, degrees", roll, *ROLL)
        run.expect_between(f"leaning {side} moves the eye out, metres", out, *OUT)
        run.expect_between(f"letting go of {side} brings it back, metres", back, 0.0, STILL)
run.finish()
