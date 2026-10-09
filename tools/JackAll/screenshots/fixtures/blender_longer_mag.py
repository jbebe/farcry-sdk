# The edit the Blender tutorial makes by hand: the AK-47's magazine, 40% longer, in a model pack.
#   blender --factory-startup -b --python blender_longer_mag.py -- <in.fc2model> <out.fc2model>

import os
import sys

ADDON = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "BlenderFC2"))
sys.path.insert(0, ADDON)

import bpy

from addon import export_xbg, import_xbg

source, target = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
result = import_xbg.load(source, lod=0, with_textures=False)

# The magazine is both CLIP parts. Mesh data, not the objects: a part moved in object mode is
# discarded on export.
meshes = [o.data for o in result["collection"].all_objects if o.name.startswith("CLIP_LOD0")]
top = max(v.co.z for mesh in meshes for v in mesh.vertices)
for mesh in meshes:
    for vertex in mesh.vertices:
        vertex.co.z = top + (vertex.co.z - top) * 1.4

export_xbg.save(target, result["collection"])
