"""Write the layer's weapon data: every bullet as lethal as its real round, from any shooter.

- Each gun's weapon properties: its bullet's impact level the one nearest the damage its round's muzzle
  energy gives, the soldiers' damage curves gone, and no close-range bonus on any difficulty.
- Each gun's archetype: its round as real figures, FCSE entity data any mod may read, as for the kick
  a round gives:

      Cartridge.Name            the round, as "7.62x39 mm"
      Cartridge.BulletMass      one projectile, in grams
      Cartridge.MuzzleVelocity  in metres per second, from this gun's barrel
      Cartridge.Projectiles     how many one round fires: a buckshot shell's pellets, 1 otherwise
      Cartridge.MuzzleEnergy    the whole round's, in joules

- gamemodesconfig.xml: the single-player hit locations, which scale every hit on anyone but the player:
  a head hit kills.
- materialimpacts.xml: blood sprays from a hit on anyone, the player too, by the whole round's muzzle
  energy, and only where the bullet goes in. Weapon.Bullet, which rounds under 1,000 J keep and every
  gun plays where its bullet comes out, sprays none; rounds over 1,000 J play Weapon.HeadBlood, a copy
  spraying from a head hit only, and over 10,000 J Weapon.Blood, a copy spraying from any hit. Beside
  them Blood.Spatter and Blood.Puddle, which the plugin plays on the ground and walls behind a hit and
  under the dead: no particles and no sound, and on every surface but flesh and water the decal of the
  same name.
- decal.xml: those two decals, the game's blood spray and blood puddle textures, kept until the game's
  decal budget recycles them.

Each archetype fragment is copied whole out of a decoded entity library with only those values changed
or FCSE's entity data component added, so JackAll merges it field by field with any other mod's copy.
Multiplayer (.Multi) archetypes are left alone, and so are guns that fire no bullet: launchers, the dart
rifle, the crossbow, the flare gun and the machetes.

    jackall-cli fcb decode <world1 entitylibrary.fcb> -o world1.xml
    jackall-cli fcb decode <world2 entitylibrary.fcb> -o world2.xml
    jackall-cli fcb decode <dlc1 entitylibrary.fcb> -o dlc1.xml
    jackall-cli fcb decode <tmpla entitylibrary.fcb> -o tmpla.xml
    python data\\weapons.py world1.xml world2.xml dlc1.xml tmpla.xml <engine\\gamemodes\\gamemodesconfig.xml>
        <databases\\materialimpacts\\materialimpacts.xml> <databases\\generic\\decal.xml>

The libraries are the campaign's, worlds\\world{1,2}\\generated\\entitylibrary.fcb and
downloadcontent\\dlc1\\generated\\entitylibrary.fcb, and tmpla's, worlds\\tmpla\\generated\\entitylibrary.fcb,
whose guns DevTools' test range copies into its own library; tmpla's fragments are written for that
library, worlds\\testrange. tmpla's and gamemodesconfig.xml are patch.dat's, materialimpacts.xml and
decal.xml common.dat's.
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "scripts"))
from entity_fragments import (add_entity_data, crc, find_value, fragment_path, has_object, object_lines, prototypes,
                              replace_hashed, write)

LAYER = os.path.join(HERE, "..", "layer", "mods")
LIBRARIES = (
    ("worlds", "world1", "generated", "entitylibrary.fcb"),
    ("worlds", "world2", "generated", "entitylibrary.fcb"),
    ("downloadcontent", "dlc1", "generated", "entitylibrary.fcb"),
    ("worlds", "testrange", "generated", "entitylibrary.fcb"),
)
GAME_MODES = os.path.join(LAYER, "engine", "gamemodes", "gamemodesconfig.xml")
IMPACTS = os.path.join(LAYER, "databases", "materialimpacts", "materialimpacts.xml")
DECALS = os.path.join(LAYER, "databases", "generic", "decal.xml")
# The impact record every gun's bullets play, left with no blood, and its copies some rounds play
# instead: one with blood from a head hit only, one with blood from any hit.
BULLET_IMPACT = "Weapon.Bullet"
HEAD_BLOOD = "Weapon.HeadBlood"
ALL_BLOOD = "Weapon.Blood"
# The logic materials whose bullet particles are the blood spray: a body hit's and a head hit's.
BODY, HEAD = "Base.Flesh", "Base.Flesh_Head"
# The field naming the record a bullet plays where it hits; the one for where it comes out keeps Weapon.Bullet.
IMPACT_FIELD = "matimpBulletImpactFx"
# The value an impact record's particle system or decal holds for none.
NONE = 4294967295
# The blood the plugin spawns, each a record and a decal of that name: their Ids, the decal's texture, its
# size in metres and spread, and the seconds it takes to fade in.
BLOOD_DECALS = {
    "Blood.Spatter": ("{78649DA6-52EE-4004-8041-4771031BA7DB}", "{2AC97185-051C-4125-B366-1FA28AFD0BE6}",
                      r"graphics\GFX\Impacts\GFX_blood_spray.XBT", 0.6, 0.15, 0),
    "Blood.Puddle": ("{4805DD53-522D-4FFC-ADC9-55EFE2D5C3B2}", "{45899DF8-DF1A-43D4-A8F5-F2DAB1B62AE4}",
                     r"graphics\objects\bloodsplashes\bloodpuddle01_d.xbt", 0.8, 0.2, 4),
}
# The surfaces a blood decal is never put on.
NO_BLOOD_DECAL = (BODY, HEAD, "Base.Water_deep", "Base.Water_low", "Base.NoImpact")
# Seconds a blood decal lasts, longer than any fight: the decal budget recycles it first.
BLOOD_LIFETIME = 9999

# Each round: one projectile's mass in grams, and how many one round fires.
BUCKSHOT = "12 gauge 00 buckshot"
ROUNDS = {
    "9x18 mm Makarov": (6.1, 1),
    "9x19 mm Parabellum": (8.0, 1),
    ".45 ACP": (15.0, 1),
    ".50 Action Express": (19.4, 1),
    "5.56x45 mm NATO": (4.0, 1),
    "7.62x39 mm": (7.9, 1),
    "7.62x51 mm NATO": (9.5, 1),
    "7.62x54 mmR": (9.6, 1),
    ".30-06 Springfield": (9.7, 1),
    ".50 BMG": (42.0, 1),
    BUCKSHOT: (3.5, 9),
}
# The whole round's muzzle energy in joules from which a hit sprays blood, on the head and anywhere:
# a rifle's, a buckshot shell's and the Desert Eagle's pass the first, only the .50 BMG the second.
HEAD_BLOOD_ENERGY = 1000.0
ALL_BLOOD_ENERGY = 10000.0
# Each gun, as its archetype is named under weapons, DLC1Weapons and WeaponProperties, with its
# variants: its round and muzzle velocity in metres per second. The mounts name their gun.
GUNS = {
    "Secondary.Makarov": ("9x18 mm Makarov", 315),
    "Secondary.SilencedMakarov_6P9": ("9x18 mm Makarov", 290),
    "Secondary.Star45": (".45 ACP", 255),
    "Secondary.MAC10": (".45 ACP", 280),
    "Secondary.Uzi": ("9x19 mm Parabellum", 390),
    "Secondary.DesertEagle": (".50 Action Express", 470),
    "Primary.MP5": ("9x19 mm Parabellum", 400),
    "Primary.AK47": ("7.62x39 mm", 715),
    "Primary.M16": ("5.56x45 mm NATO", 948),
    "Primary.FNFAL": ("7.62x51 mm NATO", 840),
    "Primary.G3KA4": ("7.62x51 mm NATO", 720),
    "Primary.Dragunov": ("7.62x54 mmR", 830),
    "Primary.AS50": (".50 BMG", 850),
    "Primary.Ithaca": (BUCKSHOT, 400),
    "Primary.SPAS12": (BUCKSHOT, 400),
    "Primary.USAS12": (BUCKSHOT, 400),
    "Special.M1903": (".30-06 Springfield", 853),
    "Special.M249_Saw": ("5.56x45 mm NATO", 915),
    "Special.PKM": ("7.62x54 mmR", 825),
    "Special.M2": (".50 BMG", 887),
    "MountedWeapons.M249_Mounted": ("5.56x45 mm NATO", 915),
    "MountedWeapons.M2_Mounted": (".50 BMG", 887),
    "DLC1.SawedOffShotgun": (BUCKSHOT, 350),
    "DLC1.SilencedShotgun": (BUCKSHOT, 330),
}
PROPERTIES_ROOT = "WeaponProperties"
ROOTS = ("weapons", "DLC1Weapons", PROPERTIES_ROOT)

# One projectile's damage per square root of its muzzle energy in joules: two torso hits from a 9x18 mm
# Makarov drop a soldier of 250 health, one from a rifle.
DAMAGE_PER_ROOT_JOULE = 7.3
# The health a stim level takes, the knots of the game's Curves.StimEffectCurves.OldDamageHealth.
DAMAGE_CURVE = ((0.0, 0.0), (9.03, 47.0543), (18.0236, 100.032), (24.0103, 201.831), (28.0165, 399.253),
                (29.9966, 999.758), (32.0055, 1604.11))
LEVELS = range(1, 33)
# The soldiers' damage curves a gun's properties name, by visibility.
AI_CURVES = ("archDamageMultiplierVisibleCurve", "archDamageMultiplierNotVisibleCurve")
# The single-player hit locations' multipliers, from the game's head 6, torso 1, arms 1, the rest 0.5.
HIT_LOCATIONS = {"Head": "100.0", "Torso": "1.0", "Arms": "0.6", "Legs": "1.0", "Hands": "0.3", "Feet": "0.3"}


def gun(relative):
    """The gun's round and muzzle velocity, or None."""
    return next((value for name, value in GUNS.items()
                 if relative == name or relative.startswith(name + ".")), None)


