"""A picture of the sheet held, as the game's camera sees it at an 87 degree field of view on a 16:9 screen,
for checking hold.py without the game: the pack's sheet and arms posed by one of its banks at one frame,
drawn with the sheet's own picture, and the 4:3 screen's edges marked.

    blender -b --python preview_hold.py -- <held.fc2model> <sheet.png> <bank part> <frame> <out.png>
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools", "BlenderFC2",
                                "tests"))
from _corpus import PACKAGE_ROOT  # noqa: F401

import bpy
from mathutils import Matrix

from addon import convert, import_mab, import_xbg
from addon.import_mab import FIRST_FRAME
from addon.pack import Pack

argv = sys.argv[sys.argv.index("--") + 1:]
SOURCE, PICTURE, BANK, FRAME, OUT = argv[0], argv[1], argv[2], int(argv[3]), argv[4]
# The game's field of view is the width of a 4:3 screen; a wider one sees further to the sides.
FOV, WIDTH, HEIGHT = 87.0, 1920, 1080
FOUR_THREE = HEIGHT * 4 // 3

pack = Pack.load(SOURCE)
bpy.ops.wm.read_factory_settings(use_empty=True)
built = import_xbg.build(pack, "sheet", lod=0, with_textures=True)
model, body = built["armature"], built["actor"]["armature"]
import_mab.load(pack, next(c["path"] for c in pack.clips if BANK in c["path"]), model, actor=built["actor"])
scene = bpy.context.scene
scene.frame_set(FRAME + FIRST_FRAME)

picture = bpy.data.images.load(PICTURE)
for part in built["parts"]:
    for slot in part.material_slots:
        for tree_node in slot.material.node_tree.nodes if slot.material and slot.material.use_nodes else []:
            if tree_node.type == "TEX_IMAGE":
                tree_node.image = picture

eye = body.matrix_world @ convert.node_frame(body.pose.bones["Camera"].matrix)
camera = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
scene.collection.objects.link(camera)
camera.matrix_world = eye @ Matrix.Rotation(math.pi / 2, 4, "X")
camera.data.sensor_fit = "HORIZONTAL"
camera.data.angle = 2 * math.atan(math.tan(math.radians(FOV) / 2) * (WIDTH / HEIGHT) / (4 / 3))
camera.data.clip_start = 0.02
scene.camera = camera
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
# The game draws only the side a face looks to.
scene.display.shading.show_backface_culling = True
scene.display.shading.background_type = "VIEWPORT"
scene.display.shading.background_color = (0.45, 0.55, 0.65)
scene.render.resolution_x, scene.render.resolution_y = WIDTH, HEIGHT
scene.render.filepath = OUT
bpy.ops.render.render(write_still=True)

image = bpy.data.images.load(OUT)
pixels = list(image.pixels)
for x in ((WIDTH - FOUR_THREE) // 2, (WIDTH + FOUR_THREE) // 2):
    for y in range(HEIGHT):
        for dx in (0, 1):
            i = (y * WIDTH + x + dx) * 4
            pixels[i:i + 4] = [1.0, 0.85, 0.0, 1.0]
image.pixels = pixels
image.save_render(OUT)
print("preview written", OUT)
