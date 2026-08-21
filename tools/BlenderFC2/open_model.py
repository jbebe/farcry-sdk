# Open a Far Cry 2 model or bundle in Blender's UI, optionally animated.
#
#   blender.exe --python open_model.py -- path\to\model.xbg [lod] [clip.mab]
#
# Arguments after the model are recognised by what they are: a number is the
# LOD, a .mab is a clip to put on the armature. Passing a script path avoids the
# shell quoting that makes a --python-expr one-liner behave differently in cmd
# and PowerShell.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy

from addon import import_mab, import_xbg

USAGE = "usage: blender --python open_model.py -- <model.xbg or .fc2model> [lod] [clip.mab]"


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print(USAGE)
        return
    path, lod, clip = argv[0], 0, None
    for argument in argv[1:]:
        if argument.lower().endswith(".mab"):
            clip = argument
        elif argument.isdigit():
            lod = int(argument)
        else:
            print(USAGE)
            return
    for name in (path, clip):
        if name and not os.path.exists(name):
            print("no such file: %s" % name)
            return

    result = import_xbg.load(path, lod=lod)
    print("opened %s: %d parts at LOD%d"
          % (os.path.basename(path), len(result["parts"]), lod))
    if not clip:
        return

    if result["armature"] is None:
        print("no armature to animate")
        return
    loaded = import_mab.load(clip, result["armature"], model_path=path)
    import_mab.apply_to_scene(bpy.context.scene, loaded["clip"])
    print("animated with %s: %d bones, %d keys, %d tracks name no bone here"
          % (os.path.basename(clip), loaded["bones"], loaded["keys"], loaded["unmatched"]))
    print("   bones named by %s" % os.path.basename(loaded["skeleton"]))


main()
