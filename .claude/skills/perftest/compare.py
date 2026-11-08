"""Compares a perftest run's DevTools session logs: python -I compare.py <run folder>

The folder holds baseline-<n>.csv and mod-<n>.csv. Frames count only in play, past the settle time
after each entry into play. A difference gets a 95% interval from a bootstrap over blocks a sixth of
the run's length (run.json), 5 to 20 seconds, since neighbouring frames are not independent: an
interval that spans zero is noise. Writes report.txt and report.png into the folder as well.
"""
import csv
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

import numpy as np

SETTLE_S = 10.0
BOOTSTRAP = 4000
# Set from the run's length by main.
BLOCK_S = 20.0
MIN_SEGMENT_S = 30.0
LABELS = ("baseline", "mod")
SEGMENTS = (("play", "all play"), ("foot", "on foot"), ("vehicle", "in a vehicle"))

out_lines = []


def say(text=""):
    print(text)
    out_lines.append(text)


class Log:
    def __init__(self, path):
        lines = path.read_text().splitlines()
        self.name = path.name
        self.header = [line[2:] for line in lines if line.startswith("#")]
        rows = list(csv.DictReader(line for line in lines if not line.startswith("#")))
        self.c = {k: np.array([float(r[k]) if r[k] != "" else math.nan for r in rows]) for k in rows[0]}
        self.plugins = set()
        for line in self.header:
            if line.startswith("plugins: "):
                self.plugins = {p.strip() for p in line[9:].split(",") if p.strip()}
        self.display = self.header[1] if len(self.header) > 1 else ""
        self.settled = self._settled()
        self.engine_ms = self._engine_ms()

    def _settled(self):
        play = self.c["in_play"] == 1
        keep = play.copy()
        entered = 0.0
        for i in range(len(play)):
            if play[i] and (i == 0 or not play[i - 1]):
                entered = self.c["t"][i]
            if play[i] and self.c["t"][i] - entered < SETTLE_S:
                keep[i] = False
        return keep

    def _engine_ms(self):
        """Engine thread CPU per frame on each sample row: its share of a core times the frame time."""
        out = np.full(len(self.c["t"]), math.nan)
        total, count = 0.0, 0
        for i, (ms, busy) in enumerate(zip(self.c["frame_ms"], self.c["engine_thread"])):
            total += ms
            count += 1
            if not math.isnan(busy):
                out[i] = busy / 100.0 * total / count
                total, count = 0.0, 0
        return out

    def mask(self, segment):
        m = self.settled.copy()
        if segment == "foot":
            m &= self.c["in_vehicle"] == 0
        elif segment == "vehicle":
            m &= self.c["in_vehicle"] == 1
        return m

    def series(self, column):
        return self.engine_ms if column == "engine_ms" else self.c[column]


# name, label, column, how it is reduced, format, 1 when lower is better
METRICS = [
    ("frame_ms", "frame time", "frame_ms", "mean", "{:.2f} ms", 1),
    ("frame_p99", "frame time, slowest 1%", "frame_ms", "p99", "{:.2f} ms", 1),
    ("fps", "frame rate", "frame_ms", "fps", "{:.0f} fps", -1),
    ("fps_low", "frame rate, 1% low", "frame_ms", "low", "{:.0f} fps", -1),
    ("hitches", "hitches over 50 ms, per minute", "frame_ms", "hitches", "{:.2f}", 1),
    ("gpu_ms", "GPU time", "gpu_ms", "mean", "{:.2f} ms", 1),
    ("gpu_p99", "GPU time, slowest 1%", "gpu_ms", "p99", "{:.2f} ms", 1),
    ("engine_ms", "engine thread CPU per frame", "engine_ms", "mean", "{:.2f} ms", 1),
    ("engine_thread", "engine thread busy", "engine_thread", "mean", "{:.0f}% of a core", 1),
    ("process_cpu", "process CPU", "process_cpu", "mean", "{:.1f}% of all cores", 1),
    ("gpu_usage", "GPU usage", "gpu_usage", "mean", "{:.0f}%", 1),
    ("committed", "committed memory, peak", "committed_mb", "max", "{:.0f} MB", 1),
    ("private", "private bytes, peak", "private_mb", "max", "{:.0f} MB", 1),
    ("largest_free", "largest free block, lowest", "largest_free_mb", "min", "{:.0f} MB", -1),
    ("vram", "VRAM, peak", "vram_mb", "max", "{:.0f} MB", 1),
]
# Means of a series, where a block bootstrap says whether a change is more than noise.
BOOTSTRAPPED = {"frame_ms", "gpu_ms", "engine_ms", "engine_thread", "process_cpu", "gpu_usage"}


