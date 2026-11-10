"""Keeps a turn going while JackAll's test fixtures hold a file their manifest.tsv does not list.

Registered on Stop. Names only, no hashing: a listed file with changed bytes is the test suite's to
catch (FixtureManifestTests). Which files count is FixtureManifest.Files in JackAll.Tools.
"""
import json
import os
import sys

FIXTURES = os.path.join("tools", "JackAll", "src", "JackAll.Tests", "Fixtures")
MANIFEST = "manifest.tsv"


def repository_root(start):
    """The checkout holding start: the nearest folder with a .git, which in a worktree is a file."""
    folder = os.path.abspath(start)
    while True:
        if os.path.exists(os.path.join(folder, ".git")):
            return folder
        parent = os.path.dirname(folder)
        if parent == folder:
            return None
        folder = parent


def unlisted(fixtures):
    with open(os.path.join(fixtures, MANIFEST), encoding="utf-8") as manifest:
        listed = {line.split("\t", 1)[0] for line in manifest.read().splitlines() if line and not line.startswith("#")}
    for folder, _, files in os.walk(fixtures):
        for name in files:
            fixture = os.path.relpath(os.path.join(folder, name), fixtures).replace(os.sep, "/")
            if fixture not in (MANIFEST, ".gitignore") and fixture not in listed:
                yield fixture


def main():
    try:
        payload = json.loads(sys.stdin.read().lstrip("﻿"))
    except ValueError:
        return 0
    if payload.get("stop_hook_active"):
        return 0

    root = repository_root(payload.get("cwd") or os.getcwd()) or os.environ.get("CLAUDE_PROJECT_DIR")
    if not root:
        return 0
    fixtures = os.path.join(root, FIXTURES)
    if not os.path.isfile(os.path.join(fixtures, MANIFEST)):
        return 0

    missing = sorted(unlisted(fixtures))
    if not missing:
        return 0

    shown = ", ".join(missing[:10]) + (f" and {len(missing) - 10} more" if len(missing) > 10 else "")
    print(
        f"JackAll fixtures missing from {MANIFEST}: {shown}. Write their lines with jackall-cli fixtures "
        f"record, as tools\\JackAll\\README.md's Building section says.",
        file=sys.stderr,
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
