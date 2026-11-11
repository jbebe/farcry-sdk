"""Game copies a session binds with its first /deploy, each cleaned back to stock when it changes hands.

    python scripts/fc2instance.py create                  makes the copies that are missing
    python scripts/fc2instance.py path                    this session's copy; never claims
    python scripts/fc2instance.py claim --topic "<t>" [n] this session's copy, binding one, or taking over copy n
    python scripts/fc2instance.py list
    python scripts/fc2instance.py clean <n>               back to stock; the claim stays

The session is CLAUDE_CODE_SESSION_ID unless --session names it. `claim` exits 3 when every copy is
bound to a session still at work, listing them, so the user picks n.
"""

import argparse
import ctypes
import datetime
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from ctypes import wintypes
from pathlib import Path

MASTER = Path(r"C:\Games\Far Cry 2")
ROOT = Path(r"C:\Games\fc2instances")
COUNT = 3
MANIFEST = "goggame-1207659042.hashdb"
STOCK = "stock.json"
CLAIM = "instance.json"
ALL_BOUND = 3

# Stock bin files GOG's manifest leaves out.
EXTRA_STOCK = ["bin/lang.ini", "bin/FC2Init.ini"]

# The patch comes from JackAll's vanilla backup, under both names, so a build finds its backup.
PATCH = {name: vanilla + ".vanilla" for vanilla in ("Data_Win32/patch.fat", "Data_Win32/patch.dat")
         for name in (vanilla, vanilla + ".vanilla")}

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
kernel32 = ctypes.WinDLL("kernel32")
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]


def game_processes(game):
    """The game's processes started from this install's bin folder, leaving other copies' alone."""
    bin_folder = Path(game).resolve() / "bin"
    pids = (wintypes.DWORD * 4096)()
    size = wintypes.DWORD()
    kernel32.K32EnumProcesses(pids, ctypes.sizeof(pids), ctypes.byref(size))
    path = ctypes.create_unicode_buffer(32768)
    found = []
    for pid in pids[: size.value // ctypes.sizeof(wintypes.DWORD)]:
        handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not handle:
            continue
        length = wintypes.DWORD(len(path))
        if kernel32.QueryFullProcessImageNameW(handle, 0, path, ctypes.byref(length)):
            exe = Path(path.value)
            if exe.parent == bin_folder and exe.name.lower() in ("fcse.exe", "farcry2.exe"):
                found.append(pid)
        kernel32.CloseHandle(handle)
    return found


def manifest_paths():
    """Every file GOG installed for the game, from its manifest; the manifest's hashes are not file MD5s."""
    with zipfile.ZipFile(MASTER / MANIFEST) as archive:
        data = archive.read(archive.namelist()[0])
    paths = {match.decode() for match in re.findall(rb"[0-9a-f]{32}/([^\x00-\x20]+)", data)}
    return sorted(path for path in paths if path.startswith(("bin/", "Data_Win32/")))


def copies():
    return [ROOT / str(n) for n in range(1, COUNT + 1)]


def created():
    return [t for t in copies() if (t / STOCK).exists()]


def instance(n):
    path = ROOT / str(n)
    if not (path / STOCK).exists():
        sys.exit(f"{path} is not a game copy; run create first")
    return path


def create():
    stock = [p for p in manifest_paths() if p not in PATCH] + EXTRA_STOCK
    for target in copies():
        if (target / STOCK).exists():
            print(f"{target}: exists")
            continue
        # Stock files are hard links, since nothing rewrites one; the patch is copied, since JackAll's
        # vanilla restore rewrites it in place.
        entries = [place(target, rel, rel, "link") for rel in stock]
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
    if game_processes(target):
        sys.exit(f"the game is running in {target}; close it first")
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
    print(f"{target}: {removed} added files removed, {restored} stock files restored", file=sys.stderr)


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
    top = Path(top).resolve()
    return None if Path(common).resolve().parent == top else top


def bound(session, worktree):
    """This session's copy: claimed under its id, or under its worktree when a resumed session's id changed."""
    for target in created():
        claim = claim_of(target)
        if claim and (claim["session"] == session or (worktree and claim["worktree"] == str(worktree))):
            return target
    return None


def session_game(start, session=None):
    """The install a tool run from `start` works on: FC2_GAME, else the copy bound to the session
    (this Claude session by default), else None."""
    if os.environ.get("FC2_GAME"):
        return Path(os.environ["FC2_GAME"]).resolve()
    copy = bound(session or os.environ.get("CLAUDE_CODE_SESSION_ID"), worktree_of(start))
    return copy.resolve() if copy else None


def orphaned(claim):
    """A claim whose worktree was squashed into main and removed: its copy is free to take over."""
    return bool(claim and claim["worktree"] and not Path(claim["worktree"]).exists())


def describe(target):
    if not (target / STOCK).exists():
        return f"{target.name}: not created"
    claim = claim_of(target)
    if claim is None:
        return f"{target.name}: free"
    where = claim["worktree"] or "main tree"
    gone = " (worktree gone)" if orphaned(claim) else ""
    return f"{target.name}: {claim['topic']} - session {claim['session']}, {where}{gone}, since {claim['claimed']}"


def claim(session, topic, worktree, n=None):
    """Binds copy n, cleaned; else this session's own copy, a free one, or one whose worktree is gone."""
    record = {"session": session, "topic": topic, "worktree": str(worktree) if worktree else None,
              "claimed": datetime.datetime.now().isoformat(timespec="seconds")}
    if n is None:
        own = bound(session, worktree)
        if own:
            return own
        for target in created():
            try:
                with open(target / CLAIM, "x") as file:
                    json.dump(record, file)
                return target
            except FileExistsError:
                continue
        target = next((t for t in created() if orphaned(claim_of(t))), None)
        if target is None:
            for t in created():
                print(describe(t), file=sys.stderr)
            sys.exit(ALL_BOUND)
    else:
        target = instance(n)
    clean(target)
    (target / CLAIM).write_text(json.dumps(record))
    return target


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["create", "path", "claim", "list", "clean"])
    parser.add_argument("n", nargs="?", type=int)
    parser.add_argument("--session", default=os.environ.get("CLAUDE_CODE_SESSION_ID"),
                        help="the session id, CLAUDE_CODE_SESSION_ID by default")
    parser.add_argument("--topic", help='what the session works on, "<mod>: <what>"')
    options = parser.parse_args()
    if options.command in ("path", "claim") and not options.session:
        parser.error(f"{options.command} needs --session outside Claude Code")
    if options.command == "claim" and not options.topic:
        parser.error("claim needs --topic")
    if options.command == "clean" and options.n is None:
        parser.error("clean needs the copy's number")

    if options.command == "create":
        create()
    elif options.command == "path":
        target = session_game(Path.cwd(), options.session)
        if target is None:
            sys.exit("no game copy is bound to this session; /deploy claims one")
        print(target)
    elif options.command == "claim":
        print(claim(options.session, options.topic, worktree_of(Path.cwd()), options.n))
    elif options.command == "list":
        for target in copies():
            print(describe(target))
    else:
        clean(instance(options.n))


if __name__ == "__main__":
    main()
