r"""What each of Sky Overhaul's switches costs: one scripted session that plays the route, then turns
each switch off for 10 s in turn, with 10 s of every switch on between them, and all of them off at
the end. With --alone it starts from all of them off and turns each on by itself instead, which also
counts what a part shares with others. --weather holds one of the mod's weathers by id and --hour
sets the clock, both before the route, so rain, storms and the night cost what they cost. The session
log, its segments and the report go into the newest perftest run folder.

    python -I switches.py [--alone] [--weather O3] [--hour 23]
    python -I switches.py --report <run folder>\switches-1.csv
"""

import argparse
import csv
import json
import math
import statistics
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from session import SAVE, route
from fc2drive import GameError, install, launch

PLUGIN = "SkyOverhaul"
# Each switch with its value on and off; off is the engine's own drawing, or none.
SWITCHES = [(name, "Overhaul", "Engine") for name in
            ("Sky", "Clouds", "Sun", "Night", "Shadows", "Grade", "Grass", "Leaves", "Sun shadows",
             "Weather", "Rain")] + \
           [(name, "On", "Off") for name in ("Grain", "Occlusion", "Heat haze", "Flatten")]
SEGMENT = 10.0
# Seconds of a segment left out at its start, for a switch to take hold, and at its end.
LEAD = 2.0
TAIL = 0.5
# Metres the view moves before a frame of the log counts as the walk's start.
MOVED = 0.05
ALL_ON = "all on"
ALL_OFF = "all off"


def play(alone, weather, hour):
    """Plays the session and returns its segments, timed in seconds from the walk's start."""
    segments = []
    with launch(SAVE, args=["-perftest"]) as game:
        # Six minutes stood in the open by day is long enough for a patrol to find the player.
        game.option("Invincibility", 1)
        if weather:
            game.sky_weather(weather)
        if hour is not None:
            game.lua(f"CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay({hour}, 0)")
        walked = route(game)

        def segment(label, values):
            for name, value in values.items():
                game.setting(PLUGIN, name, value)
            start = time.monotonic()
            time.sleep(SEGMENT)
            segments.append({"label": label, "start": start - walked, "end": time.monotonic() - walked})
            print(f"{label}: done")

        on = {name: value for name, value, _ in SWITCHES}
        off = {name: value for name, _, value in SWITCHES}
        reference, other = (ALL_OFF, ALL_ON) if alone else (ALL_ON, ALL_OFF)
        segment(reference, off if alone else on)
        for name in on:
            segment(f"{name} {'alone' if alone else 'off'}", {name: (on if alone else off)[name]})
            segment(reference, {name: (off if alone else on)[name]})
        segment(other, on if alone else off)
        segment(reference, off if alone else on)
    return segments


def keep(segments, began):
    """Moves this launch's session log into the newest run folder, its segments beside it."""
    log = max((path for path in (install() / "bin").glob("DevTools-session-*.csv") if path.stat().st_mtime > began),
              key=lambda path: path.stat().st_mtime, default=None)
    if log is None:
        raise SystemExit("the session wrote no log: is DevTools in the copy?")
    runs = Path(r"C:\Games\fc2instances\perftest") / install().name
    folder = max(runs.iterdir(), key=lambda path: path.name)
    n = 1
    while (folder / f"switches-{n}.csv").exists():
        n += 1
    target = folder / f"switches-{n}.csv"
    log.replace(target)
    target.with_suffix(".json").write_text(json.dumps(segments, indent=1), encoding="utf-8")
    return target


def measure(log):
    """Each segment's frame time, GPU time and slowest 1% of frames, in ms."""
    segments = json.loads(log.with_suffix(".json").read_text(encoding="utf-8"))
    rows = list(csv.DictReader(line for line in log.open(encoding="utf-8") if not line.startswith("#")))
    play = [row for row in rows if row["in_play"] == "1" and row["x"]]
    home = float(play[0]["x"]), float(play[0]["y"])
    zero = next(float(row["t"]) for row in play
                if math.dist(home, (float(row["x"]), float(row["y"]))) > MOVED)
    for segment in segments:
        low, high = zero + segment["start"] + LEAD, zero + segment["end"] - TAIL
        inside = [row for row in play if low <= float(row["t"]) < high]
        frame = sorted(float(row["frame_ms"]) for row in inside)
        segment["frame"] = statistics.mean(frame)
        segment["slowest"] = frame[int(len(frame) * 0.99)]
        segment["gpu"] = statistics.mean(float(row["gpu_ms"]) for row in inside if row["gpu_ms"])
    return segments


def report(log):
    segments = measure(log)
    alone = any(segment["label"].endswith(" alone") for segment in segments)
    # What a part adds: its segment over the reference's when it is turned on alone, the reference's
    # over its segment when it is turned off.
    reference, sign, verb = (ALL_OFF, 1, "costs") if alone else (ALL_ON, -1, "saves")
    refs = [segment for segment in segments if segment["label"] == reference]
    lines = [f"{log.name}: each part {'alone' if alone else 'off'} against the {reference} segments either "
             f"side, ms per frame",
             f"{reference}, {len(refs)} segments: frame {statistics.mean(s['frame'] for s in refs):.2f} "
             f"({min(s['frame'] for s in refs):.2f} to {max(s['frame'] for s in refs):.2f}), "
             f"GPU {statistics.mean(s['gpu'] for s in refs):.2f}",
             "",
             f"{'part':<18}{'frame':>8}{'GPU':>8}{verb + ' frame':>13}{verb + ' GPU':>11}"
             f"{'slowest 1%, ' + reference + ' -> part':>30}"]
    for i, segment in enumerate(segments):
        if segment["label"] == reference:
            continue
        around = [s for s in (segments[i - 1], segments[i + 1] if i + 1 < len(segments) else None)
                  if s and s["label"] == reference]
        frame, gpu, slowest = (statistics.mean(s[key] for s in around) for key in ("frame", "gpu", "slowest"))
        lines.append(f"{segment['label']:<18}{segment['frame']:8.2f}{segment['gpu']:8.2f}"
                     f"{sign * (segment['frame'] - frame):13.2f}{sign * (segment['gpu'] - gpu):11.2f}"
                     f"{slowest:21.2f} -> {segment['slowest']:.2f}")
    text = "\n".join(lines)
    print(text)
    log.with_suffix(".txt").write_text(text + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--alone", action="store_true", help="each part on by itself, from all of them off")
    parser.add_argument("--weather", help="one of Sky Overhaul's weathers to hold, by id, such as O3")
    parser.add_argument("--hour", type=int, help="the hour to set the clock to")
    parser.add_argument("--report", type=Path, help="report a saved session again")
    options = parser.parse_args()
    if options.report:
        report(options.report)
        return
    began = time.time()
    try:
        segments = play(options.alone, options.weather, options.hour)
    except GameError as error:
        raise SystemExit(f"session failed: {error}")
    report(keep(segments, began))


if __name__ == "__main__":
    main()
