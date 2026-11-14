"""Difficulty Overhaul's grenades and its flank at a player behind cover, on the test range, invincibility on.
The player stands behind cover and fires a round over the riflemen; once they fire back he crouches out of
their sight. Throw: three riflemen 28 m north of the low wall; who is asked to throw and the throw plan he
runs. Flank: three riflemen 38 m off on open ground; the plans each runs once the player is hidden, and how far across and toward him he gets. Wall,
only when named: the player behind the 18 m wall north of the long wall, four riflemen south of the long
wall, which they cannot get round, so they stay 24 m off at it; recorded with NVIDIA from the player
hiding, his view tilted up over his wall.

    python grenades.py [throw | flank | wall]
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import brain
from damage import first_match
from hard_to_hit import SHOT, Fight, aim, seconds
from perception import BLIND, CLEAR, SAVE, TRACE_SWITCH, WALL_START, Log, put, spawn, spawn_alive, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import GameError, Run, launch

GUN = "weapons.Primary.AS50"
# Each case: where the player stands, the way toward the riflemen, how far off they stand, how far across
# each, how long he stays hidden, and whether it is recorded. The wall runs only when named.
CASES = {"throw": (WALL_START, (0.0, 1.0), 28.0, (-4.0, 0.0, 4.0), 40.0, False),
         "flank": (WALL_START, (0.0, 1.0), 38.0, (-4.0, 0.0, 4.0), 30.0, False),
         "wall": ((64.0, 462.5), (0.0, -1.0), 38.5, (-9.0, -3.0, 3.0, 9.0), 40.0, True)}
DEFAULT = ("throw", "flank")
# How far up a recorded view looks over the player's cover, in degrees, and the hour it is lit at.
CLIP_PITCH = 20.0
NOON = 12
# How long the riflemen have to fire back, and how long the player stands after.
FIGHT_LIMIT = 15.0
AFTER = 0.5
GRENADES = re.compile(r"grenades: soldier (\w{8}) (.*)")
# The plans of the flank and the throw, as the trace names them below the combat brain.
WATCHED = ("NoCoverOrNoPathFailure", "FlankAttack_KR1", "FollowPathToFlankPos", "IfInTheOpenAfterFlank",
           "NoCoverRunToThreatPlan", "FindPathToNearCover", "FindCoverNear", "FleeFromLocation_BP1",
           "ThrowRequested", "CombatThrowProjectile", "ComputeProjectileTrajectory", "ComputeHigherLob",
           "ComputeFlatterThrow", "CombatThrowProjectile/ThrowGrenade")


def watched(task):
    return any(task == name or task.endswith("/" + name) for name in WATCHED)


class Layout:
    """A case's ground: the player's spot, the way toward the riflemen and the way across it."""

    def __init__(self, spot, toward):
        self.spot = spot
        self.toward = toward
        self.across = (toward[1], -toward[0])

    def at(self, ahead, across):
        return (self.spot[0] + self.toward[0] * ahead + self.across[0] * across,
                self.spot[1] + self.toward[1] * ahead + self.across[1] * across)

    def offsets(self, x, y):
        """How far across and how far ahead of the player a position is."""
        dx, dy = x - self.spot[0], y - self.spot[1]
        return dx * self.across[0] + dy * self.across[1], dx * self.toward[0] + dy * self.toward[1]


def set_up(game, log, known, layout, distance, spread):
    """The player standing at his spot facing the riflemen, each `distance` ahead at one of `spread` across."""
    game.lua(CLEAR)
    game.give(GUN)
    put(game, *layout.spot, spawn_yaw(*layout.toward))
    spawn(game, BLIND, 180.0)
    time.sleep(3.0)
    log.new_lines()
    fight = Fight(known)
    facing = spawn_yaw(-layout.toward[0], -layout.toward[1])
    for across in spread:
        fight.read(spawn_alive(game, log, layout.at(distance, across), facing, clear=False))
    return fight


def provoke(game, log, fight, layout, distance):
    """A round over the riflemen's heads, then until they fire back, and a little more."""
    aim(game, layout.spot, layout.at(distance, 0.0), 0.0, 5.0)
    game.fire(0.05)
    lines = []
    first_match(log, SHOT, FIGHT_LIMIT, lines)
    time.sleep(AFTER)
    fight.read(lines + log.new_lines())


def report(run_, fight, layout, start, name):
    """Each soldier's grenade lines and watched plans with times from the player hiding, and where he went."""
    for line in fight.lines:
        if m := GRENADES.search(line):
            t = seconds(line)
            when = f" +{t - start:.1f} s" if t is not None and t >= start else ""
            run_.note(f"{name}, {m.group(1)}{when}: {m.group(2)}")
    for soldier, tasks in fight.tasks.items():
        ran = [(t - start, task) for t, task in tasks if t is not None and t >= start and watched(task)]
        ran = [r for i, r in enumerate(ran) if i == 0 or r[1] != ran[i - 1][1]]
        track = [layout.offsets(x, y) for t, x, y in fight.track[soldier] if t is not None and t >= start]
        moved = ""
        if track:
            (a0, o0), (a1, o1) = track[0], track[-1]
            moved = f"; from {a0:+.0f} m across, {o0:.0f} m off to {a1:+.0f} m across, {o1:.0f} m off"
        run_.note(f"{name}, {soldier}: " + (", ".join(f"{task} +{t:.1f}" for t, task in ran[:24]) or "no watched plan")
                  + moved)


def case(run_, game, log, known, name):
    spot, toward, distance, spread, hidden, recorded = CASES[name]
    layout = Layout(spot, toward)
    fight = set_up(game, log, known, layout, distance, spread)
    provoke(game, log, fight, layout, distance)
    game.action("crouch", 1.0)
    start = next((t for line in reversed(fight.lines) if (t := seconds(line)) is not None), 0.0)
    if recorded:
        game.look(spawn_yaw(*toward), CLIP_PITCH)
        try:
            with run_.record(game, f"{name}: grenades over the cover"):
                time.sleep(hidden)
        except GameError as error:
            run_.note(f"{name}: no clip, {error}")
    else:
        time.sleep(hidden)
    fight.read(log.new_lines())
    report(run_, fight, layout, start, name)
    (run_.folder / f"{name}.log").write_text("\n".join(fight.lines) + "\n", encoding="utf-8")


def main():
    run_ = Run("grenades")
    log = Log()
    only = [name for name in CASES if name in sys.argv[1:]] or list(DEFAULT)
    known, _ = brain.names()
    with launch(SAVE, [TRACE_SWITCH, brain.SWITCH]) as game:
        game.option("Invincibility", 1)
        game.option("Infinite ammo", 1)
        game.option("FPS counter", False)
        game.fix_light(hour=NOON)
        for name in only:
            case(run_, game, log, known, name)
    print(run_.folder)


if __name__ == "__main__":
    main()