def projectile_energy(round_name, velocity):
    return 0.5 * ROUNDS[round_name][0] / 1000.0 * velocity * velocity


def curve_damage(level):
    (x0, y0), (x1, y1) = next((a, b) for a, b in zip(DAMAGE_CURVE, DAMAGE_CURVE[1:]) if level <= b[0])
    return y0 + (y1 - y0) * (level - x0) / (x1 - x0)


def nearest_level(damage):
    return min(LEVELS, key=lambda level: abs(curve_damage(level) - damage))


def impact_level(round_name, velocity):
    return nearest_level(DAMAGE_PER_ROOT_JOULE * math.sqrt(projectile_energy(round_name, velocity)))


def muzzle_energy(round_name, velocity):
    return ROUNDS[round_name][1] * projectile_energy(round_name, velocity)


def blood(round_name, velocity):
    """The impact record the gun's bullets play where they hit, or None to keep Weapon.Bullet."""
    energy = muzzle_energy(round_name, velocity)
    return ALL_BLOOD if energy >= ALL_BLOOD_ENERGY else HEAD_BLOOD if energy >= HEAD_BLOOD_ENERGY else None


def entity_data(round_name, velocity):
    return {"Cartridge.Name": round_name, "Cartridge.BulletMass": ROUNDS[round_name][0],
            "Cartridge.MuzzleVelocity": float(velocity), "Cartridge.Projectiles": ROUNDS[round_name][1],
            "Cartridge.MuzzleEnergy": round(muzzle_energy(round_name, velocity), 1)}


