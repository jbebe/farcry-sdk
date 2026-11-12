"""Write each gun's own muzzle flash: a first-person and a third-person particle system per gun, copied
from the stock flash nearest its muzzle device and sized to its round and barrel, with the depload
entries that load them. data\\fragments.py points the guns' weapon properties at them.

- A flash emitter, one drawing the flash texture, has its size and placement scaled by the gun's size
  and its HDRMul by its brightness; smoke and heat stay as they are.
- Every flash keeps the length of the flash the gun has in the game: a copy from another gun's flash
  takes the gun's own life, emission time and frame rate.
- A gun that keeps its stock flash, and the MP5SD, the silenced Makarov, the Dart Rifle, the MGL-140
  and the silenced shotgun, are left alone.

    jackall-cli rml decode <worlds\\world1\\generated\\world1_deploadnewparticles.rml> -o world1.xml
    jackall-cli rml decode <worlds\\world2\\generated\\world2_deploadnewparticles.rml> -o world2.xml
    jackall-cli rml decode <worlds\\tmpla\\generated\\tmpla_deploadnewparticles.rml> -o tmpla.xml
    jackall-cli rml decode <downloadcontent\\dlc1\\generated\\entitylibrary_deploadnewparticles.rml> -o dlc1.xml
    jackall-cli depload decode <worlds\\world1\\generated\\world1_depload.dat> -o world1_depload.xml
    jackall-cli depload decode <worlds\\world2\\generated\\world2_depload.dat> -o world2_depload.xml
    jackall-cli depload decode <worlds\\tmpla\\generated\\tmpla_depload.dat> -o tmpla_depload.xml
    jackall-cli depload decode <downloadcontent\\dlc1\\generated\\entitylibrary_depload.dat> -o dlc1_depload.xml
    python data\\flashes.py world1.xml world2.xml tmpla.xml dlc1.xml world1_depload.xml world2_depload.xml
        tmpla_depload.xml dlc1_depload.xml

The particle libraries and the worlds' deploads are worlds.fat's, tmpla's depload patch.dat's, and the
DLC's files dlc1's entitylibrary.fat's. tmpla's are written as the test range's, which DevTools builds
from them.
"""
import copy
import os
import sys
import uuid
import xml.etree.ElementTree as ET
from collections import namedtuple

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "scripts"))
from entity_fragments import numbered, path_id, write

LAYER = os.path.join(HERE, "..", "layer", "mods")
# Each library's folder and file stem; a base-game gun's flashes go in the three worlds', a DLC gun's
# in the DLC's, which every world loads.
LIBRARIES = {
    "world1": (("worlds", "world1", "generated"), "world1"),
    "world2": (("worlds", "world2", "generated"), "world2"),
    "testrange": (("worlds", "testrange", "generated"), "testrange"),
    "dlc1": (("downloadcontent", "dlc1", "generated"), "entitylibrary"),
}
WORLDS = ("world1", "world2", "testrange")

PREFIX = "aimingoverhaul.flashes."
FLASH_TEXTURE = "muzzle_flash"
# The fields of a flash emitter that make its length, which a copy takes from the gun's own flash.
TIMING_ATTRIBUTES = ("EmitDuration", "FramePerSecond")
TIMING_CURVES = ("InitLifeTimeKF",)


def pair(stock):
    """A stock flash's first- and third-person systems."""
    return "weapons.weapons.pl_muzzleflash_" + stock, "weapons.weapons.muzzleflash_" + stock


# One gun's flash: the stock systems copied for first and third person (None keeps the gun's own), its
# size against the copied flash and its brightness, and the gun's stock systems whose length it keeps
# where they are not the copied ones.
Flash = namedtuple("Flash", "first third size bright now_first now_third", defaults=(None, None))
# An archetype and its variants each, as WeaponProperties name them.
FLASHES = {
    # 9x18 mm from a short barrel, 9 mm burnt in a 26 cm one: smaller than the .45s.
    "Secondary.Makarov": Flash(*pair("base_gun"), 0.8, 1.0),
    "Secondary.Uzi": Flash(*pair("base_gun"), 0.8, 1.0),
    # .45 ACP from a 15 cm barrel.
    "Secondary.MAC10": Flash(*pair("base_gun"), 1.2, 1.0),
    # .50 AE: a pistol round with a rifle's charge.
    "Secondary.DesertEagle": Flash(*pair("desert_eagle"), 1.2, 1.2),
    # The birdcage and the NATO rifles' flash hiders break the flash into prongs, as on the M249.
    "Primary.M16": Flash(*pair("m249saw"), 0.9, 1.0, *pair("ak47")),
    "Primary.FNFAL": Flash(*pair("m249saw"), 1.2, 1.1, *pair("ak47")),
    "Primary.G3KA4": Flash(*pair("m249saw"), 1.2, 1.1, *pair("ak47")),
    # The Dragunov's slotted flash hider is the PKM's, on the same round.
    "Primary.Dragunov": Flash(*pair("pkm"), 1.0, 1.0, *pair("sniper_riffle")),
    # .30-06 from a bare muzzle.
    "Special.M1903": Flash(*pair("ak47"), 1.2, 1.0, *pair("sniper_riffle")),
    # .50 BMG through a muzzle brake: the blast thrown to the sides.
    "Primary.AS50": Flash(*pair("as50"), 1.4, 1.3),
    # A shotgun's flash in first person too, where the game shows the AK-47's.
    "Primary.USAS12": Flash(pair("ithaca")[0], None, 1.0, 1.0, pair("ak47")[0]),
    # .50 BMG through a conical flash hider.
    "Special.M2": Flash("weapons.weapons.muzzleflash_m249saw_mounted", None, 1.8, 1.3,
                        "weapons.weapons.muzzleflash_ak47"),
    "MountedWeapons.M2_Mounted": Flash("weapons.weapons.muzzleflash_m249saw_mounted",
                                       "weapons.weapons.muzzleflash_m249saw_mounted", 1.8, 1.3),
    # Barrels cut short: the powder still burning as it leaves.
    "DLC1.SawedOffShotgun": Flash("dlc1_particles.dlc1.pl_muzzleflash_sawedgun", pair("ithaca")[1], 1.4, 1.2),
}


