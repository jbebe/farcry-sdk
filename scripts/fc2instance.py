"""Game copies for sessions working in their own worktrees: one copy per worktree, cleaned back to stock.

    python scripts/fc2instance.py create        makes the copies that are missing
    python scripts/fc2instance.py path          the game for this worktree, claiming a free copy if needed
    python scripts/fc2instance.py list
    python scripts/fc2instance.py clean <n>     back to stock: the patch from vanilla, nothing added
    python scripts/fc2instance.py release <n>   clean, then free the copy for another worktree
"""

import argparse
import datetime
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

MASTER = Path(r"C:\Games\Far Cry 2")
ROOT = Path(r"C:\Games\fc2instances")
COUNT = 3
MANIFEST = "goggame-1207659042.hashdb"
STOCK = "stock.json"
CLAIM = "instance.json"

# Stock bin files GOG's manifest leaves out.
EXTRA_STOCK = ["bin/lang.ini", "bin/FC2Init.ini"]

# The patch comes from JackAll's vanilla backup, under both names, so a build finds its backup.
PATCH = {name: vanilla + ".vanilla" for vanilla in ("Data_Win32/patch.fat", "Data_Win32/patch.dat")
         for name in (vanilla, vanilla + ".vanilla")}


def manifest_paths():
    """Every file GOG installed for the game, from its manifest; the manifest's hashes are not file MD5s."""
    with zipfile.ZipFile(MASTER / MANIFEST) as archive:
        data = archive.read(archive.namelist()[0])
    paths = {match.decode() for match in re.findall(rb"[0-9a-f]{32}/([^\x00-\x20]+)", data)}
    return sorted(path for path in paths if path.startswith(("bin/", "Data_Win32/")))


def created():
    return [t for t in (ROOT / str(n) for n in range(1, COUNT + 1)) if (t / STOCK).exists()]


def instance(n):
    path = ROOT / str(n)
    if path not in created():
        sys.exit(f"{path} is not a game copy; run create first")
    return path


def create():
    stock = [p for p in manifest_paths() if p not in PATCH] + EXTRA_STOCK
    for n in range(1, COUNT + 1):
        target = ROOT / str(n)
        if target in created():
            print(f"{target}: exists")
            continue
        # bin\ is copied, since tools overwrite its files in place; the archives are hard links.
        entries = [place(target, rel, rel, "copy" if rel.startswith("bin/") else "link") for rel in stock]
        entries += [place(target, rel, source, "copy") for rel, source in PATCH.items()]
        (target / STOCK).write_text(json.dumps(entries, indent=1))
        print(f"{target}: {len(entries)} files")


def place(target, rel, source, kind):
    """Puts the master's `source` at `rel` in the copy, and returns its stock.json entry."""
    master, path = MASTER / source, target / rel
    if not master.exists():
        sys.exit(f"{master} is missing from the GOG install")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.unlink(missing_ok=True)
    if kind == "link":
        os.link(master, path)
    else:
        shutil.copy2(master, path)
    stat = path.stat()
    return {"path": rel, "source": source, "kind": kind, "size": stat.st_size, "mtime": stat.st_mtime_ns}


def clean(target):
    entries = json.loads((target / STOCK).read_text())
    keep = {Path(e["path"]) for e in entries} | {Path(STOCK), Path(CLAIM)}
    removed = restored = 0
    for folder, _, files in os.walk(target, topdown=False):
        for name in files:
            path = Path(folder) / name
            if path.relative_to(target) not in keep:
                path.unlink()
                removed += 1
        if Path(folder) != target and not os.listdir(folder):
            os.rmdir(folder)
    for entry in entries:
        path = target / entry["path"]
        stat = path.stat() if path.exists() else None
        if stat is None or (stat.st_size, stat.st_mtime_ns) != (entry["size"], entry["mtime"]):
            place(target, entry["path"], entry["source"], entry["kind"])
            restored += 1
    print(f"{target}: {removed} added files removed, {restored} stock files restored")


def claim_of(target):
    try:
        return json.loads((target / CLAIM).read_text())
    except FileNotFoundError:
        return None


def worktree_of(start):
    """The worktree holding `start`, or None in the main working tree."""
    top, common = subprocess.run(
        ["git", "-C", str(start), "rev-parse", "--path-format=absolute", "--show-toplevel", "--git-common-dir"],
        capture_output=True, text=True, check=True).stdout.split("\n")[:2]
    return None if Path(common).resolve().parent == Path(top).resolve() else Path(top).resolve()


def game_for(start):
    """The game this worktree tests in: its claimed copy, claiming a free one, or the GOG install itself."""
    worktree = worktree_of(start)
    if worktree is None:
        return MASTER
    copies = created()
    for target in copies:
        claim = claim_of(target)
        if claim and Path(claim["worktree"]) == worktree:
            return target
    for target in copies:
        try:
            with open(target / CLAIM, "x") as file:
                json.dump({"worktree": str(worktree), "claimed": datetime.datetime.now().isoformat(timespec="seconds")}, file)
            return target
        except FileExistsError:
            continue
    sys.exit("every game copy is claimed; release one first")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["create", "path", "list", "clean", "release"])
    parser.add_argument("n", nargs="?", type=int)
    options = parser.parse_args()
    if options.command in ("clean", "release") and options.n is None:
        parser.error(f"{options.command} needs the copy's number")

    if options.command == "create":
        create()
    elif options.command == "path":
        print(game_for(Path.cwd()))
    elif options.command == "list":
        copies = created()
        for target in (ROOT / str(n) for n in range(1, COUNT + 1)):
            claim = claim_of(target)
            if target not in copies:
                print(f"{target.name}: not created")
            elif claim is None:
                print(f"{target.name}: free")
            else:
                gone = "" if Path(claim["worktree"]).exists() else " (worktree gone)"
                print(f"{target.name}: {claim['worktree']} since {claim['claimed']}{gone}")
    else:
        target = instance(options.n)
        clean(target)
        if options.command == "release":
            (target / CLAIM).unlink(missing_ok=True)
            print(f"{target}: free")


if __name__ == "__main__":
    main()
