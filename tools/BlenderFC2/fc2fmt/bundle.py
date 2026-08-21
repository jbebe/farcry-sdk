# Read and write `.fc2model`, one model plus every file it needs.
#
# An `.xbg` on its own is not openable: it names its materials by game-relative
# path, each `.xbm` names its textures the same way, and those live in shared
# trees far from the model. A bundle is a zip carrying all of them under their
# game paths, so an editor never has to reach into a game install.
#
# Every entry carries a role. `owned` files sit in the model's own directory and
# exist for this model alone; `shared` files back many other models, so editing
# one through this bundle would change all of them.

import json
import posixpath
import zipfile
from dataclasses import dataclass

from . import xbt
from .assets import normalise
from .xbg import XbgFile
from .xbm import XbmMaterial, inline_materials

MANIFEST = "manifest.json"
FORMAT = "fc2model"
VERSION = 1
EXTENSION = ".fc2model"
SKELETON_SUFFIX = "_ref.skeleton"

OWNED = "owned"
SHARED = "shared"


@dataclass
class Entry:
    path: str
    role: str
    data: bytes


class Bundle:
    """Every file one model needs, keyed by its game-relative path."""

    def __init__(self, model=""):
        self.model = normalise(model)
        self.entries = {}
        self.missing = []

    def read(self, game_path):
        entry = self.entries.get(normalise(game_path))
        return entry.data if entry else None

    def owned(self):
        return [e for e in self.sorted_entries() if e.role == OWNED]

    def sorted_entries(self):
        return sorted(self.entries.values(), key=lambda entry: entry.path)

    @property
    def size(self):
        return sum(len(entry.data) for entry in self.entries.values())

    @classmethod
    def build(cls, model_path, source):
        """Collect a model and everything it references out of `source`."""
        self = cls(model_path)
        data = self._pull(source, self.model)
        if data is None:
            raise ValueError("no model at %s" % self.model)
        model = XbgFile.parse(data)
        inline = inline_materials(model)
        for material_path in model.materials:
            # An embedded material travels inside the .xbg, so only the rest
            # names a file to fetch.
            definition = inline.get(normalise(material_path))
            if definition is None:
                material = self._pull(source, material_path)
                if material is None:
                    continue
                definition = XbmMaterial.parse(material)
            for texture_path in definition.textures.values():
                self._pull(source, texture_path)
                # Half of all textures keep their top mip in a sibling, and a
                # bundle without it renders at half resolution on each axis.
                self._pull(source, xbt.companion(texture_path), optional=True)
        self._pull(source, self.model[:-4] + SKELETON_SUFFIX, optional=True)
        return self

    @classmethod
    def load(cls, path):
        with zipfile.ZipFile(path) as archive:
            manifest = json.loads(archive.read(MANIFEST))
            if manifest.get("format") != FORMAT:
                raise ValueError("not an %s bundle" % FORMAT)
            if manifest.get("version", 0) > VERSION:
                raise ValueError("bundle is version %d, this reads up to %d"
                                 % (manifest["version"], VERSION))
            self = cls(manifest["model"])
            for record in manifest["entries"]:
                name = normalise(record["path"])
                self.entries[name] = Entry(name, record["role"],
                                           archive.read(record["path"]))
        return self

    def write(self, path):
        """Save as a zip whose manifest names the model and every entry's role."""
        entries = self.sorted_entries()
        manifest = {
            "format": FORMAT,
            "version": VERSION,
            "model": self.model,
            "entries": [{"path": e.path, "role": e.role} for e in entries],
        }
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(MANIFEST, json.dumps(manifest, indent=2))
            for entry in entries:
                archive.writestr(entry.path, entry.data)

    def _pull(self, source, game_path, optional=False):
        """Copy one file out of `source`, noting it as missing when absent."""
        if game_path is None:
            return None
        path = normalise(game_path)
        if path in self.entries:
            return self.entries[path].data
        data = source.read(game_path)
        if data is None:
            if not optional:
                self.missing.append(path)
            return None
        self.entries[path] = Entry(path, self._role(path), data)
        return data

    def _role(self, path):
        return OWNED if posixpath.dirname(path) == posixpath.dirname(self.model) else SHARED