def system_name(gun, third):
    return PREFIX + gun.split(".")[-1].lower() + ("_3rd" if third else "")


# The (first-person, third-person) systems each gun's weapon properties name, None for the game's own.
NAMES = {gun: (system_name(gun, False), system_name(gun, True) if row.third else None)
         for gun, row in FLASHES.items()}


def guid(name):
    return "{%s}" % str(uuid.uuid5(uuid.NAMESPACE_URL, name)).upper()


def is_flash(emitter):
    return FLASH_TEXTURE in (emitter.get("DiffuseTexture") or "").lower()


def number(value):
    return ("%.4f" % value).rstrip("0").rstrip(".")


def scale_curve(emitter, curve, factor):
    for part in ("ValueKF", "VariantKF"):
        element = emitter.find("%s/%s" % (curve, part))
        element.set("Value", number(float(element.get("Value")) * factor))


def scaled(stock, name, row, timing):
    """A copy of a stock system under `name`, its flash emitters sized and brightened by the row and
    given `timing`'s length."""
    system = copy.deepcopy(stock)
    system.set("Name", name)
    system.set("Id", guid(name))
    for emitter in system.iter("PartEmit"):
        emitter.set("Name", "%s.%s" % (name, emitter.get("Name").rsplit(".", 1)[-1]))
        emitter.set("Id", guid(emitter.get("Name")))
        if not is_flash(emitter):
            continue
        scale_curve(emitter, "InitSizeKF", row.size)
        emitter.set("PosOffset", ",".join(number(float(v) * row.size) for v in emitter.get("PosOffset").split(",")))
        emitter.set("HDRMul", number(float(emitter.get("HDRMul", "1")) * row.bright))
        if timing is not None:
            for attribute in TIMING_ATTRIBUTES:
                emitter.set(attribute, timing.get(attribute))
            for curve in TIMING_CURVES:
                emitter[list(emitter).index(emitter.find(curve))] = copy.deepcopy(timing.find(curve))
    return system


def systems(path):
    return {s.get("Name").lower(): s for s in ET.parse(path).getroot().iter("PartSys")}


def depload(path):
    """Each resource's children in a decoded depload, by the resource's CRC."""
    return {int(r.get("crc_ID")): list(r) for r in ET.parse(path).getroot().iter("Resource")}


def resource(crc, children):
    element = ET.Element("Resource", crc_ID=str(crc))
    for child in children:
        element.append(ET.Element(child.tag, crc_ID=child.get("crc_ID")))
    ET.indent(element, "\t")
    return ET.tostring(element, encoding="unicode") + "\n"


def emit(world, library, entries, name, stock_name, timing_name, row):
    folder, stem = LIBRARIES[world]
    particles = os.path.join(LAYER, *folder, stem + "_deploadnewparticles.rml")
    deploads = os.path.join(LAYER, *folder, stem + "_depload.dat")
    stock = library[stock_name.lower()]
    timing = None
    if timing_name is not None:
        timing = next((e for e in library[timing_name.lower()].iter("PartEmit") if is_flash(e)), None)
    system = scaled(stock, name, row, timing)
    ET.indent(system, "\t")
    write(os.path.join(particles, numbered(name)), ET.tostring(system, encoding="unicode") + "\n")
    # The system's entry lists its emitters, and each emitter's lists what its stock original loads.
    emitters = list(system.iter("PartEmit"))
    write(os.path.join(deploads, numbered(name)), resource(path_id(name), [
        ET.Element("CParticlesEmitterParamResource", crc_ID=str(path_id(e.get("Name")))) for e in emitters]))
    for copied, original in zip(emitters, stock.iter("PartEmit")):
        loads = entries.get(path_id(original.get("Name")))
        if loads is None:
            sys.exit("%s's depload has no entry for %s" % (world, original.get("Name")))
        write(os.path.join(deploads, numbered(copied.get("Name"))), resource(path_id(copied.get("Name")), loads))
    print(world, name)


def main(paths):
    libraries = dict(zip(LIBRARIES, (systems(p) for p in paths[:4])))
    entries = dict(zip(LIBRARIES, (depload(p) for p in paths[4:])))
    for gun, row in FLASHES.items():
        views = tuple(zip(NAMES[gun], (row.first, row.third), (row.now_first, row.now_third)))
        for world in ("dlc1",) if gun.startswith("DLC1.") else WORLDS:
            for name, stock, now in views:
                if stock is not None:
                    emit(world, libraries[world], entries[world], name, stock, now, row)


if __name__ == "__main__":
    if len(sys.argv) != 9:
        sys.exit(__doc__)
    main(sys.argv[1:])
