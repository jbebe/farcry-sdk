"""Write the layer's player health: each difficulty's maximum health and bar size, the game's curves
scaled. Easy and Normal take as many AK-47 hits as in the game, 37 and 28 from full at the sickness
level the campaign starts at: their curves grow by as much as the real round outhits the game's AK.
Hardcore keeps the game's curves, where the 3rd hit kills, and on Infamous the 1st does. Sickness still
shrinks each pool as in the game, and a bar stays a fifth of maximum health, as the HUD's five bars need.

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
from weapons import GUNS, LAYER, LIBRARIES, curve_damage, impact_level

# The game's own AK-47 impact level, which a soldier's hit on the player kept in sight.
GAME_AK_LEVEL = 13
# How many times harder the real round hits than the game's AK.
AK_RATIO = curve_damage(impact_level(*GUNS["Primary.AK47"])) / curve_damage(GAME_AK_LEVEL)
# Each difficulty's scale on the game's curves; Hardcore keeps them.
SCALES = {"Casual": AK_RATIO, "Experienced": AK_RATIO, "Infamous": 0.4}
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
