"""Reads a Difficulty Overhaul combat log and reports its fights. README.md says how to use it."""

import argparse
import math
import os
import re
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

LOGS = Path(os.environ.get("FC2_GAME", r"C:\Games\Far Cry 2")) / "bin" / "difficulty-overhaul-logs"

# A session file's line, then an fcse.log line from this plugin.
SESSION_LINE = re.compile(r"^(\d\d):(\d\d):(\d\d)\.(\d+) (.*)$")
FCSE_LINE = re.compile(r"^\[[\d-]+ (\d\d):(\d\d):(\d\d)\.(\d+)\]\[difficultyoverhaul\] (.*)$")

# Lines from before the session files lack the soldier, the shake, the weapon and the shooter.
SHOT = re.compile(
    r"^(?:soldier (\w{8}) )?shot at player: chance (\S+) \(.*?\) at (\S+) m, .*?, (hit|miss)"
    r"(?:; visibility \S+, (.+?); the player (.+))?$")
DISCIPLINE = re.compile(r"^(?:soldier (\w{8}) )?fire discipline: holds his fire")
SUPPRESSED = re.compile(r"^(?:soldier (\w{8}) )?suppressed: (hit, he panics|near miss)")
WATCHED = re.compile(r"^(?:soldier (\w{8}) )?watched spot:")
SOLDIER_HIT = re.compile(r"^soldier (\w{8}) hit: .*? m, (.+?) \(")
PLAYER_HIT = re.compile(
    r"^player hit: \w+ level \d+, ([\w ]+), at (\S+) m, health (\S+) -> (\S+)"
    r"(?: of (\S+), by (\w{8}))? \((\w+)")
DIED = re.compile(r"^player died: killed by (\w{8}) at (\S+) m")

RANGES = [(0, 15), (15, 30), (30, 60), (60, 1e9)]


@dataclass
class Line:
    time: float
    text: str


@dataclass
class Soldier:
    id: str
    first: float = None
    weapon: str = ""
    shots: list = field(default_factory=list)
    holds: int = 0
    panics: int = 0
    shakes: int = 0
    restarts: int = 0
    hits_taken: list = field(default_factory=list)


def clock(seconds):
    whole = int(seconds)
    centis = int(seconds * 100) % 100
    return f"{whole // 3600 % 24:02}:{whole // 60 % 60:02}:{whole % 60:02}.{centis:02}"


def read_lines(path):
    lines = []
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        match = SESSION_LINE.match(raw) or FCSE_LINE.match(raw)
        if match is None:
            continue
        h, m, s, fraction, text = match.groups()
        seconds = int(h) * 3600 + int(m) * 60 + int(s) + float("0." + fraction)
        # A session past midnight keeps counting up.
        while lines and seconds < lines[-1].time - 43200:
            seconds += 86400
        lines.append(Line(seconds, text))
    return lines


def default_log():
    sessions = sorted(LOGS.glob("combat-*.log"))
    if not sessions:
        sys.exit(f"No combat logs in {LOGS}; pass a path.")
    return sessions[-1]


def analyse(lines):
    soldiers = {}
    deaths = []
    player_hits = []
    max_health = None
    difficulty = None

    def soldier(sid, time):
        s = soldiers.setdefault(sid or "unknown", Soldier(sid or "unknown"))
        if s.first is None:
            s.first = time
        return s

    for line in lines:
        text, t = line.text, line.time
        if m := SHOT.match(text):
            sid, chance, dist, outcome, weapon, player = m.groups()
            s = soldier(sid, t)
            s.weapon = weapon or s.weapon
            s.shots.append(dict(soldier=s.id, time=t, chance=float(chance), distance=float(dist),
                                hit=outcome == "hit", player=player))
        elif m := DISCIPLINE.match(text):
            soldier(m.group(1), t).holds += 1
        elif m := SUPPRESSED.match(text):
            s = soldier(m.group(1), t)
            if m.group(2) == "near miss":
                s.shakes += 1
            else:
                s.panics += 1
        elif m := WATCHED.match(text):
            soldier(m.group(1), t).restarts += 1
        elif m := SOLDIER_HIT.match(text):
            soldier(m.group(1), t).hits_taken.append(m.group(2))
        elif m := PLAYER_HIT.match(text):
            part, dist, before, after, maximum, by, diff = m.groups()
            player_hits.append(dict(time=t, part=part, distance=float(dist), before=float(before),
                                    after=float(after), by=by))
            max_health = maximum or max_health
            difficulty = diff
            if by is None and float(before) > 0 >= float(after):
                deaths.append(dict(time=t, by=None, distance=float(dist)))
        elif m := DIED.match(text):
            deaths.append(dict(time=t, by=m.group(1), distance=float(m.group(2))))
    return soldiers, deaths, player_hits, max_health, difficulty


def count(n, word):
    return f"{n} {word}" + ("" if n == 1 else "s")


def tally(shots):
    return f"{count(len(shots), 'shot')}, {count(sum(shot['hit'] for shot in shots), 'hit')}"


def percent(part, whole):
    return f"{100 * part / whole:.0f} %" if whole else "-"


