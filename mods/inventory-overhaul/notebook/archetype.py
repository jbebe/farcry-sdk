"""gadgets.Equipped.Notebook: gadgets.Equipped.Map lifted out of a decoded entity library and pointed at
the notebook's pages, its descriptor, its graphic component's objects (one per page strip) and its
skeleton, in a gadget category of its own and without the map's GPS. Written as the world's fragment
in the layer.

    python archetype.py <entitylibrary.xml> <notebook.obj> <nodes.json> <layer> <world>
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "scripts"))
from entity_fragments import crc, fragment_path, prototypes, replace_hashed, replace_value, write

library, obj_path, nodes_path, layer, world = sys.argv[1:6]
NAME = "gadgets.Equipped.Notebook"
# The new archetype's id, and its gadget category, the spare unused1 (the map's is 1).
ENTITY_ID = 81301
CATEGORY = 2
# The notebook's own MOVE weapon index, whose branches are the map's (9) copied.
ANIMATION = 44
MODEL = r"graphics\objects\equipment\notebook\notebook.xbg"
SKELETON = r"graphics\objects\equipment\notebook\notebook_ref.skeleton"
DESCRIPTOR = r"graphics\objects\equipment\notebook\notebook.xml"
# The pages' nodes in model space, as page.py placed them, rotations w first.
NODES = {name: place for name, place in json.load(open(nodes_path)).items() if name.startswith("Page")}


def f(values):
    return ",".join("%g" % v for v in values)


points = [tuple(map(float, line.split()[1:4])) for line in open(obj_path) if line.startswith("v ")]
low = [min(p[a] for p in points) for a in range(3)]
high = [max(p[a] for p in points) for a in range(3)]
box = 'bboxMin="%s" bboxMax="%s"' % (f(low), f(high))

lines = open(library, encoding="utf-8").read().split("\n")
start, end = next((start, end) for name, start, end in prototypes(lines) if name == "gadgets.Equipped.Map")
text = "\n".join(lines[start:end])

objects = "\n".join(
    '                  <object index="%d" meshName="%s" boneName="%s" %s />' % (i, n, n, box)
    for i, n in enumerate(NODES))
bones = "\n".join(
    '                    <bone name="%s" pos="%s" rot="%s" />' % (n, f(p), f(r)) for n, (p, r) in NODES.items())
descriptor = (
    "<hidDescriptor>\n"
    '                <component class="GraphicComponent" version="2" detail="0">\n'
    f"{objects}\n"
    f'                  <resource fileName="{MODEL}" {box} />\n'
    '                  <skeleton name="Map" pos="0,0,0" rot="1,-0,-0,-0">\n'
    f"{bones}\n"
    "                  </skeleton>\n"
    "                </component>\n"
    "              </hidDescriptor>")
text, count = re.subn(r"<hidDescriptor>.*?</hidDescriptor>", lambda m: descriptor, text, count=1, flags=re.S)
assert count == 1

graphic = re.search(r'( *)<object type="object">\s*<value name="hidIndex".*?</object>', text, flags=re.S)
indent = graphic.group(1)
replacement = []
for i, n in enumerate(NODES):
    replacement.append("\n".join([
        f'{indent}<object type="object">',
        f'{indent}  <value name="hidIndex" type="UInt32">{i}</value>',
        f'{indent}  <value name="text_objModel" type="String">{MODEL}</value>',
        f'{indent}  <value name="objModel" type="Hash">{crc(MODEL)}</value>',
        f'{indent}  <value name="hidMeshName" type="String">{n}</value>',
        f'{indent}  <value name="text_hidNodeName" type="String">{n}</value>',
        f'{indent}  <value name="hidNodeName" type="Hash">{crc(n)}</value>',
        f'{indent}  <value name="text_hidNodeNameLOD0" type="String">{n}_LOD0</value>',
        f'{indent}  <value name="hidNodeNameLOD0" type="Hash">{crc(n + "_LOD0")}</value>',
        f'{indent}  <value name="hidDetailObject" type="Bool">False</value>',
        f"{indent}</object>"]))
text = text[:graphic.start()] + "\n".join(replacement) + text[graphic.end():]

text = replace_hashed(text, "fileName", r"graphics\objects\mapcompass\map.xml", DESCRIPTOR)
text = replace_hashed(text, "fileSkeleton", r"graphics\objects\mapcompass\map_ref.skeleton", SKELETON)
for name, kind, old, new in [
        ("Name", "String", "Equipped.Map", "Equipped.Notebook"),
        ("hidName", "String", "gadgets.Equipped.Map", NAME),
        ("disEntityId", "Int64", "328", ENTITY_ID),
        ("selCategory", "UInt32", "1", CATEGORY),
        ("iAnimationValue", "Int32", "9", ANIMATION),
        ("archCompassArchetype", "String", "gadgets.Equipped.CompassSingle", "")]:
    text = replace_value(text, name, kind, old, new)

write(fragment_path(layer, ("worlds", world, "generated", "entitylibrary.fcb"), NAME), text.split("\n"))
