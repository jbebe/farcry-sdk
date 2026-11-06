"""Difficulty Overhaul's squad tactics on the test range: a squad of three riflemen fights the player,
who stands still, in one of four arenas. In the open, with no cover in reach, an assaulter runs out to the
player's side and a soldier crouches to fire where he stops. At the wall, the squad stands north of the
low wall with the player south of it, so a soldier fires from behind it. Beside it, the wall lies off to
one side with nothing between the player and the squad, so a soldier whose cover is far holds and fires
crouched. Behind it, the player crouches behind the wall once shot at, so a soldier makes for cover near
himself or rushes him. Within 35 m half the squad suppresses.

Reports how far round the player each soldier went and how much closer he came, from the perception
trace, the distances a fighting soldier notices and recognises the player from, and from the brain trace
his plans, roles, flank runs, cover attacks, crouches, holds, searches and rushes; each soldier's whole
run of tasks goes in a file in the run's folder.

    python tactics.py [open|wall|side|behind] [distance] [crouched]
"""

import re
import sys
import time
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import brain
from damage import SOLDIERS_GUN, first_match
from perception import CLEAR, SAVE, TRACE_SWITCH, WARM_UP, Log, put, run_case, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

# Where the player stands in each arena and how far north of him the squad stands by default; the low
# wall lies across (100, 40), 28 m from the player at the wall, 33 m beside it and 3 m behind it.
ARENAS = {"open": ((440.0, 180.0), 40.0), "wall": ((100.0, 12.0), 36.0), "side": ((70.0, 25.0), 40.0),
          "behind": ((100.0, 37.0), 36.0)}
# Each soldier's offset across the line to the player.
SQUAD = (-8.0, 0.0, 8.0)
TRIES = 3
START_LIMIT = 30.0
WATCH = 15.0
# The ends of task names that show a soldier's role, his flank runs, how he fired from cover and where
# he crouched to fire in the open, and the cover attack an assaulter chooses.
ROLES = ("AssaultAttack", "SuppressAttack")
FLANK = "FollowPathToFlankPos"
COVER_ATTACKS = ("PopUpAttack", "StrafeAttack", "StandUpAttack")
CROUCH = "CrouchInTheOpen"
# Where a soldier held where he stood to fire, looked for cover near himself, and rushed the player.
HOLD = "HoldInTheOpen"
NEAR = "FindCoverNear"
RUSHES = ("RushHim", "NoCoverMoveAttack")
ASSAULT_COVER = "AssaultAttack/CTaskChooseCoverAttack1"
SHOT = re.compile(r"shot at player:")
TRACE = re.compile(r"soldier (\w{8}) perception trace: (\S+) m, .*notices from (\S+) m, recognises from (\S+) m; "
                   r"he stands at (\S+), (\S+)")
STANCE = re.compile(r"soldier (\w{8}) shot at player: .*, he is (\w+);")


def fight(run, game, log, known, plans, player, distance, crouched, attempt):
    game.lua(CLEAR)
    put(game, *player, spawn_yaw(0.0, 1.0))
    log.new_lines()
    for across in SQUAD:
        spawn(game, (player[0] + across, player[1] + distance), spawn_yaw(0.0, -1.0))
    # Everything since they spawned, the brain's first plans with it.
    lines = []
    if first_match(log, SHOT, START_LIMIT, lines) is None:
        run.note(f"try {attempt}: they never opened fire")
        return
    # Shot at, he takes cover, and stands again after; the crouch key toggles.
    if crouched:
        game.action("crouch", 1.0)
    time.sleep(WATCH)
    lines += log.new_lines()
    if crouched:
        game.action("crouch", 1.0)
    tracks = {}
    for m in filter(None, map(TRACE.search, lines)):
        tracks.setdefault(m.group(1), []).append(m)
    for soldier, track in tracks.items():
        first, last = track[0], track[-1]
        angles = [spawn_yaw(float(m.group(5)) - player[0], float(m.group(6)) - player[1]) for m in track]
        turn = max(abs((a - angles[0] + 180.0) % 360.0 - 180.0) for a in angles)
        run.note(f"try {attempt}, {soldier}: {first.group(2)} m -> {last.group(2)} m in {WATCH:.0f} s, round the "
                 f"player by up to {turn:.0f} degrees; he notices a still player from {last.group(3)} m and "
                 f"recognises him from {last.group(4)} m")
    stances = defaultdict(Counter)
    for m in filter(None, map(STANCE.search, lines)):
        stances[m.group(1)][m.group(2)] += 1
    for soldier, started in brain.tasks(lines, known).items():
        (run.folder / f"brain-{attempt}-{soldier}.txt").write_text("\n".join(started) + "\n")
        ran = list(dict.fromkeys(task for task in started if task in plans))
        ends = Counter(task.rsplit("/", 1)[-1] for task in started)
        chose = sum(task.endswith(ASSAULT_COVER) for task in started)
        run.note(f"try {attempt}, {soldier}: plans {', '.join(ran)}; roles {tally(ends, ROLES)}; "
                 f"{ends[FLANK]} flank runs; from cover {tally(ends, COVER_ATTACKS)}, chosen as an assaulter "
                 f"{chose} times; crouched {ends[CROUCH]} times; held {ends[HOLD]}, searched near {ends[NEAR]}, "
                 f"rushed {tally(ends, RUSHES)}; shots {tally(stances[soldier])}")


def tally(counts, names=None):
    """Each name's count, of those given or all, leaving out the ones never counted."""
    return ", ".join(f"{name} {counts[name]}" for name in (names or counts) if counts[name]) or "none"


def main():
    crouched = "crouched" in sys.argv[1:]
    args = [arg for arg in sys.argv[1:] if arg != "crouched"]
    arena = args[0] if args else "open"
    player, distance = ARENAS[arena]
    if len(args) > 1:
        distance = float(args[1])
    run = Run(f"tactics-{arena}" + ("-crouched" if crouched else ""))
    log = Log()
    known, plans = brain.names()
    with launch(SAVE, [TRACE_SWITCH, brain.SWITCH]) as game:
        game.option("Invincibility", 1)
        game.give(SOLDIERS_GUN)
        # The first soldier spawned after the load sees nothing.
        run_case(game, log, WARM_UP)
        for attempt in range(1, TRIES + 1):
            fight(run, game, log, known, plans, player, distance, crouched, attempt)
    print(run.folder)


if __name__ == "__main__":
    main()
