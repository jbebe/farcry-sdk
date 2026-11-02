"""Write the layer's movement fragments: how quickly the player gets going and stops, with the top
speeds and the jump left as the game has them.

Each fragment is a player archetype, or the sprint's speed curve, copied whole out of a decoded
entity library with only the values below changed, so JackAll merges it field by field with any
other mod's copy and reports a clash on the same field.

    jackall-cli fcb decode <world1 entitylibrary.fcb> -o world1.xml
    jackall-cli fcb decode <world2 entitylibrary.fcb> -o world2.xml
    python data\\movement.py world1.xml world2.xml

The libraries are the campaign's: worlds\\world{1,2}\\generated\\entitylibrary.fcb.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fragments import LAYER, of, set_value
from entity_fragments import fragment_path, prototypes, write

LIBRARIES = (
    ("worlds", "world1", "generated", "entitylibrary.fcb"),
    ("worlds", "world2", "generated", "entitylibrary.fcb"),
)

# The player and the twelve characters under it.
PLAYER = "player.MainCharacter.PawnPlayer"
# Metres a second squared a walk speeds up and slows down by, which the game's 30 and 30 do in a
# seventh of a second. Slowing down is only toward a slower pace, such as a walk from a sprint or
# the sights' pace: let go of every key and the game stops the player dead.
RATES = {"fWalkingAcceleration": "8", "fWalkingDeceleration": "10"}

# The sprint's speed over the seconds since it began, which the game's curve takes to its top in
# half a second. Its first rise is stretched to this many seconds, and every later knot moved as far,
# so the top speed, its fall to a run and that run's speed stay as they are.
SPRINT = "Curves.Locomotion.Sprint"
SPRINT_RISE = 1.5


def stretch_sprint(block):
    """Moves every knot after the first by as much as the first rise is stretched."""
    knots = [i + 1 for i, l in enumerate(block) if l.strip() == '<value name="Value" type="Vector4">']
    times = [float(re.search(r"<x>([^<]*)</x>", block[i]).group(1)) for i in knots]
    later = SPRINT_RISE - times[1]
    for i, time in zip(knots[1:], times[1:]):
        block[i] = re.sub(r"<x>[^<]*</x>", "<x>%.4f</x>" % (time + later), block[i])


def main(paths):
    written = 0
    for path, library in zip(paths, LIBRARIES):
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        for name, start, end in prototypes(lines):
            block = lines[start:end]
            if of(name, (PLAYER,)):
                for value_name, value in RATES.items():
                    set_value(block, value_name, value)
            elif name == SPRINT:
                stretch_sprint(block)
            else:
                continue
            out = fragment_path(LAYER, library, name)
            write(out, block)
            written += 1
            print(os.path.relpath(out, LAYER))
    print("%d fragments" % written)


if __name__ == "__main__":
    main(sys.argv[1:3])
