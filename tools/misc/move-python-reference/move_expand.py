"""Clone the states of one weapon in a MOVE expansion onto a different weapon index.

Deep-copies every state whose subtree tests EquippedWeapon == <from>, remaps the
copies' internal back-references, renames them, retargets their weapon criteria and
appends them to the state machine. The parent link of each copy is left alone, so the
new states graft onto the same base-graph states the originals do.

  python move_expand.py dlc1.bin out.bin --from 42 --to 44 --prefix MyWeapon
"""
import argparse
import collections
import zlib

import move_codec as mc

ENUM_CRITERIA = ("CMoveCriteriaEnumEqual", "CMoveCriteriaEnumNotEqual")
CH_EQUIPPED, CH_DESIRED = 17, 18


def path_id(name):
    """CPathID: CRC32 of the lowercased name."""
    return zlib.crc32(name.lower().encode()) & 0xFFFFFFFF


def owned(obj, acc=None):
    """Every object this one owns, reachable through 'pnew' pointers."""
    if acc is None:
        acc = []
    acc.append(obj)
    for kind, _, value in obj.ops:
        if kind == "pnew":
            owned(value, acc)
    return acc


def clone(root):
    """Deep-copy a subtree; back-references inside it follow the copies."""
    originals = owned(root)
    copies = {id(o): mc.Obj(o.cls) for o in originals}
    for original in originals:
        copy = copies[id(original)]
        for kind, name, value in original.ops:
            if kind == "pnew":
                copy.ops.append((kind, name, copies[id(value)]))
            elif kind == "pref":
                copy.ops.append((kind, name, copies.get(id(value), value)))
            else:
                copy.ops.append((kind, name, value))
    return copies[id(root)], [copies[id(o)] for o in originals]


def criterion(obj):
    """(channel, comparand) for an enum criterion, or (None, None)."""
    if obj.cls not in ENUM_CRITERIA:
        return None, None
    return mc.field(obj, "m_eValueID"), mc.field(obj, "m_Value")


def retarget(objs, old, new):
    hits = 0
    for obj in objs:
        channel, value = criterion(obj)
        if channel in (CH_EQUIPPED, CH_DESIRED) and value == old:
            mc.set_field(obj, "m_Value", new)
            hits += 1
    return hits


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--from", dest="old", type=int, required=True)
    ap.add_argument("--to", dest="new", type=int, required=True)
    ap.add_argument("--prefix", default="MODDED")
    args = ap.parse_args()

    mf = mc.load(args.src)
    sm = mc.state_machine(mf)
    before = len(mf.seq)

    sources = [v for k, _, v in sm.ops
               if k == "pnew" and any(criterion(o) == (CH_EQUIPPED, args.old) for o in owned(v))]
    if not sources:
        raise SystemExit("no states test EquippedWeapon == %d in %s" % (args.old, args.src))

    criteria = 0
    for n, state in enumerate(sources):
        copy, objs = clone(state)
        mc.set_field(copy, "m_stateNameHash", path_id("%s_%d" % (args.prefix, n)))
        criteria += retarget(objs, args.old, args.new)
        sm.ops.append(("pnew", "CMoveBaseState", copy))
    mc.set_field(sm, "nbState", mc.field(sm, "nbState") + len(sources))

    open(args.dst, "wb").write(mc.save(mf))

    check = mc.load(args.dst)
    used = collections.Counter()
    for obj in check.seq:
        channel, value = criterion(obj)
        if channel in (CH_EQUIPPED, CH_DESIRED):
            used[(channel, value)] += 1
    print("cloned %d states (%d objects), retargeted %d criteria from %d to %d"
          % (len(sources), len(check.seq) - before, criteria, args.old, args.new))
    print("wrote %s: %d objects, %d states"
          % (args.dst, len(check.seq), mc.field(mc.state_machine(check), "nbState")))
    for channel in (CH_EQUIPPED, CH_DESIRED):
        for value in (args.old, args.new):
            print("   channel %d == %-3d %d criteria" % (channel, value, used[(channel, value)]))


if __name__ == "__main__":
    main()
