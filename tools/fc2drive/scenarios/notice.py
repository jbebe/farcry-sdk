"""Difficulty Overhaul's notice delay on the test range: an idle rifleman walking the lane's patrol route
toward the player, by day, the player standing still, walking to him, or running to and fro. Reports
where he noticed the player and after how long in sight, beside where the model has him notice: the
delay walked over his traced looks, a quarter second apart, each with its own distance, zone and
notice distance. Idle soldiers turn as they please, so only a walk-in keeps him facing the player.

    python notice.py [--only still,"walking to him"]
"""

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from perception import FOCUS, SAVE, TRACE_SWITCH, WARM_UP, Log, run_case, run_walk

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

# The model: noticed at once within these lines in his focus and at his side, otherwise after
# (d - line)^2 / 400 s over how far inside his notice distance the player is, at most LONGEST; out of
# sight forgotten over FORGET.
AT_ONCE = {FOCUS: 30.0, "at his side": 20.0}
LONGEST = 4.0
FORGET = 2.0
TRACED = 0.25
# As perception.py's walk-ins: name, route, hour, how far ahead and aside the player stands, crouched,
# what he does, and the zone; the rest unused here.
CASES = [
    ("still", "lane", 12, 75, 0, False, "still", FOCUS, None, None, None),
    ("walking to him", "lane", 12, 140, 0, False, "walk", FOCUS, None, None, None),
    ("running to and fro", "lane", 12, 140, 0, False, "sidestep", FOCUS, None, None, None),
    ("crouched still", "lane", 12, 75, 0, True, "still", FOCUS, None, None, None),
]
# A traced look, or the look he noticed the player at: its distance, zone and notice distance.
LOOK = re.compile(r"perception(?: trace: |: (?:notices|recognises) the player at )(\S+) m, [^,]+, "
                  r"(in his focus|at his side|behind him)[^;]*; .*notices from (\S+) m")
ATTEMPTS = 3
NOTICED = re.compile(r"perception: (notices|recognises) the player at (\S+) m, .*; (\S+) s after first sighting him$")


def delay(distance, notice, zone):
    if distance <= AT_ONCE[zone]:
        return 0.0
    within = 1.0 - distance / notice
    return LONGEST if within <= 0.0 else min((distance - AT_ONCE[zone]) ** 2 / 400.0 / within, LONGEST)


def predicted(lines):
    """Where the model has him notice the player over his traced looks and the look he noticed him at,
    and the seconds in sight by then."""
    sat = 0.0
    sighted = None
    for count, (distance, zone, notice) in enumerate(
            (float(t.group(1)), t.group(2), float(t.group(3))) for t in map(LOOK.search, lines) if t):
        if zone in AT_ONCE and distance <= notice:
            sighted = count if sat <= 0.0 else sighted
            needed = delay(distance, notice, zone)
            sat = 1.0 if needed <= 0.0 else sat + TRACED / needed
            if sat >= 1.0:
                return distance, (count - sighted) * TRACED
        else:
            sat = max(sat - TRACED / FORGET, 0.0)
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--only", help="comma-separated case names")
    only = parser.parse_args().only
    cases = [case for case in CASES if only is None or case[0] in only.split(",")]
    run = Run("notice")
    log = Log()
    with launch(SAVE, [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        # The first soldier spawned after the load sees nothing.
        run_case(game, log, WARM_UP)
        for case in cases:
            # A soldier may take his route the other way, or turn back before he is near.
            for _ in range(ATTEMPTS):
                watch = run_walk(game, log, case)
                noticed = next((m for m in map(NOTICED.search, watch.lines) if m), None)
                judged = noticed is not None or min(watch.distances(), default=AT_ONCE[FOCUS] + 1) <= AT_ONCE[FOCUS]
                if judged:
                    break
            model = predicted(watch.lines)
            got = "never" if noticed is None else \
                f"{noticed.group(1)} at {float(noticed.group(2)):.1f} m after {noticed.group(3)} s"
            want = "never" if model is None else f"at {model[0]:.1f} m after {model[1]:.1f} s"
            run.note(f"{case[0]:<20} | model {want:<22} | {got}" + ("" if judged else " | he never came near"))
    print(run.folder)


if __name__ == "__main__":
    main()
