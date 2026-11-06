"""Difficulty Overhaul's squad tactics on the test range, on open ground east of the hub with no cover in
reach: a squad of three riflemen 40 m off, in their long band, sees the player, who stands still, and
fights him. The squad splits into suppressors, who fire from where they stand, and assaulters; with the
game's flanking switched on at long and medium range, an assaulter with no cover to reach should run out
to the player's side rather than straight at him. Reports how far round the player each went and how
much closer he came, from his positions in the perception trace, the distances a fighting soldier
notices and recognises the player from, 1.5 times an idle one's, and the combat plans each ran and his
flank runs, from the brain trace; each soldier's whole run of tasks goes in a file in the run's folder.

    python tactics.py
"""

import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import brain
from damage import SOLDIERS_GUN, first_match
from perception import CLEAR, SAVE, TRACE_SWITCH, WARM_UP, Log, put, run_case, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

PLAYER = (440.0, 180.0)
DISTANCE = 40.0
# Each soldier's offset across the line to the player.
SQUAD = (-8.0, 0.0, 8.0)
TRIES = 3
START_LIMIT = 15.0
WATCH = 15.0
# The run to a flanking spot, the end of every flank.
FLANK = "/FollowPathToFlankPos"
SHOT = re.compile(r"shot at player:")
TRACE = re.compile(r"soldier (\w{8}) perception trace: (\S+) m, .*notices from (\S+) m, recognises from (\S+) m; "
                   r"he stands at (\S+), (\S+)")


def fight(run, game, log, known, plans, attempt):
    game.lua(CLEAR)
    put(game, *PLAYER, spawn_yaw(0.0, 1.0))
    log.new_lines()
    for across in SQUAD:
        spawn(game, (PLAYER[0] + across, PLAYER[1] + DISTANCE), spawn_yaw(0.0, -1.0))
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
        angles = [spawn_yaw(float(m.group(5)) - PLAYER[0], float(m.group(6)) - PLAYER[1]) for m in track]
        turn = max(abs((a - angles[0] + 180.0) % 360.0 - 180.0) for a in angles)
        run.note(f"try {attempt}, {soldier}: {first.group(2)} m -> {last.group(2)} m in {WATCH:.0f} s, round the "
                 f"player by up to {turn:.0f} degrees; he notices a still player from {last.group(3)} m and "
                 f"recognises him from {last.group(4)} m")
    for soldier, started in brain.tasks(lines, known).items():
        (run.folder / f"brain-{attempt}-{soldier}.txt").write_text("\n".join(started) + "\n")
        ran = list(dict.fromkeys(task for task in started if task in plans))
        flanks = sum(task.endswith(FLANK) for task in started)
        run.note(f"try {attempt}, {soldier}: plans {', '.join(ran)}; {flanks} flank runs")


def main():
    run = Run("tactics")
    log = Log()
    known, plans = brain.names()
    with launch(SAVE, [TRACE_SWITCH, brain.SWITCH]) as game:
        game.option("Invincibility", 1)
        game.give(SOLDIERS_GUN)
        # The first soldier spawned after the load sees nothing.
        run_case(game, log, WARM_UP)
        for attempt in range(1, TRIES + 1):
            fight(run, game, log, known, plans, attempt)
    print(run.folder)


if __name__ == "__main__":
    main()
