"""Shared plumbing for the address-library generator.

Deliberately free of Ghidra imports so the offline stages (match, score,
validate, mint, emit) can be run, tested and iterated without a JVM.
"""

import csv
import hashlib
import json
import os
import sys

if sys.version_info >= (3, 11):
    import tomllib
else:  # pragma: no cover - the project targets 3.14
    import tomli as tomllib

# Dunia.dll's preferred image base. Everything on disk and in the emitted table
# is an RVA; this is only used to translate the VA-shaped addresses that appear
# in FCSE sources and in the engine-internals docs.
PREFERRED_BASE = 0x10000000

TIER_EXACT = "exact"
TIER_NEAR_EXACT = "near_exact"
TIER_REVIEW = "review"
SHIPPING_TIERS = (TIER_EXACT, TIER_NEAR_EXACT)


def repo_root():
    """The FarCry2 checkout root, derived from this file's location."""
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.normpath(os.path.join(here, "..", "..", "..", ".."))


def tool_dir():
    return os.path.dirname(os.path.abspath(__file__))


def load_config(path=None):
    path = path or os.path.join(tool_dir(), "addrlib.toml")
    with open(path, "rb") as fh:
        cfg = tomllib.load(fh)
    cfg["_path"] = path
    cfg["_hash"] = file_sha256(path)
    return cfg


def resolve_project_location(cfg):
    loc = cfg["project"]["location"]
    if not os.path.isabs(loc):
        loc = os.path.join(repo_root(), loc)
    return os.path.normpath(loc)


def cache_dir(cfg, create=True):
    d = os.path.join(tool_dir(), "cache")
    if create:
        os.makedirs(d, exist_ok=True)
    return d


def out_dir(cfg, create=True):
    d = os.path.join(tool_dir(), "out")
    if create:
        os.makedirs(d, exist_ok=True)
    return d


# One build's extracted functions, keyed by RVA. `key` is "reference" or "target".
def load_functions(cfg, key):
    build_id = cfg["builds"][key]["id"]
    path = os.path.join(cache_dir(cfg, create=False), "%s.functions.jsonl" % build_id)
    return {r["rva"]: r for r in read_jsonl(path)}


# ---------------------------------------------------------------------------
# io
# ---------------------------------------------------------------------------
def file_sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def write_jsonl(path, rows):
    tmp = path + ".tmp"
    n = 0
    with open(tmp, "w", encoding="utf-8") as fh:
        for row in rows:
            fh.write(json.dumps(row, separators=(",", ":")))
            fh.write("\n")
            n += 1
    os.replace(tmp, path)
    return n


def read_jsonl(path):
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if line:
                yield json.loads(line)


def write_json(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as fh:
        json.dump(obj, fh, indent=2, sort_keys=True)
        fh.write("\n")
    os.replace(tmp, path)


def read_json(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


# ---------------------------------------------------------------------------
# csv tables (registry / overrides) -- kept text so they diff and review well
# ---------------------------------------------------------------------------
def read_csv_rows(path):
    if not os.path.exists(path):
        return []
    with open(path, newline="", encoding="utf-8") as fh:
        return list(csv.DictReader(fh))


def write_csv_rows(path, fieldnames, rows):
    tmp = path + ".tmp"
    with open(tmp, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=fieldnames)
        w.writeheader()
        for r in rows:
            w.writerow(r)
    os.replace(tmp, path)


def parse_rva(text):
    """Accept an RVA or a VA at Dunia's preferred base, with or without '0x'.

    Always hex: every address in this project, in FCSE's sources and in the
    engine-internals docs is written in hex, so treating a bare '5E8CE0' as
    decimal would silently accept a wrong address rather than fail.
    """
    value = text if isinstance(text, int) else int(text.strip(), 16)
    if value >= PREFERRED_BASE:
        value -= PREFERRED_BASE
    return value


def fmt_rva(value):
    return "0x%08X" % value


# ---------------------------------------------------------------------------
# reporting
# ---------------------------------------------------------------------------
class Reporter:
    """Collects a human-readable report while the stage runs."""

    def __init__(self, title):
        self.title = title
        self.lines = []

    def line(self, text=""):
        self.lines.append(text)
        print(text)

    def section(self, text):
        self.line("")
        self.line("=== %s ===" % text)

    def save(self, path):
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(self.title + "\n")
            fh.write("=" * len(self.title) + "\n\n")
            fh.write("\n".join(self.lines))
            fh.write("\n")
