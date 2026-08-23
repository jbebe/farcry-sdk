# Shared corpus location and helpers for the test scripts.
#
# Importing this also puts the package root on sys.path, so each script starts
# with `from _corpus import ...` and nothing else.

import hashlib
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PACKAGE_ROOT = os.path.normpath(os.path.join(HERE, ".."))
sys.path.insert(0, PACKAGE_ROOT)


def _drop_installed_copies():
    """Forget any of our modules Blender already loaded from somewhere else.

    The add-on puts its own directory on sys.path, so once the extension is
    installed, a test run inside Blender imports that frozen copy instead of the
    files being edited - and passes.
    """
    for name, module in list(sys.modules.items()):
        if name.split(".")[0] != "addon":
            continue
        path = getattr(module, "__file__", None)
        if path and not os.path.abspath(path).startswith(PACKAGE_ROOT):
            del sys.modules[name]


_drop_installed_copies()

CORPUS = os.path.normpath(os.path.join(HERE, "..", "..", "..", "tmp", "gamefiles"))
GRAPHICS = os.path.join(CORPUS, "worlds", "worlds", "graphics")
PELVIS_REF = os.path.join(GRAPHICS, "characters", "_common", "pelvis_ref.skeleton")


def present():
    return os.path.isdir(CORPUS)


def require():
    """Print the skip line and return False when the retail export is absent."""
    if present():
        return True
    print("corpus %s not present, skipping" % CORPUS)
    return False


def find(suffix, root=None):
    for base, _dirs, names in os.walk(root or CORPUS):
        for name in names:
            if name.lower().endswith(suffix):
                yield os.path.join(base, name)


def first_difference(a, b):
    if a == b:
        return None
    limit = min(len(a), len(b))
    # Compare in blocks first so a total mismatch does not crawl byte by byte.
    block = 4096
    for start in range(0, limit, block):
        if a[start:start + block] != b[start:start + block]:
            return next(i for i in range(start, min(start + block, limit)) if a[i] != b[i])
    return limit


def describe_difference(a, b):
    return "differs at byte %s (%d vs %d bytes)" % (first_difference(a, b), len(a), len(b))


# Where a pack comes from for a test: JackAll builds one, because the pack is a
# contract between two codebases and a fixture written by hand would only ever
# test this side's idea of it. Proprietary game content is never committed, so
# there is nothing to check in either.
GAME = os.environ.get("FC2_GAME", r"C:\Games\Far Cry 2")
CLI = os.path.normpath(os.path.join(
    HERE, "..", "..", "JackAll", "src", "JackAll.Cli", "bin", "Debug", "net10.0", "jackall-cli.exe"))

_packs = {}


def have_jackall():
    return os.path.exists(CLI) and os.path.isdir(GAME)


def require_pack():
    """Print the skip line and return False when no pack can be built."""
    if have_jackall():
        return True
    print("no JackAll build at %s or no install at %s, skipping" % (CLI, GAME))
    return False


def pack(model_path, clips=(), rig=None):
    """Build a pack for one game path, once per run.

    Written under the system temp directory rather than the repo, since it holds
    game content.
    """
    key = (model_path, tuple(clips), rig)
    if key in _packs:
        return _packs[key]

    directory = os.path.join(tempfile.gettempdir(), "fc2packs")
    os.makedirs(directory, exist_ok=True)
    # The clips and rig are part of the name: a pack built for one bank is not the
    # pack built for another, and reusing the file would silently test the first
    # one twice.
    stamp = hashlib.sha256(repr(key).encode()).hexdigest()[:8]
    out = os.path.join(directory, "%s-%s.fc2model" % (
        os.path.splitext(os.path.basename(model_path))[0], stamp))
    if not os.path.exists(out):
        command = [CLI, "fc2model", "export", model_path, "--game", GAME, "-o", out]
        for clip in clips:
            command += ["--clip", clip]
        if rig:
            command += ["--rig", rig]
        result = subprocess.run(command, capture_output=True, text=True)
        if result.returncode != 0 or not os.path.exists(out):
            raise RuntimeError("jackall could not pack %s:\n%s\n%s"
                               % (model_path, result.stdout, result.stderr))
    _packs[key] = out
    return out
