"""Write the layer's vehicle archetype fragments: FCSE's entity data component naming the real vehicle
each car depicts, by which src\\real_vehicle.cpp knows the car whatever mass or engine a mod gives it.

Each fragment is a vehicle archetype copied whole out of a decoded entity library with the component
added, or the layer's own fragment of it given the component, so JackAll merges it field by field with
any other mod's copy. Multiplayer archetypes are left alone, as are the world libraries' stand-ins for
the DLC vehicles, which the DLC library outranks.

    jackall-cli fcb decode <world1 entitylibrary.fcb> -o world1.xml
    jackall-cli fcb decode <world2 entitylibrary.fcb> -o world2.xml
    jackall-cli fcb decode <dlc1 entitylibrary.fcb> -o dlc1.xml
    python data\\fragments.py world1.xml world2.xml dlc1.xml
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "scripts"))
from entity_fragments import add_entity_data, fragment_path, prototypes, read, write

LAYER = os.path.join(HERE, "..", "layer", "mods")
KEY = "VehicleOverhaul.Vehicle"
VEHICLE = "vehicle."

# The real vehicle each car depicts, an archetype and its variants each, by its name in
# src\real_vehicle.cpp.
WORLD_VEHICLES = {
    "Land.Rover": "Land Rover Series III",
    "Land.Datsun": "Datsun 1200",
    "Land.Buggy": "Buggy",
    "Land.JeepLiberty": "Jeep Liberty",
    "Land.JeepWrangler": "Jeep Wrangler",
    "Land.JeepWrangler.SE_Taxi": "Jeep Wrangler taxi",
    "Land.BigTruck": "ZIL-130",
}
DLC_VEHICLES = {
    "Land.DLC_Vehicle1_DLC1": "Quad",
    "Land.DLC_Vehicle2_DLC1": "Unimog",
}
LIBRARIES = (
    (("worlds", "world1", "generated", "entitylibrary.fcb"), WORLD_VEHICLES),
    (("worlds", "world2", "generated", "entitylibrary.fcb"), WORLD_VEHICLES),
    (("downloadcontent", "dlc1", "generated", "entitylibrary.fcb"), DLC_VEHICLES),
)


def vehicle_of(archetype, vehicles):
    """The real vehicle of the archetype, or of the closest one it is a variant of."""
    for name in sorted(vehicles, key=len, reverse=True):
        if archetype == name or archetype.startswith(name + "."):
            return vehicles[name]
    return None


def main(paths):
    written = 0
    for path, (library, vehicles) in zip(paths, LIBRARIES):
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        for name, start, end in prototypes(lines):
            archetype = name[len(VEHICLE):]
            if (not name.startswith(VEHICLE)
                    or any(part.startswith("Multi") for part in archetype.split("."))):
                continue
            vehicle = vehicle_of(archetype, vehicles)
            if vehicle is None:
                continue
            out = fragment_path(LAYER, library, name)
            block, newline = (read(out) if os.path.exists(out) else (lines[start:end], "\n"))
            if any(KEY in line for line in block):
                continue
            add_entity_data(block, {KEY: vehicle})
            write(out, block, newline)
            written += 1
            print(os.path.relpath(out, LAYER))
    print("%d fragments" % written)


if __name__ == "__main__":
    main(sys.argv[1:4])
