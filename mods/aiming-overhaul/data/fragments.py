"""Write the layer's weapon archetype fragments: which guns fire tracers, how fast and how long their
streaks are, and which scopes lose the engine's iron-sight post effect.

Each fragment is a WeaponProperties archetype copied whole out of a decoded entity library, with
only the fields below changed, so JackAll merges it field by field with any other mod's copy and
reports a clash on the same field. Multiplayer (.Multi) archetypes are left alone.

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
# The scopes the plugin draws itself, whose iron-sight post effect would blur the world around
# them: the weapons in src\scopes.cpp's table.
with open(os.path.join(HERE, "..", "src", "scopes.cpp"), encoding="utf-8") as _scopes:
    DRAWN_SCOPES = tuple(re.findall(r'\{"weapons\.([^"]+)"', _scopes.read()))

PREFIX = "WeaponProperties."


def of(name, archetypes):
    """Whether the archetype is one of these or a variant under one."""
    return any(name == a or name.startswith(a + ".") for a in archetypes)


def prototypes(lines):
    """Each EntityPrototype's hidName and the line range of its node."""
    for start, line in enumerate(lines):
        if line.strip() != '<object hash="256A1FF9">':
            continue
        indent = line[:len(line) - len(line.lstrip())]
        end = next(i for i in range(start + 1, len(lines))
                   if lines[i].startswith(indent + "</object>"))
        name = next(re.search(r">([^<]*)<", l).group(1) for l in lines[start:end]
                    if '<value name="hidName"' in l)
        yield name, start, end + 1


def find_value(block, name, within=None):
    """The line of the one value of this name in the block, inside the object of type `within` if
    given."""
    first, last = 0, len(block)
    if within is not None:
        first = next(i for i, l in enumerate(block) if l.strip() == '<object type="%s">' % within)
        indent = block[first][:len(block[first]) - len(block[first].lstrip())]
        last = next(i for i in range(first + 1, len(block))
                    if block[i].startswith(indent + "</object>"))
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
    return (any(l.strip() == '<object type="BulletTracer">' for l in block)
            and get_value(block, "texTexture", "BulletTracer") != "FFFFFFFF"
            and get_value(block, "iFrequency", "BulletTracer") != "0")


def main(paths):
    written = 0
    for path, library in zip(paths, LIBRARIES):
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        for name, start, end in prototypes(lines):
            if not name.startswith(PREFIX) or name.endswith(".Multi"):
                continue
            archetype = name[len(PREFIX):]
            block = lines[start:end]
            if fires_tracers(block):
                if of(archetype, TRACER_GUNS):
                    set_value(block, "fSpeed", TRACER_SPEED, "BulletTracer")
                    set_value(block, "fLength", TRACER_LENGTH, "BulletTracer")
                else:
                    set_value(block, "iFrequency", "0", "BulletTracer")
            if of(archetype, DRAWN_SCOPES):
                set_value(block, "text_IronsightFX", "")
                set_value(block, "IronsightFX", "FFFFFFFF")
            if block == lines[start:end]:
                continue
            out = os.path.join(LAYER, *library, *name.split(".")) + ".xml"
            os.makedirs(os.path.dirname(out), exist_ok=True)
            with open(out, "w", encoding="utf-8", newline="\n") as handle:
                handle.write("\n".join(block) + "\n")
            written += 1
            print(os.path.relpath(out, LAYER))
    print("%d fragments" % written)


if __name__ == "__main__":
    main(sys.argv[1:4])