def set_in(block, kind, name, value):
    """Sets every value `name` inside the one object of this type, if the archetype has one."""
    if not has_object(block, kind):
        return
    first, last, _ = object_lines(block, kind)
    pattern = re.compile(r'(<value name="%s" type="[^"]+">)[^<]*(</value>)' % name)
    for i in range(first, last):
        block[i] = pattern.sub(lambda m: m.group(1) + value + m.group(2), block[i])


def edit_properties(block, round_name, velocity):
    level = str(impact_level(round_name, velocity))
    # The second is a shotgun's, for its pellets off the centre of its spread.
    for stim in ("Stim_ImpactDamage", "Stim_ImpactDamageSecondary"):
        set_in(block, stim, "nLevel", level)
    set_in(block, "RangeMultipliers", "iMultiplier", "0")
    for curve in AI_CURVES:
        set_in(block, "AICurve", curve, "")
    text = "\n".join(block)
    record = blood(round_name, velocity)
    # A variant that names no impact record keeps none.
    if record and find_value(text, "text_" + IMPACT_FIELD, "String") == BULLET_IMPACT:
        block[:] = replace_hashed(text, IMPACT_FIELD, BULLET_IMPACT, record).split("\n")


def hit_locations(text):
    start = text.index("<HitLocations>", text.index("<DefaultCountersService>"))
    end = text.index("</HitLocations>", start)
    section = text[start:end]
    for part, multiplier in HIT_LOCATIONS.items():
        section, count = re.subn(r'(<%s multiplier=")[^"]*(")' % part, r"\g<1>%s\2" % multiplier, section)
        assert count == 1, part
    return text[:start] + section + text[end:]


def renamed(record, name, record_id):
    return re.sub(r'Id="[^"]*"', 'Id="%s"' % record_id,
                  record.replace('"%s"' % BULLET_IMPACT, '"%s"' % name), count=1)


