# Open a Far Cry 2 model pack in Blender's UI, optionally animated.
#
#   blender.exe --python open_model.py -- path\to\model.fc2model [lod] [clip]
#
# Arguments after the pack are recognised by what they are: a number is the LOD,
# anything else names one of the animation banks the pack carries, matched on
# the tail of its path. Passing a script path avoids the shell quoting that makes
# a --python-expr one-liner behave differently in cmd and PowerShell.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy

from addon import import_mab, import_xbg

USAGE = "usage: blender --python open_model.py -- <model.fc2model> [lod] [clip]"


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print(USAGE)
        return
    path, lod, wanted = argv[0], 0, None
    for argument in argv[1:]:
        if argument.isdigit():
            lod = int(argument)
        else:
            wanted = argument
    if not os.path.exists(path):
        print("no such file: %s" % path)
        return

    result = import_xbg.load(path, lod=lod)
    pack = result["pack"]
    print("opened %s: %d parts at LOD%d, %d animation bank(s)"
          % (os.path.basename(path), len(result["parts"]), lod, len(pack.clips)))
    if not wanted:
        for clip in sorted(pack.clips, key=lambda c: c["label"].casefold())[:20]:
            print("   %s  %d frames at %d Hz%s"
                  % (clip["label"], clip.get("frames", 0), clip.get("rate", 0),
                     " on %s" % clip["bone"] if clip.get("bone") else ""))
        return

    matched = next((c for c in pack.clips
                    if wanted.lower() in c["path"].replace("\\", "/").lower()), None)
    if matched is None:
        print("no bank in this pack matches %r" % wanted)
        return
    if result["armature"] is None:
        print("no armature to animate")
        return

    loaded = import_mab.load(pack, matched["path"], result["armature"],
                             with_props=True, actor=result.get("actor"))
    import_mab.apply_to_scene(bpy.context.scene, loaded["clip"])
    print("animated with %s: %d bones, %d keys, %d tracks name no bone here"
          % (matched["label"], loaded["bones"], loaded["keys"], loaded["unmatched"]))
    if loaded.get("actor"):
        print("   %s poses %d bones and holds the model at %s"
              % (os.path.basename(pack.actor), loaded["actor"]["bones"],
                 loaded["actor"].get("bone", "nothing")))


main()
