"""Write the layer's HUD fragments: the red marks pointing to whoever hit the player and the red flash
around the screen's edges are hidden, in every aspect and language of hud.mgb.

    python data\\hud.py <the common archive's ui\\localized folder>

The fragments are the area the page draws each mark from, with its one image hidden, and the page's
one placing of the red flash, hidden, written to layer\\mods\\ui\\localized\\.
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
HIDDEN = {
    # The direction mark's image, hit_indicator_sliced, in area #0E2B6A57.
    "direction marks": re.compile(
        r'(<Element [^>]*HIDDEN=")false("[^>]*>\s*<USERDATA name="#D165391C">)'),
    # The placing of area #268C4407, the red edges and veins a hit flashes.
    "red flashes": re.compile(
        r'(<Element [^>]*type="AreaInstance" HIDDEN=")false("[^>]*>\s*<USERDATA name="#0CF83837">)'),
}


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
                text = handle.read()
            for what, element in HIDDEN.items():
                text, hidden = element.subn(r"\g<1>true\g<2>", text)
                if hidden != 1:
                    raise SystemExit("%s: %d %s" % (retail, hidden, what))
            with open(xml, "w", encoding="utf-8") as handle:
                handle.write(text)
            shutil.rmtree(out, ignore_errors=True)
            run("mgb", "fragments", xml, "--base", retail, "--out", out)
        print(os.path.relpath(out, LAYER))


if __name__ == "__main__":
    main(sys.argv[1])
