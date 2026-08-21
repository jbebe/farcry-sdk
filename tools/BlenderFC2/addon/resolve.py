# Turn a game-relative asset path into a file on disk.
#
# Materials and textures name themselves `graphics\...`, relative to the root of
# whichever archive they were extracted from. That root is found from the model
# being imported: its nearest `graphics` ancestor is the same tree, so the
# directory holding it is the root to index.

import os

INDEXED_SUFFIXES = (".xbm", ".xbt")
GRAPHICS = "graphics"


def normalise(path):
    return path.replace("\\", "/").lower().lstrip("./")


def find_root(model_path):
    """The archive root a model was extracted to, or None."""
    directory = os.path.dirname(os.path.abspath(model_path))
    while True:
        parent = os.path.dirname(directory)
        if parent == directory:
            return None
        if os.path.basename(directory).lower() == GRAPHICS:
            return parent
        directory = parent


class GameFiles:
    """An index of one extracted archive, keyed by game-relative path."""

    def __init__(self, root, extra_roots=()):
        self.root = root
        self.entries = {}
        for base in (root,) + tuple(extra_roots):
            self._index(base)

    def _index(self, base):
        if not os.path.isdir(base):
            return
        for directory, _dirs, files in os.walk(base):
            relative = normalise(os.path.relpath(directory, base))
            for filename in files:
                if filename.lower().endswith(INDEXED_SUFFIXES):
                    # A later root overrides an earlier one, which is how a
                    # patched asset should win.
                    self.entries["%s/%s" % (relative, filename.lower())] = \
                        os.path.join(directory, filename)

    def find(self, game_path):
        return self.entries.get(normalise(game_path))

    def __len__(self):
        return len(self.entries)


_cache = {}


def game_files(root, extra_roots=()):
    """One index per root, since walking an install takes a moment."""
    key = (os.path.abspath(root), tuple(extra_roots))
    if key not in _cache:
        _cache[key] = GameFiles(*key)
    return _cache[key]
