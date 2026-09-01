# Read and write .fc2model, the decoded pack JackAll hands an editor.
#
# Nothing here decodes a Dunia format, because nothing in a pack is one: the
# mesh is JSON with flat float arrays, materials are JSON, textures are PNG,
# the rig and its clips are JSON. That is the whole point of the format — one
# codebase owns the byte layouts, and this one owns what a scene looks like.
#
# See docs/docs/file-formats/fc2model.md.

import hashlib
import json
import posixpath
import zipfile

MANIFEST = "manifest.json"
FORMAT = "fc2model"
EXTENSION = ".fc2model"

# The highest manifest this understands. A pack declares the lowest reader that
# can make sense of it, so a later additive change stays readable here.
READS_VERSION = 2

OWNED = "owned"
SHARED = "shared"

MESH = "mesh"
RIG = "rig"
MATERIAL = "material"
TEXTURE = "texture"
CLIP = "clip"


def same_path(a, b):
    """Whether two game paths name one file.

    A path arrives however the referencing file spelled it — a mesh names its
    materials `GRAPHICS\\_MATERIALS\\…` — so it is compared, never rewritten.
    """
    return _key(a) == _key(b)


def _key(path):
    return (path or "").replace("\\", "/").lower()


def stem(path):
    """A game path's file name without its extension."""
    return posixpath.splitext(posixpath.basename(_key(path)))[0]


def read_manifest(path):
    """What a pack carries, without unpacking any of it."""
    with zipfile.ZipFile(path) as archive:
        return _manifest(archive, path)


def _manifest(archive, path):
    manifest = json.loads(archive.read(MANIFEST))
    if manifest.get("format") != FORMAT:
        raise ValueError("%s is not a %s pack" % (path, FORMAT))
    required = manifest.get("requires_reader", manifest.get("version", 1))
    if required > READS_VERSION:
        raise ValueError("%s needs a reader for version %d; this one reads up to %d"
                         % (path, required, READS_VERSION))
    return manifest


class Entry:
    """One file in the pack: what it is in the game, and where it sits in the zip."""

    def __init__(self, record):
        self.record = record
        self.path = record["path"]
        self.file = record["file"]
        self.kind = record["kind"]
        self.role = record.get("role", SHARED)
        self.usage = record.get("usage")

    @property
    def modified(self):
        """An entry is changed exactly when it has grown an origin hash."""
        return "origin_sha256" in self.record

    @property
    def owned(self):
        return self.role == OWNED

    def __repr__(self):
        return "<Entry %s %s>" % (self.kind, self.path)


class Pack:
    """A model and everything it needs, decoded."""

    def __init__(self, manifest, files):
        self.manifest = manifest
        self.files = files
        self.entries = [Entry(record) for record in manifest.get("entries", [])]

    @classmethod
    def load(cls, path):
        with zipfile.ZipFile(path) as archive:
            manifest = _manifest(archive, path)
            files = {name: archive.read(name) for name in archive.namelist()
                     if name != MANIFEST}
        return cls(manifest, files)

    @property
    def model(self):
        return self.manifest["model"]

    @property
    def limits(self):
        """The ceilings the pack declares, so a validator hardcodes none of them."""
        return self.manifest.get("limits", {})

    @property
    def clips(self):
        """The bank index: enough to list what is carried without parsing any of it."""
        return self.manifest.get("clips", [])

    def entry(self, game_path):
        for entry in self.entries:
            if same_path(entry.path, game_path):
                return entry
        return None

    def of_kind(self, kind):
        return [entry for entry in self.entries if entry.kind == kind]

    def content(self, entry):
        return self.files[entry.file]

    def document(self, entry):
        return json.loads(self.content(entry))

    def read(self, game_path):
        """The bytes behind a game path, whatever kind of file it is."""
        entry = self.entry(game_path)
        return self.content(entry) if entry else None

    def mesh(self):
        return self.document(self._only(MESH))

    def rig(self):
        entries = self.of_kind(RIG)
        return self.document(entries[0]) if entries else None

    def material(self, game_path):
        """One material's document, or None when the pack does not carry it."""
        entry = self.entry(game_path)
        return self.document(entry) if entry and entry.kind == MATERIAL else None

    def texture(self, game_path):
        """One texture as PNG bytes, at full resolution with mip0 already merged."""
        entry = self.entry(game_path)
        return self.content(entry) if entry and entry.kind == TEXTURE else None

    def clip(self, game_path):
        entry = self.entry(game_path)
        return self.document(entry) if entry and entry.kind == CLIP else None

    def replace(self, game_path, content):
        """Put edited bytes behind an entry, marking what it arrived as.

        The origin hash is what says an entry changed, so applying a pack writes
        only what an editor touched — a texture travels as PNG, and re-encoding
        an untouched one would compress it again on every save.
        """
        entry = self.entry(game_path)
        if entry is None:
            raise KeyError("this pack carries no %s" % game_path)
        if entry.role == SHARED:
            raise ValueError(
                "%s is shared with other models; editing it would change every one"
                % entry.path)
        entry.record.setdefault("origin_sha256", entry.record["sha256"])
        entry.record["sha256"] = hashlib.sha256(content).hexdigest()
        self.files[entry.file] = content
        return entry

    def replace_clip(self, game_path, bank):
        """Put an edited animation bank back, whatever its role says.

        A bank is `shared` by the ownership rule and that rule is too blunt for
        one: the AK-47's reload counts three users, two of them unnamed
        resources that load it rather than models that use it. What actually
        matters is which clip inside it changed.

        This is safe because of what it can change, not because of a permission:
        an untouched clip carries its sections and masks verbatim, so it goes
        back byte for byte, and the writer only ever rewrites the clip that fits
        this pack's own rig. The character's arms are the same file they were.
        """
        entry = self.entry(game_path)
        if entry is None:
            raise KeyError("this pack carries no %s" % game_path)
        if entry.kind != CLIP:
            raise ValueError("%s is a %s, not an animation bank" % (game_path, entry.kind))

        content = dumps(bank)
        entry.record.setdefault("origin_sha256", entry.record["sha256"])
        entry.record["sha256"] = hashlib.sha256(content).hexdigest()
        self.files[entry.file] = content
        return entry

    def replace_document(self, game_path, document):
        return self.replace(game_path, dumps(document))

    def save(self, path):
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(MANIFEST, json.dumps(self.manifest, indent=2))
            for name in sorted(self.files):
                archive.writestr(name, self.files[name])

    def _only(self, kind):
        entries = self.of_kind(kind)
        if not entries:
            raise ValueError("this pack carries no %s" % kind)
        return entries[0]


def dumps(document):
    """A document the way JackAll writes one: compact, and no stray floats.

    `separators` matters more than it looks — the default puts a space after
    every comma, which on a mesh's flat float arrays is megabytes of nothing.
    """
    return json.dumps(document, separators=(",", ":")).encode("utf-8")
