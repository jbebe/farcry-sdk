"""Write the layer's weapon archetype fragments: which guns fire tracers, how fast and how long their
streaks are, which scopes the plugin draws and how, those scopes losing the engine's iron-sight
post effect and its random spread down the sights, which guns' breath can be held, what each weapon
weighs, and which guns the game still takes the kick back on.

Each fragment is a WeaponProperties or weapons archetype copied whole out of a decoded entity
library, with only the fields below changed or FCSE's entity data component added, so JackAll
merges it field by field with any other mod's copy and reports a clash on the same field.
Multiplayer (.Multi) archetypes are left alone.

    jackall-cli fcb decode <world1 entitylibrary.fcb> -o world1.xml
    jackall-cli fcb decode <world2 entitylibrary.fcb> -o world2.xml
    jackall-cli fcb decode <dlc1 entitylibrary.fcb> -o dlc1.xml
    python data\\fragments.py world1.xml world2.xml dlc1.xml

The libraries are the campaign's: worlds\\world{1,2}\\generated\\entitylibrary.fcb and
downloadcontent\\dlc1\\generated\\entitylibrary.fcb.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "scripts"))
from entity_fragments import (add_entity_data, fragment_path, has_object, object_lines, prototypes,
                              write)

LAYER = os.path.join(HERE, "..", "layer", "mods")
LIBRARIES = (
    ("worlds", "world1", "generated", "entitylibrary.fcb"),
    ("worlds", "world2", "generated", "entitylibrary.fcb"),
    ("downloadcontent", "dlc1", "generated", "entitylibrary.fcb"),
)

# The guns that keep their tracers, an archetype and its variants each; every other gun's are off.
TRACER_GUNS = ("Special.M249_Saw", "Special.PKM", "Primary.Dragunov", "Primary.AS50",
               "MountedWeapons.M249_Mounted", "MountedWeapons.M2_Mounted")
# A tracer's speed and its streak's longest length, in metres.
TRACER_SPEED = "350"
TRACER_LENGTH = "8"
# The scopes the plugin draws from their eyepiece, an archetype and its variants each, as the entity
# data src\scopes.h's Scope is read from; they lose their iron-sight post effect. A reticle is an
# image under bin\plugins, which layer\plugins\ installs.
RETICLES = "aiming-overhaul\\reticles\\"
SCOPES = {
    "Special.Dart_Rifle": {"Reticle": RETICLES + "hunter.png", "Rim": 0.15, "LensX": -0.00284,
                           "LensY": -0.00148, "Raise": 0.06},
    "Special.M1903": {"Reticle": RETICLES + "hunter.png", "Rim": 0.15, "LensX": -0.00061,
                      "LensY": -0.00025, "Raise": 0.06},
    "Primary.Dragunov": {"Reticle": RETICLES + "pso.png", "Rim": 0.36, "LensX": -0.00254,
                         "LensY": -0.02073, "Raise": 0.1819},
    "Primary.AS50": {"Reticle": RETICLES + "tactical.png", "Rim": 0.36, "LensX": 0.00222,
                     "LensY": -0.00248, "Raise": 0.1058},
    # The AR-16's eyecup is the MGL-140's, seen from further back.
    "Primary.M16": {"Reticle": RETICLES + "holosight.png", "ReticleLit": 1, "Shape": "ar16",
                    "Rim": 0.15, "LensX": -0.00474, "LensY": -0.00087, "Size": 0.8},
    "Primary.MGL140": {"Reticle": RETICLES + "holosight.png", "ReticleLit": 1, "Shape": "mgl140",
                       "Rim": 0.15, "LensX": -0.00275, "LensY": 0.00213, "Raise": 0.073,
                       "Size": 0.8},
}
SCOPE_KEY = "AimingOverhaul.Scope"
# The engine's random spread down the sights, standing and crouched, which a scope loses.
SCOPE_SPREADS = ("BulletSpread_IronSight", "BulletSpreadCrouch_IronSight")
# The guns whose scope sways harder and whose breath can be held to steady it: the magnifying ones,
# not the holographic sights.
HOLD_BREATH = ("Special.Dart_Rifle", "Special.M1903", "Primary.Dragunov", "Primary.AS50")
HOLD_BREATH_KEY = "AimingOverhaul.HoldBreath"
# Each weapon's weight in the hands, loaded, in kilograms, an archetype and its variants each: how
# long it takes to come up after a sprint.
WEIGHTS = {
    "HandToHand": 0.7,
    "Secondary.Makarov": 0.8, "Secondary.SilencedMakarov_6P9": 1.1, "Secondary.Star45": 1.1,
    "Secondary.DesertEagle": 2.0, "Secondary.MAC10": 3.0, "Secondary.Uzi": 3.6,
    "Secondary.M79": 2.9, "Secondary.IED": 0.5,
    "Primary.AK47": 4.3, "Primary.FNFAL": 4.9, "Primary.G3KA4": 4.7, "Primary.M16": 4.0,
    "Primary.MP5": 3.4, "Primary.Ithaca": 3.6, "Primary.SPAS12": 4.4, "Primary.USAS12": 7.0,
    "Primary.MGL140": 6.0, "Primary.Dragunov": 4.7, "Primary.AS50": 14.0,
    "Special.Dart_Rifle": 3.5, "Special.M1903": 4.3, "Special.Flare_Gun": 0.8,
    "Special.M249_Saw": 10.0, "Special.PKM": 9.0, "Special.RPG7": 7.0, "Special.Carl_Gustaf": 10.0,
    "Special.Mortar": 10.0, "Special.LPO50": 8.0,
}
WEIGHT_KEY = "AimingOverhaul.Weight"
# The guns whose kick the game still takes back after a burst: the shotguns and the launchers.
# Every other gun's kick stays in the aim.
RECOIL_RETURNS = ("Primary.Ithaca", "Primary.SPAS12", "Primary.USAS12", "Primary.MGL140",
                  "Secondary.M79", "Special.RPG7", "Special.Carl_Gustaf", "DLC1.Crossbow",
                  "DLC1.SawedOffShotgun", "DLC1.SilencedShotgun")
RECOIL_RETURNS_KEY = "AimingOverhaul.RecoilReturns"

PROPERTIES = "WeaponProperties."
WEAPONS = ("weapons.", "DLC1Weapons.")


def of(name, archetypes):
    """Whether the archetype is one of these or a variant under one."""
    return any(name == a or name.startswith(a + ".") for a in archetypes)


def find_value(block, name, within=None):
    """The line of the one value of this name in the block, inside the object of type `within` if
    given."""
    first, last = 0, len(block)
    if within is not None:
        first, last, _ = object_lines(block, within)
    hits = [i for i in range(first, last) if '<value name="%s" ' % name in block[i]]
    if len(hits) != 1:
        raise SystemExit("%d values named %s" % (len(hits), name))
    return hits[0]


def get_value(block, name, within=None):
    return re.search(r">([^<]*)<", block[find_value(block, name, within)]).group(1)


def set_value(block, name, value, within=None):
    at = find_value(block, name, within)
    block[at] = re.sub(r">[^<]*<", ">%s<" % value, block[at], count=1)


def fires_tracers(block):
    """Whether the gun leaves tracers: it has a tracer texture, and a round in so many is one."""
    return (has_object(block, "BulletTracer")
            and get_value(block, "texTexture", "BulletTracer") != "FFFFFFFF"
            and get_value(block, "iFrequency", "BulletTracer") != "0")


def edit_properties(archetype, block):
    if fires_tracers(block):
        if of(archetype, TRACER_GUNS):
            set_value(block, "fSpeed", TRACER_SPEED, "BulletTracer")
            set_value(block, "fLength", TRACER_LENGTH, "BulletTracer")
        else:
            set_value(block, "iFrequency", "0", "BulletTracer")
    if of(archetype, SCOPES):
        set_value(block, "text_IronsightFX", "")
        set_value(block, "IronsightFX", "FFFFFFFF")
        for spread in SCOPE_SPREADS:
            if has_object(block, spread):
                set_value(block, "fAmplitude", "0", spread)


def lookup(archetype, table):
    """The table's value for the archetype or one it is a variant of, or None."""
    return next((value for a, value in table.items() if of(archetype, (a,))), None)


def edit_weapon(archetype, block):
    data = {SCOPE_KEY + key: value for key, value in (lookup(archetype, SCOPES) or {}).items()}
    if of(archetype, HOLD_BREATH):
        data[HOLD_BREATH_KEY] = 1
    if of(archetype, RECOIL_RETURNS):
        data[RECOIL_RETURNS_KEY] = 1
    weight = lookup(archetype, WEIGHTS)
    if weight is not None:
        data[WEIGHT_KEY] = weight
    if data:
        add_entity_data(block, data)


def main(paths):
    written = 0
    for path, library in zip(paths, LIBRARIES):
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        for name, start, end in prototypes(lines):
            if name.endswith(".Multi"):
                continue
            block = lines[start:end]
            if name.startswith(PROPERTIES):
                edit_properties(name[len(PROPERTIES):], block)
            elif name.startswith(WEAPONS):
                edit_weapon(name.split(".", 1)[1], block)
            if block == lines[start:end]:
                continue
            out = fragment_path(LAYER, library, name)
            write(out, block)
            written += 1
            print(os.path.relpath(out, LAYER))
    print("%d fragments" % written)


if __name__ == "__main__":
    main(sys.argv[1:4])
