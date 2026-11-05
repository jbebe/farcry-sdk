"""Write the layer's HUD fragments: the red marks pointing to whoever hit the player are hidden, in
every aspect and language of hud.mgb, leaving the game's red hurt overlay.

    python data\\hud.py <the common archive's ui\\localized folder>

The fragment is the area the page draws each mark from, with its one image hidden, written to
layer\\mods\\ui\\localized\\.
"""
import os
import re
import shutil
import sys
import tempfile

from breath import HERE, run

LAYER = os.path.join(HERE, "..", "layer", "mods", "ui", "localized")

VARIANTS = [(aspect, lang) for aspect in ("pc", "pcwidescreen")
            for lang in ("eng", "fre", "ger", "ita", "spa")]
# The direction mark's image, hit_indicator_sliced, in area #0E2B6A57.
MARK = re.compile(r'(<Element [^>]*HIDDEN=")false("[^>]*>\s*<USERDATA name="#D165391C">)')


def main(localized):
    for aspect, lang in VARIANTS:
        retail = os.path.join(localized, aspect, lang, "ui", "hud.mgb")
        out = os.path.join(LAYER, aspect, lang, "ui", "hud.mgb")
        with tempfile.TemporaryDirectory() as work:
            package = os.path.join(work, "hud.mgb")
            shutil.copy(retail, package)
            run("mgb", "decode", package)
            xml = os.path.join(work, "hud.xml")
            with open(xml, encoding="utf-8") as handle:
                text, hidden = MARK.subn(r"\g<1>true\g<2>", handle.read())
            if hidden != 1:
                raise SystemExit("%s: %d direction marks" % (retail, hidden))
            with open(xml, "w", encoding="utf-8") as handle:
                handle.write(text)
            shutil.rmtree(out, ignore_errors=True)
            run("mgb", "fragments", xml, "--base", retail, "--out", out)
        print(os.path.relpath(out, LAYER))


if __name__ == "__main__":
    main(sys.argv[1])
