# Turn a game-relative asset path into bytes.
#
# Materials and textures name themselves `graphics\...`, relative to the root of
# whichever archive they were extracted from. Everything that follows such a
# reference goes through a source, so the same code reads from an extracted
# install or from a self-contained bundle without knowing which.
#
# A source answers `read(game_path)` with bytes, or None when it holds no such
# file, and `paths()` with every game path it holds.

import os

INDEXED_SUFFIXES = (".xbg", ".xbm", ".xbt", ".skeleton")
GRAPHICS = "graphics"


def normalise(path):
    return path.replace("\\", "/").lower().lstrip("./")


def find_root(model_path):
    """The archive root a model was extracted to, or None.

    Anchored on the model's own nearest `graphics` ancestor: matching the
    directory name anywhere in the tree finds `worlds/worlds` instead.
    """
    directory = os.path.dirname(os.path.abspath(model_path))
    while True:
        parent = os.path.dirname(directory)
        if parent == directory:
            return None
        if os.path.basename(directory).lower() == GRAPHICS:
            return parent
        directory = parent


class InstallAssets:
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

    def read(self, game_path):
        path = self.entries.get(normalise(game_path))
        return open(path, "rb").read() if path else None

    def paths(self):
        return self.entries.keys()

    def __len__(self):
        return len(self.entries)


def find_named(source, name, suffix=".xbg"):
    """Game paths in a source whose file name is `name` with this suffix.

    A `.mab` names an attached prop the way its file is named rather than by
    path, so the path has to be recovered from the name.
    """
    wanted = "/%s%s" % (name.lower(), suffix)
    return sorted(path for path in source.paths() if path.endswith(wanted))


_cache = {}


def install_assets(root, extra_roots=()):
    """One index per root, since walking an install takes a moment."""
    key = (os.path.abspath(root), tuple(extra_roots))
    if key not in _cache:
        _cache[key] = InstallAssets(*key)
    return _cache[key]
