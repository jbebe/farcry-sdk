"""The papers lying in a world: for each, the Predecessor's envelope pickup lifted out of a decoded
entity library, drawn as the stock single sheet and naming its Items id in FCSE entity data, and an
instance of it placed in a sector. Written as the world's archetype and sector fragments in the
layer.

    python papers.py <entitylibrary.xml> <layer> <world>
"""
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "scripts"))
from entity_fragments import add_entity_data, fragment_path, prototypes, replace_hashed, replace_value, write

library, layer, world = sys.argv[1:4]
ENVELOPE = "OA_MissionPickups.MissionPickups.PartnerMissionEnvelope"
SHEET = "OA_DetailObjects.DetailObjects.SingleSheet_BK"
GRAPHIC = re.compile(r'<component class="GraphicComponent".*?</component>', re.S)
PAPERS = [
    # On the bed of the burnt-out truck StaticObject_10702, by its open tailgate, flat on the planks.
    {"item": "Torn_notebook_page", "archetype_id": 81302, "world": "world1", "level": "w1_c_2",
     "sector": 2988, "entity_id": 2099000000000000201, "pos": (1834.6381, 2389.0729, 36.0984),
     "angles": (7.3121, 0.5206, 43.2083)},
]


def vector(name, values):
    return ['  <value name="%s" type="Vector3">' % name,
            *("    <%s>%s</%s>" % (axis, v, axis) for axis, v in zip("xyz", values)),
            "  </value>"]


lines = open(library, encoding="utf-8").read().split("\n")
blocks = {name: "\n".join(lines[start:end]) for name, start, end in prototypes(lines) if name in (ENVELOPE, SHEET)}

# The envelope drawn as the sheet: lit, glinting and kept like a placed retail pickup, and saved once
# picked like a diamond case.
pickup = GRAPHIC.sub(lambda m: GRAPHIC.search(blocks[SHEET]).group(0), blocks[ENVELOPE], count=1)
pickup = pickup.replace(r"Equipment\Envelope\Envelope.hkx", r"Equipment\Folder\SingleSheet.hkx")
for name, extension in [("fileName", ".xml"), ("objModel", ".xbg"), ("hidResourceId", ".hkx")]:
    pickup = replace_hashed(pickup, name, r"graphics\objects\equipment\envelope\envelope" + extension,
                            r"graphics\objects\equipment\folder\singlesheet" + extension)
for name, suffix in [("hidNodeName", ""), ("hidNodeNameLOD0", "_LOD0")]:
    pickup = replace_hashed(pickup, name, "Envelope" + suffix, "SingleSheet" + suffix)
for name, kind, old, new in [
        ("olgLightGroup", "Hash", "00000001", "00000081"),
        ("bAllowCullBySize", "Bool", "True", "False"),
        ("bBehaveLikeAPickup", "Bool", "False", "True"),
        ("selLevel", "UInt32", "1", "3")]:
    pickup = replace_value(pickup, name, kind, old, new)

for paper in (p for p in PAPERS if p["world"] == world):
    name = "MissionPickups.InventoryOverhaul_" + paper["item"]
    text = pickup
    for field, kind, old, new in [
            ("Name", "String", "MissionPickups.PartnerMissionEnvelope", name),
            ("hidName", "String", ENVELOPE, "OA_MissionPickups." + name),
            ("disEntityId", "Int64", "4422", paper["archetype_id"])]:
        text = replace_value(text, field, kind, old, new)
    assert "nvelope" not in text
    block = text.split("\n")
    add_entity_data(block, {"InventoryOverhaul.Item": paper["item"]})
    write(fragment_path(layer, ("worlds", world, "generated", "entitylibrary.fcb"), "OA_MissionPickups." + name),
          block)

    instance = "InventoryOverhaul_" + paper["item"]
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