def report(path, lines):
    if not lines:
        print(f"{path}: no combat lines.")
        return
    soldiers, deaths, player_hits, max_health, difficulty = analyse(lines)
    shots = [shot for s in soldiers.values() for shot in s.shots]
    counts = [("hold", "holds"), ("panic", "panics"), ("shake", "shakes"), ("restart", "restarts")]
    span = lines[-1].time - lines[0].time
    print(f"{path.name}: {clock(lines[0].time)} to {clock(lines[-1].time)} ({span / 60:.0f} min), "
          f"{difficulty or 'difficulty not logged'}, maximum health {max_health or 'not logged'}")
    print()
    print("Totals")
    hits = sum(shot["hit"] for shot in shots)
    print(f"  Shots at you: {len(shots)}, {hits} hits ({percent(hits, len(shots))})")
    for low, high in RANGES:
        band = [shot for shot in shots if low <= shot["distance"] < high]
        label = f"{low}+ m" if high > 1e8 else f"{low}-{high} m"
        print(f"    {label:>8}: {tally(band)}")
    results = [r for s in soldiers.values() for r in s.hits_taken]
    print(f"  Your hits on soldiers: {len(results)}, "
          f"{sum(any('killed' in r for r in s.hits_taken) for s in soldiers.values())} killed, "
          f"{sum(any('wounded' in r for r in s.hits_taken) for s in soldiers.values())} wounded")
    print(f"  Deaths: {len(deaths)}")
    totals = [count(sum(getattr(s, attribute) for s in soldiers.values()), label)
              for label, attribute in counts]
    print("  Soldiers: " + ", ".join(totals))

    flags = []
    for s in soldiers.values():
        nans = [shot for shot in s.shots if math.isnan(shot["chance"])]
        if nans:
            flags.append((nans[0]["time"], f"soldier {s.id}: {count(len(nans), 'shot')} with no "
                                           f"chance (nan), {s.weapon or 'weapon not logged'}"))
    for death in deaths:
        killer = soldiers.get(death["by"])
        first = next((shot["time"] for shot in killer.shots
                      if shot["time"] > death["time"] - 30), None) if killer else None
        if first is not None and death["time"] - first < 2.0:
            flags.append((death["time"], f"death {death['time'] - first:.1f} s after the killer's "
                                         f"first shot ({killer.id})"))
    if flags:
        print()
        print("Flags")
        for t, text in sorted(flags):
            print(f"  {clock(t)}  {text}")

    for death in deaths:
        print()
        killer = soldiers.get(death["by"])
        weapon = f", {killer.weapon}" if killer and killer.weapon else ""
        print(f"Death at {clock(death['time'])}: killed by {death['by'] or 'unknown'} at "
              f"{death['distance']:.1f} m{weapon}")
        recent = lambda t: death["time"] - 10 <= t <= death["time"]
        window = sorted((shot for shot in shots if recent(shot["time"])),
                        key=lambda shot: shot["time"])
        by_soldier = defaultdict(list)
        for shot in window:
            by_soldier[shot["soldier"]].append(shot)
        print(f"  Last 10 s: {count(len(by_soldier), 'soldier')} fired {tally(window)}")
        for sid, own in by_soldier.items():
            chances = [shot["chance"] for shot in own if not math.isnan(shot["chance"])]
            spread = f"chance {min(chances):.2f}-{max(chances):.2f}" if chances else "no chance"
            print(f"    {sid}: {tally(own)}, {spread}, {own[0]['distance']:.0f} m, "
                  f"first at {clock(own[0]['time'])}")
        for hit in player_hits:
            if recent(hit["time"]):
                print(f"    hit at {clock(hit['time'])}: {hit['part']}, {hit['before']:.0f} -> "
                      f"{hit['after']:.0f}, by {hit['by'] or 'unknown'} at {hit['distance']:.0f} m")
        last = next((shot for shot in reversed(window) if shot["player"]), None)
        if last:
            print(f"  You were {last['player']} at the last shot.")

    print()
    print("Soldiers")
    for s in sorted(soldiers.values(), key=lambda s: s.first):
        parts = [s.weapon or "weapon not logged"]
        if s.shots:
            best = max((shot["chance"] for shot in s.shots if not math.isnan(shot["chance"])),
                       default=0.0)
            parts.append(f"{tally(s.shots)} from {clock(s.shots[0]['time'])}, "
                         f"best chance {best:.2f}")
        extras = [count(getattr(s, attribute), label) for label, attribute in counts
                  if getattr(s, attribute)]
        if extras:
            parts.append(", ".join(extras))
        if s.hits_taken:
            parts.append(f"you hit him {len(s.hits_taken)}x: {s.hits_taken[-1]}")
        print(f"  {s.id}  " + " | ".join(parts))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", nargs="?", type=Path,
                        help="a combat log or fcse.log; the newest session's when left out")
    parser.add_argument("--at", help="print the lines around this time, HH:MM:SS")
    parser.add_argument("--window", type=float, default=10.0,
                        help="seconds before --at to print (default 10)")
    parser.add_argument("--soldier", help="print every line of this soldier")
    args = parser.parse_args()

    path = args.log or default_log()
    lines = read_lines(path)
    if args.at:
        h, m, s = (float(part) for part in args.at.split(":"))
        at = h * 3600 + m * 60 + s
        for line in lines:
            if at - args.window <= line.time % 86400 <= at + 2:
                print(f"{clock(line.time)} {line.text}")
    elif args.soldier:
        for line in lines:
            if args.soldier.upper() in line.text:
                print(f"{clock(line.time)} {line.text}")
    else:
        report(path, lines)


if __name__ == "__main__":
    main()
