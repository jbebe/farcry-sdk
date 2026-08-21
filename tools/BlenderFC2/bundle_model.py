# Pack a model and every file it needs into a .fc2model bundle.
#
#   python bundle_model.py <model.xbg> [-o out.fc2model] [--root DIR]
#
# The root is the directory an archive was extracted to, the one holding
# `graphics`; it is found from the model's own path when not given.

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fc2fmt.assets import InstallAssets, find_root
from fc2fmt.bundle import EXTENSION, Bundle


def main(argv=None):
    parser = argparse.ArgumentParser(description="Pack a Far Cry 2 model into a bundle.")
    parser.add_argument("model", help="the .xbg to pack")
    parser.add_argument("-o", "--output", help="where to write the bundle")
    parser.add_argument("--root", help="extracted archive root to resolve assets against")
    parser.add_argument("--quiet", action="store_true", help="only report problems")
    args = parser.parse_args(argv)

    root = args.root or find_root(args.model)
    if not root:
        parser.error("no archive root above %s; pass --root" % args.model)

    bundle = Bundle.build(os.path.relpath(args.model, root), InstallAssets(root))
    output = args.output or os.path.splitext(args.model)[0] + EXTENSION
    bundle.write(output)

    owned = len(bundle.owned())
    if not args.quiet:
        print("%s: %d files (%d owned, %d shared), %.1f MB packed to %.1f MB"
              % (bundle.model, len(bundle.entries), owned, len(bundle.entries) - owned,
                 bundle.size / 1e6, os.path.getsize(output) / 1e6))
    for path in bundle.missing:
        print("missing: %s" % path, file=sys.stderr)
    return 1 if bundle.missing else 0


if __name__ == "__main__":
    sys.exit(main())
