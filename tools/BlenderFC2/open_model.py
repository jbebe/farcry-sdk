# Open a Far Cry 2 model in Blender's UI.
#
#   blender.exe --python open_model.py -- path\to\model.xbg [lod]
#
# Passing a script path avoids the shell quoting that makes a --python-expr
# one-liner behave differently in cmd and PowerShell.

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from addon import import_xbg


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("usage: blender --python open_model.py -- <model.xbg> [lod]")
        return
    path = argv[0]
    lod = int(argv[1]) if len(argv) > 1 else 0
    if not os.path.exists(path):
        print("no such file: %s" % path)
        return
    result = import_xbg.load(path, lod=lod)
    print("opened %s: %d parts at LOD%d" % (os.path.basename(path), len(result["parts"]), lod))


main()