def values(logs, segment, column):
    parts = [log.series(column)[log.mask(segment)] for log in logs]
    v = np.concatenate(parts) if parts else np.array([])
    return v[~np.isnan(v)]


def reduce(logs, segment, column, how):
    v = values(logs, segment, column)
    if len(v) == 0:
        return math.nan
    if how == "mean":
        return v.mean()
    if how == "p99":
        return np.percentile(v, 99)
    if how == "fps":
        return 1000.0 / v.mean()
    if how == "low":
        return 1000.0 / np.sort(v)[-max(1, len(v) // 100):].mean()
    if how == "hitches":
        return np.sum(v > 50.0) / (v.sum() / 60000.0)
    if how == "max":
        return v.max()
    if how == "min":
        return v.min()
    raise ValueError(how)


def blocks(logs, segment, column):
    """(sum, count) of the column per BLOCK_S block of each log, blocks with too little data dropped."""
    out = []
    for log in logs:
        m = log.mask(segment)
        series = log.series(column)
        ids = np.floor(log.c["t"] / BLOCK_S).astype(int)
        acc = defaultdict(lambda: [0.0, 0])
        for b, x in zip(ids[m], series[m]):
            if not math.isnan(x):
                acc[b][0] += x
                acc[b][1] += 1
        least = 3 if column in ("engine_ms", "engine_thread", "process_cpu", "gpu_usage") else 20
        out += [(s, n) for s, n in acc.values() if n >= least]
    return np.array(out)


def interval(base, mod, segment, column):
    a, b = blocks(base, segment, column), blocks(mod, segment, column)
    if len(a) < 3 or len(b) < 3:
        return None
    rng = np.random.default_rng(1)
    ia = rng.integers(0, len(a), (BOOTSTRAP, len(a)))
    ib = rng.integers(0, len(b), (BOOTSTRAP, len(b)))
    diffs = b[ib, 0].sum(1) / b[ib, 1].sum(1) - a[ia, 0].sum(1) / a[ia, 1].sum(1)
    return np.percentile(diffs, [2.5, 97.5])


def seconds(logs, segment):
    return sum(np.sum(log.c["frame_ms"][log.mask(segment)]) for log in logs) / 1000.0


def distance_km(logs):
    total = 0.0
    for log in logs:
        m = log.mask("vehicle")
        xyz = np.stack([log.c["x"][m], log.c["y"][m], log.c["z"][m]], axis=1)
        step = np.linalg.norm(np.diff(xyz, axis=0), axis=1)
        total += float(np.sum(step[step < 5.0]))
    return total / 1000.0


def bound(logs):
    frame = reduce(logs, "play", "frame_ms", "mean")
    gpu = reduce(logs, "play", "gpu_ms", "mean")
    engine = reduce(logs, "play", "engine_thread", "mean")
    if gpu >= 0.9 * frame:
        return "GPU-bound: the GPU is busy for the whole frame"
    if engine >= 90.0:
        return "CPU-bound: the engine thread has no time to spare"
    return ("neither saturated: the frame rate is capped (a limiter or vsync) or waiting, so frame time "
            "shows a cost only once it passes the cap; read GPU time and engine CPU per frame")


def checks(groups):
    base, mod = groups["baseline"], groups["mod"]
    displays = {log.display for logs in groups.values() for log in logs}
    if len(displays) > 1:
        say("WARNING: the logs ran at different settings: " + " | ".join(sorted(displays)))
    base_plugins = set.intersection(*(log.plugins for log in base))
    mod_plugins = set.intersection(*(log.plugins for log in mod))
    added, removed = mod_plugins - base_plugins, base_plugins - mod_plugins
    say("plugins only with the mod: " + (", ".join(sorted(added)) or "none (a data-only mod?)"))
    if removed:
        say("WARNING: plugins only in the baseline: " + ", ".join(sorted(removed)))
    for segment, title in SEGMENTS:
        sa, sb = seconds(base, segment), seconds(mod, segment)
        line = f"{title}: baseline {sa:.0f} s, mod {sb:.0f} s"
        if segment == "vehicle" and (sa or sb):
            ka, kb = distance_km(base), distance_km(mod)
            line += f"; driven {ka:.1f} km and {kb:.1f} km"
        per_a, per_b = sa / len(base), sb / len(mod)
        if min(per_a, per_b) > 0 and min(per_a, per_b) < 0.6 * max(per_a, per_b):
            line += "  <- uneven: a run on one side did not do as much of this as one on the other"
        say(line)
    for label in LABELS:
        say(f"{label}: {bound(groups[label])}")


def table(groups, segment, title):
    base, mod = groups["baseline"], groups["mod"]
    if min(seconds(base, segment), seconds(mod, segment)) < MIN_SEGMENT_S:
        return
    say()
    say(f"== {title}")
    say(f"{'':34}{'baseline':>18}{'mod':>18}   change")
    for name, label, column, how, fmt, better in METRICS:
        if segment != "play" and how in ("max", "min"):
            continue
        a, b = reduce(base, segment, column, how), reduce(mod, segment, column, how)
        if math.isnan(a) or math.isnan(b):
            continue
        line = f"{label:34}{fmt.format(a):>18}{fmt.format(b):>18}"
        delta = b - a
        line += f"   {delta:+.2f}" + (f" ({100.0 * delta / abs(a):+.1f}%)" if a else "")
        if name in BOOTSTRAPPED:
            ci = interval(base, mod, segment, column)
            if ci is not None:
                lo, hi = ci
                verdict = "noise" if lo <= 0.0 <= hi else ("worse" if (lo > 0) == (better > 0) else "better")
                line += f"  [{lo:+.2f} to {hi:+.2f}] {verdict}"
        say(line)


def plot(groups, folder):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return
    fig, (top, bottom) = plt.subplots(2, 1, figsize=(11, 8))
    colours = {"baseline": "tab:blue", "mod": "tab:orange"}
    for label, logs in groups.items():
        for log in logs:
            m = log.settled
            top.plot(log.c["t"][m], log.c["frame_ms"][m], lw=0.4, alpha=0.6, color=colours[label],
                     label=log.name)
            f = np.sort(log.c["frame_ms"][m])
            bottom.plot(f, np.linspace(0, 1, len(f)), color=colours[label], label=log.name)
    top.set_xlabel("seconds into the log")
    top.set_ylabel("frame ms, in play")
    top.legend()
    every = np.concatenate([log.c["frame_ms"][log.settled] for logs in groups.values() for log in logs])
    bottom.set_xlim(0, np.percentile(every, 99.5))
    bottom.set_xlabel("frame ms")
    bottom.set_ylabel("share of frames at or under")
    bottom.legend()
    fig.tight_layout()
    fig.savefig(folder / "report.png", dpi=110)
    say(f"\nplot: {folder / 'report.png'}")


def main():
    global BLOCK_S, MIN_SEGMENT_S
    folder = Path(sys.argv[1])
    length = json.loads((folder / "run.json").read_text(encoding="utf-8-sig"))["length"]
    BLOCK_S = min(20.0, max(5.0, length / 6.0))
    MIN_SEGMENT_S = min(30.0, length / 2.0)
    groups = {label: [Log(p) for p in sorted(folder.glob(f"{label}-*.csv"))] for label in LABELS}
    for label in LABELS:
        if not groups[label]:
            sys.exit(f"no {label}-*.csv in {folder}")
        say(f"{label}: " + ", ".join(log.name for log in groups[label]))
    say(f"runs of {length:.0f} s: {BLOCK_S:.0f} s blocks, a segment needs {MIN_SEGMENT_S:.0f} s a side")
    say(groups["baseline"][0].display)
    checks(groups)
    for segment, title in SEGMENTS:
        table(groups, segment, title)
    say("\n[interval] is the 95% range of the change; 'noise' when it spans zero. All play mixes on foot")
    say("and in a vehicle, so its interval is wider: read the segment the mod works in. Memory has no")
    say("interval: its peaks follow where the session went as much as the mod.")
    plot(groups, folder)
    (folder / "report.txt").write_text("\n".join(out_lines) + "\n")


if __name__ == "__main__":
    main()
