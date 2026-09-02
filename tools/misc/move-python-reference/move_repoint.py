"""Point a MOVE graph's clip references at different .mab files.

A state names its clip only by `m_animNameHash`, the CRC32 of the clip's
lowercased game path, so retargeting a weapon's animation is substituting one
hash for another. That is what lets a replacement weapon borrow another's
animation set without shipping a copy of it, and own outright only the clips it
actually re-authored.

The map is a TSV of `old game path <TAB> new game path`. Paths are hashed, not
opened, so the new one need not exist yet.

`--weapon` is not optional in practice. A clip filed under one weapon's folder
can still be played by another - the dart rifle's own `1stge_uppb_draw` is also
played by the MGL-140 - so rewriting every reference to a hash retargets those
other weapons too. With `--weapon`, only the references a given EquippedWeapon
index governs are rewritten, which is separable because each reference site has
exactly one nearest governing weapon.

  python move_repoint.py movemgr.bin out.bin --map vss.tsv --weapon 39
  python move_repoint.py movemgr.bin --map vss.tsv --weapon 39 --dry-run
"""
import argparse
import collections
import zlib

import move_codec as mc
from move_expand import CH_DESIRED, CH_EQUIPPED, criterion

FIELD = "m_animNameHash"


def path_id(name):
    """CPathID: CRC32 of the lowercased name."""
    return zlib.crc32(name.lower().encode()) & 0xFFFFFFFF


def read_map(path):
    pairs = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line or line.startswith("#") or "\t" not in line:
                continue
            old, new = line.split("\t")[:2]
            pairs[path_id(old)] = (path_id(new), old, new)
    return pairs


class Scope(dict):
    """Which objects a weapon governs, and which weapons govern any other."""

    governed = None


def governs(mf, weapon):
    """Whether each object's nearest weapon-pinned ancestor is this weapon.

    A reference belongs to the weapon whose criteria are closest above it, so a
    clip two weapons share is still separable: each site answers to one of them.
    Sites with no weapon above them at all are shared behaviour and belong to
    nobody - `governed` returns None for those.
    """
    parent, pinned, answer = {}, {}, Scope()
    for obj in mf.seq:
        for kind, _name, value in obj.ops:
            if kind == "pnew":
                parent[id(value)] = obj

    def weapons(obj):
        if id(obj) in pinned:
            return pinned[id(obj)]
        found = set()
        pinned[id(obj)] = found
        for kind, _name, value in obj.ops:
            if kind != "pnew":
                continue
            channel, comparand = criterion(value)
            if channel in (CH_EQUIPPED, CH_DESIRED) and comparand is not None:
                found.add(comparand)
            found |= weapons(value)
        pinned[id(obj)] = found
        return found

    def governed(obj):
        """The weapons whose criteria sit closest above this object, or None."""
        at = obj
        while at is not None:
            here = weapons(at)
            if here:
                return here
            at = parent.get(id(at))
        return None

    for obj in mf.seq:
        answer[id(obj)] = governed(obj) == {weapon}
    answer.governed = governed
    return answer


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("src")
    ap.add_argument("dst", nargs="?")
    ap.add_argument("--map", required=True)
    ap.add_argument("--weapon", type=int,
                    help="Only rewrite references this EquippedWeapon index governs")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    pairs = read_map(args.map)
    mf = mc.load(args.src)
    mine = governs(mf, args.weapon) if args.weapon is not None else None

    hits, theirs, loose = collections.Counter(), collections.Counter(), collections.Counter()
    for obj in mf.seq:
        for index, (kind, name, value) in enumerate(obj.ops):
            if name != FIELD or value not in pairs:
                continue
            if mine is not None and not mine[id(obj)]:
                (loose if mine.governed(obj) is None else theirs)[value] += 1
                continue
            hits[value] += 1
            if not args.dry_run:
                obj.ops[index] = (kind, name, pairs[value][0])

    if theirs:
        print("left alone, another weapon governs them: %d reference(s) across %d clip(s)"
              % (sum(theirs.values()), len(theirs)))
        for old, count in theirs.most_common():
            print("   %3d  %s" % (count, pairs[old][1].split("\\")[-1]))
        print()

    # An ungoverned site is shared behaviour: rewriting it would retarget every
    # weapon that reaches it. Leaving it is right, but it also means this weapon
    # can still reach the old clip, so the retarget is incomplete rather than done.
    if loose:
        print("WARNING: %d reference(s) across %d clip(s) are governed by no weapon, so this "
              "weapon still reaches the old clip through them:"
              % (sum(loose.values()), len(loose)))
        for old, count in loose.most_common():
            print("   %3d  %s" % (count, pairs[old][1].split("\\")[-1]))
        print()

    print("%d of %d mapped clips are referenced, %d references in total"
          % (len(hits), len(pairs), sum(hits.values())))
    for old, (_new, old_path, new_path) in sorted(pairs.items(), key=lambda kv: kv[1][1]):
        print("   %3d  %s\n        -> %s"
              % (hits[old], old_path.split("\\")[-1], new_path.split("\\")[-1]))
    missing = [p[1] for k, p in pairs.items() if not hits[k]]
    if missing:
        print("\n%d mapped clips are named by no state:" % len(missing))
        for path in sorted(missing):
            print("   %s" % path.split("\\")[-1])

    if args.dry_run or not args.dst:
        return
    open(args.dst, "wb").write(mc.save(mf))
    check = mc.load(args.dst)
    mine = governs(check, args.weapon) if args.weapon is not None else None
    left = sum(1 for obj in check.seq for _k, name, value in obj.ops
               if name == FIELD and value in pairs
               and (mine is None or mine[id(obj)]))
    print("\nwrote %s: %d objects, %d of this weapon's references still point at the old clips"
          % (args.dst, len(check.seq), left))


if __name__ == "__main__":
    main()
