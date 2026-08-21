# Render an .xbg to a PNG, headless, for looking at what the importer built.
#
#   & "C:\Programs\Blender 5.2\blender.exe" -b --python render_preview.py -- model.xbg out.png
#   ... -- model.xbg out.png --highlight 5    colours one part red
#
# The numeric gates cannot see a part sitting in the wrong place if it still
# lands inside the model bounds, so this exists to be looked at.

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)

import bpy
import mathutils

from addon import import_xbg

PALETTE = [(0.85, 0.35, 0.25, 1), (0.30, 0.60, 0.85, 1), (0.45, 0.80, 0.45, 1),
           (0.90, 0.75, 0.30, 1), (0.70, 0.45, 0.85, 1), (0.40, 0.80, 0.80, 1)]


def arguments():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    highlight = None
    if "--highlight" in argv:
        at = argv.index("--highlight")
        highlight = int(argv[at + 1])
        argv = argv[:at] + argv[at + 2:]
    return argv[0], argv[1], highlight


def frame(parts):
    corners = [obj.matrix_world @ mathutils.Vector(corner)
               for obj in parts for corner in obj.bound_box]
    low = mathutils.Vector([min(c[i] for c in corners) for i in range(3)])
    high = mathutils.Vector([max(c[i] for c in corners) for i in range(3)])
    return (low + high) / 2.0, max((high - low).length / 2.0, 0.05)


def main():
    target, out, highlight = arguments()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = import_xbg.load(target, lod=0)["parts"]
    if not parts:
        print("nothing to render")
        return 1

    centre, radius = frame(parts)
    direction = mathutils.Vector((1.0, -1.6, 0.75)).normalized()
    camera = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    camera.location = centre + direction * radius * 3.2
    camera.rotation_euler = (direction * -1).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    sun = bpy.data.lights.new("key", type="SUN")
    sun.energy = 4.0
    light = bpy.data.objects.new("key", sun)
    light.rotation_euler = (math.radians(55), 0.0, math.radians(35))
    bpy.context.scene.collection.objects.link(light)

    for index, obj in enumerate(parts):
        if highlight is None:
            obj.color = PALETTE[index % len(PALETTE)]
        else:
            obj.color = (0.9, 0.25, 0.2, 1) if index == highlight else (0.55, 0.6, 0.65, 1)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("rendered %d parts to %s" % (len(parts), out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
