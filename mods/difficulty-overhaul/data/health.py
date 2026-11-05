"""Write the layer's player health: each difficulty's maximum health and bar size, the game's curves
scaled so that from full health at the sickness level the campaign starts at, AK-47 hits kill on the
8th on Easy, the 5th on Normal, the 3rd on Hardcore and the 1st on Infamous. Sickness still shrinks
each pool as in the game, and a bar stays a fifth of maximum health, as the HUD's five bars need.

    python data\\health.py world1.xml world2.xml dlc1.xml tmpla.xml

The decoded libraries are data\\weapons.py's, and tmpla's curves are written for DevTools' test range as
its guns are. dlc1's library holds no curves.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "scripts"))
from entity_fragments import fragment_path, prototypes, write
from weapons import LAYER, LIBRARIES

# Each difficulty's scale on the game's curves; Hardcore keeps them.
SCALES = {"Casual": 1.32, "Experienced": 1.11, "Infamous": 0.4}
CURVES = ("HealthMax", "HealthBarSize")
KNOT_VALUE = re.compile(r"<y>([^<]*)</y>")


def scale_knots(block, scale):
    """Scales each knot's value, the y two lines into its Value, leaving its Info alone."""
    for i, line in enumerate(block):
        if line.strip() == '<value name="Value" type="Vector4">':
            block[i + 2] = KNOT_VALUE.sub(lambda m: "<y>%g</y>" % round(float(m.group(1)) * scale, 1),
                                          block[i + 2])


def main(paths):
    names = {"Curves.PlayerSicknessCurves.%s_%s" % (curve, difficulty): scale
             for curve in CURVES for difficulty, scale in SCALES.items()}
    for path, library in zip(paths, LIBRARIES):
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        for name, start, end in prototypes(lines):
            if name in names:
                block = lines[start:end]
                scale_knots(block, names[name])
                out = fragment_path(LAYER, library, name)
                write(out, block)
                print(os.path.relpath(out, LAYER))


if __name__ == "__main__":
    main(sys.argv[1:])
