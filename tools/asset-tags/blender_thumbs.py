# Renders a textured three-quarter view of each .fc2model pack a job list names, for render.py.
#   blender -b --factory-startup --python blender_thumbs.py -- <jobs.json>   ([[pack, png], ...])
import json
import os
import sys

import bpy
import mathutils

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "BlenderFC2"))
from addon import import_xbg  # noqa: E402

SIZE = 320


def setup_scene():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_backface_culling = False
    scene.render.resolution_x = scene.render.resolution_y = SIZE
    scene.render.image_settings.file_format = "PNG"
    scene.world = bpy.data.worlds.new("background")
    scene.world.color = (0.32, 0.34, 0.36)
    return scene


def frame(scene, box):
    """A camera looking down at the box from the front right, far enough to fit it."""
    low, high = mathutils.Vector(box[:3]), mathutils.Vector(box[3:])
    centre, size = (low + high) / 2, max((high - low).length, 0.05)
    camera = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
    scene.collection.objects.link(camera)
    camera.data.lens = 50
    camera.location = centre + mathutils.Vector((1.0, -1.4, 0.9)).normalized() * size * 1.9
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.clip_start, camera.data.clip_end = size * 0.01, size * 10
    scene.camera = camera


def show_diffuse(parts):
    """Makes each material's diffuse image the one Workbench draws."""
    for obj in parts:
        for material in obj.data.materials:
            if material and material.node_tree:
                images = [n for n in material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image]
                images.sort(key=lambda n: "_d" not in n.image.name.lower())
                if images:
                    material.node_tree.nodes.active = images[0]


for pack, png in json.load(open(sys.argv[sys.argv.index("--") + 1])):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = setup_scene()
    try:
        result = import_xbg.load(pack, lod=0, with_armature=False)
        show_diffuse(result["parts"])
        frame(scene, result["mesh"]["box"])
        scene.render.filepath = png
        bpy.ops.render.render(write_still=True)
    except Exception as error:
        print("FAILED", pack, error)
