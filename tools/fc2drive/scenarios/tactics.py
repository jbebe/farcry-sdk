"""Difficulty Overhaul's squad tactics on the test range: a squad of three riflemen sees the player, who
stands still, and fights him, in one of two arenas. In the open, on ground east of the hub with no cover
in reach, an assaulter runs out to the player's side at long and medium range, 20 m and 15 m across his
line at each run, and each soldier crouches to fire where he stops. At the low wall the squad stands
north of its barriers with the player south of them, so a soldier fires from behind the wall: a
suppressor as the game has him, an assaulter now too. Within 35 m half the squad suppresses, rounded
down for the assaulters.

Reports how far round the player each soldier went and how much closer he came, from his positions in
the perception trace, the distances a fighting soldier notices and recognises the player from, 1.5
times an idle one's, and from the brain trace his plans, roles, flank runs, cover attacks and crouches;
each soldier's whole run of tasks goes in a file in the run's folder.

    python tactics.py [open|wall] [distance]     the open at 40 m by default, the wall at 36 m
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

# Where the player stands in each arena and how far north of him the squad stands by default: the low
# wall's barriers lie across (100, 40), 28 m from the player, where an assaulter in his medium band looks
# for cover, 25 to 35 m from his target.
ARENAS = {"open": ((440.0, 180.0), 40.0), "wall": ((100.0, 12.0), 36.0)}
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
ASSAULT_COVER = "AssaultAttack/CTaskChooseCoverAttack1"
SHOT = re.compile(r"shot at player:")
TRACE = re.compile(r"soldier (\w{8}) perception trace: (\S+) m, .*notices from (\S+) m, recognises from (\S+) m; "
                   r"he stands at (\S+), (\S+)")
STANCE = re.compile(r"soldier (\w{8}) shot at player: .*, he is (\w+);")


def fight(run, game, log, known, plans, player, distance, attempt):
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
    time.sleep(WATCH)
    lines += log.new_lines()
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
                 f"{chose} times; crouched {ends[CROUCH]} times; shots {tally(stances[soldier])}")


def tally(counts, names=None):
    """Each name's count, of those given or all, leaving out the ones never counted."""
    return ", ".join(f"{name} {counts[name]}" for name in (names or counts) if counts[name]) or "none"


def main():
    arena = sys.argv[1] if len(sys.argv) > 1 else "open"
    player, distance = ARENAS[arena]
    if len(sys.argv) > 2:
        distance = float(sys.argv[2])
    run = Run(f"tactics-{arena}")
    log = Log()
    known, plans = brain.names()
    with launch(SAVE, [TRACE_SWITCH, brain.SWITCH]) as game:
        game.option("Invincibility", 1)
        game.give(SOLDIERS_GUN)
        # The first soldier spawned after the load sees nothing.
        run_case(game, log, WARM_UP)
        for attempt in range(1, TRIES + 1):
            fight(run, game, log, known, plans, player, distance, attempt)
    print(run.folder)


if __name__ == "__main__":
    main()
