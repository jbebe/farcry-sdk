# Build a bundle for a few models and check nothing else has to be present.
#
#   python bundle.py
#
# Self-containment is the whole claim a bundle makes, so the check walks the
# model's reference graph using the bundle as the only source: every material it
# names, every texture those name, and a decodable payload behind each one.

import os
import sys
import tempfile

from _corpus import GRAPHICS, describe_difference, require

from fc2fmt import xbt
from fc2fmt.assets import InstallAssets, find_root, normalise
from fc2fmt.bundle import OWNED, SHARED, Bundle
from fc2fmt.xbg import XbgFile
from fc2fmt.xbm import XbmMaterial

SAMPLES = (
    os.path.join(GRAPHICS, "weapons", "primary", "ak47", "ak47.xbg"),
    os.path.join(GRAPHICS, "actors", "buddy_andrehyppolite", "andrehyppolite.xbg"),
    os.path.join(GRAPHICS, "vehicles", "land", "buggy", "buggy.xbg"),
)


def fail(message):
    print("FAIL %s" % message)
    return 1


def check_closed(bundle):
    """Resolve the model's whole reference graph against the bundle alone."""
    errors = 0
    for path in bundle.missing:
        errors += fail("%s: nothing supplied %s" % (bundle.model, path))
    model = XbgFile.parse(bundle.read(bundle.model))
    for material_path in model.materials:
        data = bundle.read(material_path)
        if not data:
            errors += fail("%s: no material %s" % (bundle.model, material_path))
            continue
        for slot, texture_path in XbmMaterial.parse(data).textures.items():
            texture = xbt.read(bundle, texture_path)
            if texture is None:
                errors += fail("%s: no texture for %s (%s)"
                               % (bundle.model, slot, texture_path))
            elif not texture.dds:
                errors += fail("%s: empty payload for %s" % (bundle.model, texture_path))
    return errors


def check_roles(bundle):
    """The model and its own directory are editable; shared assets are not."""
    entry = bundle.entries[bundle.model]
    if entry.role != OWNED:
        return fail("%s: the model itself is marked %s" % (bundle.model, entry.role))
    if not any(e.role == SHARED for e in bundle.entries.values()):
        return fail("%s: no shared asset, so the role split is not being applied"
                    % bundle.model)
    return 0


def check_round_trip(bundle, directory):
    """Writing and reading back must preserve every byte and every role."""
    path = os.path.join(directory, os.path.basename(bundle.model) + ".fc2model")
    bundle.write(path)
    reloaded = Bundle.load(path)
    if reloaded.model != bundle.model:
        return fail("%s: reloaded as %s" % (bundle.model, reloaded.model))
    if set(reloaded.entries) != set(bundle.entries):
        difference = set(bundle.entries) ^ set(reloaded.entries)
        return fail("%s: entries differ by %s" % (bundle.model, sorted(difference)[:3]))
    errors = 0
    for name, entry in bundle.entries.items():
        other = reloaded.entries[name]
        if other.data != entry.data:
            errors += fail("%s: %s" % (name, describe_difference(entry.data, other.data)))
        if other.role != entry.role:
            errors += fail("%s: role %s became %s" % (name, entry.role, other.role))
    return errors


def main():
    if not require():
        return 0
    errors = 0
    with tempfile.TemporaryDirectory() as directory:
        for model_path in SAMPLES:
            if not os.path.exists(model_path):
                continue
            root = find_root(model_path)
            game_path = normalise(os.path.relpath(model_path, root))
            bundle = Bundle.build(game_path, InstallAssets(root))
            errors += check_closed(bundle) + check_roles(bundle) + check_round_trip(bundle, directory)
            print("%s: %d files, %d owned, %.1f MB"
                  % (bundle.model, len(bundle.entries), len(bundle.owned()),
                     bundle.size / 1e6))
    print("bundle: %s" % ("FAILED" if errors else "OK"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
