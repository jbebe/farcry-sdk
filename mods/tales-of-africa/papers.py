"""The papers lying in a world, read in Inventory Overhaul's notebook: for each, one of the
Predecessor's pickups lifted out of a decoded entity library, the envelope drawn as a stock sheet or photo or the
tape as it is, naming its Items id and a cassette its sound event in FCSE entity data, and an
instance of it placed in a sector. Written as the world's archetype and sector fragments in the
layer.

    python papers.py <entitylibrary.xml> <layer> <world>
"""
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "scripts"))
from entity_fragments import add_entity_data, fragment_path, prototypes, replace_hashed, replace_value, write

library, layer, world = sys.argv[1:4]
ENVELOPE = "OA_MissionPickups.MissionPickups.PartnerMissionEnvelope"
TAPE = "OA_MissionPickups.MissionPickups.PartnerMissionTape"
GRAPHIC = re.compile(r'<component class="GraphicComponent".*?</component>', re.S)
# What the envelope is drawn as: the archetype whose graphic it takes, and that graphic's file and
# node. The photo has no physics of its own, so both take the sheet's.
LOOKS = {
    "sheet": ("OA_DetailObjects.DetailObjects.SingleSheet_BK", r"graphics\objects\equipment\folder\singlesheet",
              "SingleSheet"),
    "photo": ("props.World1_HotelRoom.HotelRoom_Photo_1", r"graphics\objects\equipment\pictureframe\photo_01",
              "Photo_01"),
}
# The note flat where the boombox Appliances.BoomBoxRadio_398 stands, on a beer barrel; the photo and
# the cassette on the round table beside it.
PAPERS = [
    {"item": "Note.Torn_notebook_page", "base": ENVELOPE, "look": "sheet", "archetype_id": 81302,
     "world": "world1", "level": "w1_c_3", "sector": 2600, "entity_id": 2099000000000000204,
     "pos": (2604.02, 2108.09, 21.3459), "angles": (0, 0, 0)},
    {"item": "Photo.Cliff_village", "base": ENVELOPE, "look": "photo", "archetype_id": 81303,
     "world": "world1", "level": "w1_c_3", "sector": 2600, "entity_id": 2099000000000000205,
     "pos": (2606.95, 2106.80, 20.5065), "angles": (0, 0, 30)},
    {"item": "Tape.Test_cassette", "base": TAPE, "sound": "0x004E1C52", "archetype_id": 81304,
     "world": "world1", "level": "w1_c_3", "sector": 2600, "entity_id": 2099000000000000206,
     "pos": (2606.75, 2107.00, 20.5065), "angles": (0, 0, 0)},
]


def vector(name, values):
    return ['  <value name="%s" type="Vector3">' % name,
            *("    <%s>%s</%s>" % (axis, v, axis) for axis, v in zip("xyz", values)),
            "  </value>"]


def drawn_as(envelope, look):
    """The envelope with the look's graphic and the sheet's physics."""
    archetype, path, node = LOOKS[look]
    pickup = GRAPHIC.sub(lambda m: GRAPHIC.search(blocks[archetype]).group(0), envelope, count=1)
    pickup = pickup.replace(r"Equipment\Envelope\Envelope.hkx", r"Equipment\Folder\SingleSheet.hkx")
    for name, extension in [("fileName", ".xml"), ("objModel", ".xbg")]:
        pickup = replace_hashed(pickup, name, r"graphics\objects\equipment\envelope\envelope" + extension,
                                path + extension)
    pickup = replace_hashed(pickup, "hidResourceId", r"graphics\objects\equipment\envelope\envelope.hkx",
                            LOOKS["sheet"][1] + ".hkx")
    for name, suffix in [("hidNodeName", ""), ("hidNodeNameLOD0", "_LOD0")]:
        pickup = replace_hashed(pickup, name, "Envelope" + suffix, node + suffix)
    # Saved once picked, like a diamond case and the tape.
    return replace_value(pickup, "selLevel", "UInt32", "1", "3")


lines = open(library, encoding="utf-8").read().split("\n")
wanted = {ENVELOPE, TAPE} | {archetype for archetype, _, _ in LOOKS.values()}
blocks = {name: "\n".join(lines[start:end]) for name, start, end in prototypes(lines) if name in wanted}

for paper in (p for p in PAPERS if p["world"] == world):
    base = blocks[paper["base"]]
    text = drawn_as(base, paper["look"]) if "look" in paper else base
    name = "MissionPickups.TalesOfAfrica_" + paper["item"].replace(".", "_")
    base_id = re.search(r'<value name="disEntityId" type="Int64">(\d+)</value>', base).group(1)
    # Lit, glinting and kept like a placed retail pickup, and named.
    for field, kind, old, new in [
            ("olgLightGroup", "Hash", "00000001", "00000081"),
            ("bAllowCullBySize", "Bool", "True", "False"),
            ("bBehaveLikeAPickup", "Bool", "False", "True"),
            ("Name", "String", paper["base"].split(".", 1)[1], name),
            ("hidName", "String", paper["base"], "OA_MissionPickups." + name),
            ("disEntityId", "Int64", base_id, paper["archetype_id"])]:
        text = replace_value(text, field, kind, old, new)
    assert "nvelope" not in text
    block = text.split("\n")
    data = {"InventoryOverhaul.Item": paper["item"]}
    if "sound" in paper:
        data["InventoryOverhaul.Sound"] = paper["sound"]
    add_entity_data(block, data)
    write(fragment_path(layer, ("worlds", world, "generated", "entitylibrary.fcb"), "OA_MissionPickups." + name),
          block)

    instance = "TalesOfAfrica_" + paper["item"].replace(".", "_")
    sector = os.path.join(layer, "levels", paper["level"], "generated", "worldsectors",
                          "worldsector%d.data.fcb" % paper["sector"])
    write(os.path.join(sector, "%s.%d.xml" % (instance.lower(), paper["entity_id"])), [
        '<object type="Entity">',
        '  <value name="tplCreatureType" type="String">OA_MissionPickups.%s</value>' % name,
        '  <value name="hidName" type="String">%s</value>' % instance,
        '  <value name="disEntityId" type="Int64">%d</value>' % paper["entity_id"],
        *vector("hidPos", paper["pos"]),
        *vector("hidAngles", paper["angles"]),
        *vector("hidPos_precise", paper["pos"]),
        '  <object type="Components">',
        '    <object type="CEventComponent">',
        '      <value name="hidHasAliasName" type="Bool">False</value>',
        '      <object type="hidLinks" />',
        "    </object>",
        "  </object>",
        "</object>"])
