"""Write the layer's particle libraries: the blood a bullet hit sprays, the particle system
bullet_impact.bullet_impact.b_imp_flesh, as a red mist and a few droplets in place of the game's splash.

- The splash, an animated blob of blood, and the dust puff beside it are gone.
- The mist is the game's red smoke puff, smaller, fainter and quicker.
- The droplets are the machete hit's, three flung out fast where the splash flew, falling as they fade.

JackAll merges no particle library, so each is the game's whole, with only that system changed.

    jackall-cli rml decode <worlds\\world1\\generated\\world1_deploadnewparticles.rml> -o world1.xml
    jackall-cli rml decode <worlds\\world2\\generated\\world2_deploadnewparticles.rml> -o world2.xml
    jackall-cli rml decode <worlds\\tmpla\\generated\\tmpla_deploadnewparticles.rml> -o tmpla.xml
    jackall-cli rml decode <downloadcontent\\dlc1\\generated\\entitylibrary_deploadnewparticles.rml> -o dlc1.xml
    python data\\particles.py world1.xml world2.xml tmpla.xml dlc1.xml

The libraries are worlds.fat's and dlc1's entitylibrary.fat's. tmpla's is written as the test range's, which
DevTools ships as a copy of it.
"""
import copy
import os
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from weapons import LAYER

HERE = os.path.dirname(os.path.abspath(__file__))
CLI = os.path.join(HERE, "..", "..", "..", "tools", "JackAll", "src", "JackAll.Cli", "bin", "Release", "net10.0",
                   "jackall-cli.exe")
LIBRARIES = (
    ("worlds", "world1", "generated", "world1_deploadnewparticles.rml"),
    ("worlds", "world2", "generated", "world2_deploadnewparticles.rml"),
    ("worlds", "testrange", "generated", "testrange_deploadnewparticles.rml"),
    ("downloadcontent", "dlc1", "generated", "entitylibrary_deploadnewparticles.rml"),
)
SYSTEM = "bullet_impact.bullet_impact.b_imp_flesh"
EMITTER = "bullet_impact.Bullet_impact.B_imp_flesh."
SPLASH, DUST, MIST = EMITTER + "spurt", EMITTER + "spurt2", EMITTER + "spurt3"
DROPLETS = "machete_impact.Slash.M_imp_flesh.spurt3"
SPRINKLE, SPRINKLE_ID = EMITTER + "sprinkle", "{7D2F4C0F-A035-4B7B-81E8-5EA633BF7DE3}"
# The splash's aim, which the sprinkle takes: out of the wound in a 45-degree cone.
AIM = ("RotOffset", "EmitDirAxisAngle", "EmitDirAxisSpread", "EmitDirPlaneAngle", "EmitDirPlaneSpread", "EmitFlag",
       "EmitVolRadius")


def block(text, opening, closing):
    """The start and end of the element that `opening` begins."""
    start = text.index(opening)
    return start, text.index(closing, start) + len(closing)


def emitter(system, name):
    return next(e for e in system.iter("PartEmit") if e.get("Name") == name)


def set_init(emit, curve, value, variant):
    """Sets an Init curve's value and its random spread."""
    emit.find(curve + "/ValueKF").set("Value", str(value))
    emit.find(curve + "/VariantKF").set("Value", str(variant))


def set_keys(emit, curve, value):
    """Sets every key of a colour curve."""
    for key in emit.find(curve):
        key.set("Value", value)


def set_knots(emit, curve, knots):
    """Replaces a curve over a particle's life with these (life, value) knots."""
    element = emit.find(curve)
    del element[:]
    for x, y in knots:
        ET.SubElement(element, "Knot", X=str(x), Y=str(y))


def mist(emit):
    emit.set("NbInitPart", "3")
    set_init(emit, "EmitRateKF", 10, 0)
    set_init(emit, "InitSizeKF", 0.7, 0.2)
    set_init(emit, "InitLifeTimeKF", 0.7, 0)
    set_init(emit, "InitSpeedKF", 0.8, 0.3)
    # Blood red, where the game's dark tint times its red came out nearly black.
    set_keys(emit, "InitColorKF", "0.420000,0.030000,0.030000")
    set_keys(emit, "PartColorKF", "1.000000,1.000000,1.000000")
    for knot in emit.find("InitAlphaKF"):
        knot.set("Y", "0.8")
    # Held for a third of its life, then gone, where the game's fell by half in the first fifth.
    set_knots(emit, "PartAlphaKF", ((0, 1), (0.35, 0.8), (1, 0)))


def sprinkle(droplets, splash):
    emit = copy.deepcopy(droplets)
    emit.set("Name", SPRINKLE)
    emit.set("Id", SPRINKLE_ID)
    for name in AIM:
        emit.set(name, splash.get(name))
    emit.find("EmitFlag").attrib = dict(splash.find("EmitFlag").attrib)
    emit.find("Flags").set("NoEmitRot", splash.find("Flags").get("NoEmitRot"))
    emit.set("NbInitPart", "3")
    emit.set("MaxEmitDist", "100")
    emit.set("FadeEmitDist", "75")
    set_init(emit, "InitSizeKF", 0.25, 0.08)
    set_init(emit, "InitLifeTimeKF", 0.6, 0)
    set_init(emit, "InitSpeedKF", 3, 1)
    return emit


def edit(text, droplets):
    start, end = block(text, '<PartSys Name="%s"' % SYSTEM, "</PartSys>")
    system = ET.fromstring(text[start:end])
    splash = emitter(system, SPLASH)
    mist(emitter(system, MIST))
    system.append(sprinkle(droplets, splash))
    system.remove(splash)
    system.remove(emitter(system, DUST))
    return text[:start] + ET.tostring(system, encoding="unicode") + text[end:]


def main(paths):
    texts = [open(path, encoding="utf-8").read() for path in paths]
    # The machete's droplets, from whichever library holds them.
    opening = '<PartEmit Name="%s"' % DROPLETS
    source = next(t for t in texts if opening in t)
    start, end = block(source, opening, "</PartEmit>")
    droplets = ET.fromstring(source[start:end])
    with tempfile.TemporaryDirectory() as work:
        for text, library in zip(texts, LIBRARIES):
            edited = os.path.join(work, library[-1] + ".xml")
            with open(edited, "w", encoding="utf-8") as handle:
                handle.write(edit(text, droplets))
            out = os.path.join(LAYER, *library)
            os.makedirs(os.path.dirname(out), exist_ok=True)
            subprocess.run([CLI, "rml", "encode", edited, "-o", out], check=True, capture_output=True)
            print(os.path.relpath(out, LAYER))


if __name__ == "__main__":
    if len(sys.argv) != 1 + len(LIBRARIES):
        sys.exit(__doc__)
    main(sys.argv[1:])