def without_particles(record, material):
    """The record with the particles of every material `material` matches blanked, and how many there were."""
    return re.subn(
        r'(<ParticlesSystem disName="%s" disMatId="\d+" psParticlesSystem=")[^"]*(" crc_psParticlesSystem=")\d+'
        % material, r"\g<1>\g<2>%d" % NONE, record)


def without_blood(record, *materials):
    for material in materials:
        record, count = without_particles(record, re.escape(material))
        assert count == 1, material
    return record


def blood_decal_record(bullet, name, record_id):
    """A copy of the bullet's record with no particles or sound, whose decal on every surface is `name`."""
    record = renamed(bullet, name, record_id)
    record = re.sub(r'sndSwitchSound="[^"]*"', 'sndSwitchSound="0x%08X"' % NONE, record, count=1)
    record, _ = without_particles(record, '[^"]+')
    return re.sub(r'(<Decal disName="([^"]+)" disMatId="\d+" decalDecal=")\d+',
                  lambda m: m.group(1) + str(NONE if m.group(2) in NO_BLOOD_DECAL else int(crc(name), 16)), record)


def impacts(text):
    start = text.index('\t<MaterialImpact Name="%s"' % BULLET_IMPACT)
    end = text.index("</MaterialImpact>", start) + len("</MaterialImpact>\r\n")
    bullet = text[start:end]
    head_blood = without_blood(renamed(bullet, HEAD_BLOOD, "{E2FB8C04-6D59-4621-AF39-BA8118292BF6}"), BODY)
    all_blood = renamed(bullet, ALL_BLOOD, "{CB5AD2F5-9B7B-4613-8E77-77960A45E7E8}")
    blood_decals = "".join(blood_decal_record(bullet, name, record_id)
                           for name, (record_id, *_) in BLOOD_DECALS.items())
    return text[:start] + without_blood(bullet, BODY, HEAD) + head_blood + all_blood + blood_decals + text[end:]


def decals(text):
    """decal.xml with the blood decals, each a copy of the bullet wound's with its own texture and size."""
    start = text.index('\t<Generic Name="%s"' % BODY)
    wound = text[start:text.index("\r\n", start) + 2]
    added = ""
    for name, (_, decal_id, texture, size, spread, fade_in) in BLOOD_DECALS.items():
        decal = wound.replace('"%s"' % BODY, '"%s"' % name)
        for field, value in (("Id", decal_id), ("crc_hidName", int(crc(name), 16)), ("texDiffuse", texture),
                             ("texSpecular", ""), ("texNormal", ""), ("fLifeTime", BLOOD_LIFETIME),
                             ("fFadeInDuration", fade_in), ("fFadeOutDuration", 2), ("bUniformSize", 1),
                             ("fSizeX", size), ("fSizeXVariance", spread), ("fSizeY", size),
                             ("fSizeYVariance", spread)):
            decal, count = re.subn(r'( %s=")[^"]*(")' % field, lambda m: m.group(1) + str(value) + m.group(2),
                                   decal, count=1)
            assert count == 1, field
        added += decal
    end = text.index("</GenericLibrary>")
    return text[:end] + added + text[end:]


def write_retail(retail, out, edit):
    """Writes the retail file at `out` in the layer, its text passed through `edit`."""
    with open(retail, encoding="utf-8", newline="") as handle:
        text = edit(handle.read())
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)
    print(os.path.relpath(out, LAYER))


def main(paths, game_modes, material_impacts, decal_types):
    written = 0
    for path, library in zip(paths, LIBRARIES):
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        for name, start, end in prototypes(lines):
            root, _, relative = name.partition(".")
            if root not in ROOTS or ".Multi" in name:
                continue
            found = gun(relative)
            if found is None:
                continue
            block = lines[start:end]
            if root == PROPERTIES_ROOT:
                edit_properties(block, *found)
            else:
                add_entity_data(block, entity_data(*found))
            if block == lines[start:end]:
                continue
            out = fragment_path(LAYER, library, name)
            write(out, block)
            written += 1
            print(os.path.relpath(out, LAYER))
    write_retail(game_modes, GAME_MODES, hit_locations)
    write_retail(material_impacts, IMPACTS, impacts)
    write_retail(decal_types, DECALS, decals)
    print("%d fragments" % written)
    for name, (round_name, velocity) in GUNS.items():
        level = impact_level(round_name, velocity)
        print("%-32s %-20s level %2d, %4.0f health" % (name, round_name, level, curve_damage(level)))


if __name__ == "__main__":
    main(sys.argv[1:5], sys.argv[5], sys.argv[6], sys.argv[7])
