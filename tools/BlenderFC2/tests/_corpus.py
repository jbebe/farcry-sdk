# Shared corpus location and helpers for the test scripts.
#
# Importing this also puts the package root on sys.path, so each script starts
# with `from _corpus import ...` and nothing else.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))

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
