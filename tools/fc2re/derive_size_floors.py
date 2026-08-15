# Derive lower bounds on class size from evidence already harvested.
#
# Allocation sites only cover the classes something calls `new` on. Everything
# else ends up a 4-byte struct holding just its vptr, and Ghidra then renders
# accesses past it as this[3].vptr -- a member name on an offset it does not
# describe. These bounds are real evidence rather than a fabricated default:
#
#   base        a base at offset O whose size is S puts the derived class at
#               O + S or more, applied transitively over the RTTI graph
#   vptr        a secondary subobject table with offset-to-top -T means a vptr
#               sits at offset T, so the class reaches at least T + 4
#   member      the last recovered property member ends where it ends
#
# Allocation sizes stay authoritative; a bound is only used where none exists,
# and is marked exact=false so the appliers can say so.
#
# Output: class_sizes_merged.jsonl, same shape as class_sizes.jsonl, so it
# drops straight into --sizes on either applier.
#
#   python derive_size_floors.py out
#
# Reads and writes files only; never touches Ghidra.

import argparse
import json
import os
from collections import defaultdict

POINTER_SIZE = 4

# Inheritance is acyclic, so this only bounds pathological input.
MAX_ROUNDS = 32

# Past this a size came from a buffer that borrowed a class name.
MAX_TRUSTED_SIZE = 0x20000


def load_jsonl(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


def name_of(row):
    """Ghidra's spelling, which is what the type database is keyed on."""
    return row.get("ghidra_class") or row.get("class")


def base_edges(typeinfo):
    """class -> [(base class, offset)], resolved through typeinfo symbols."""
    by_symbol = {t["symbol"]: name_of(t) for t in typeinfo if t.get("symbol")}
    edges = defaultdict(list)
    for t in typeinfo:
        cls = name_of(t)
        if not cls:
            continue
        for b in t.get("bases") or []:
            base = by_symbol.get(b.get("typeinfo"))
            if base and base != cls:
                edges[cls].append((base, b.get("offset") or 0))
    return edges


def vptr_floors(vtables):
    """A subobject table at offset-to-top -T needs room for a vptr at T."""
    out = {}
    for v in vtables:
        cls = name_of(v)
        if not cls:
            continue
        for sub in v.get("subobjects") or []:
            at = -(sub.get("offset_to_top") or 0)
            if at >= 0:
                out[cls] = max(out.get(cls, 0), at + POINTER_SIZE)
    return out


def member_floors(properties):
    """The last recovered property member ends where it ends."""
    out = {}
    for r in properties:
        cls = r.get("owner")
        off = r.get("offset")
        if cls and off is not None:
            out[cls] = max(out.get(cls, 0), off + POINTER_SIZE)
    return out


def solve(exact, seeds, edges):
    """Raise every class to the largest bound its bases imply.

    Iterated to a fixpoint rather than resolved in topological order, because
    the graph is built from recovered names and need not be a clean DAG.
    """
    size = dict(seeds)
    for cls, value in exact.items():
        size[cls] = max(size.get(cls, 0), value)

    for _ in range(MAX_ROUNDS):
        changed = False
        for cls, bases in edges.items():
            best = size.get(cls, 0)
            for base, offset in bases:
                got = size.get(base)
                if got:
                    best = max(best, offset + got)
            if best > size.get(cls, 0):
                size[cls] = best
                changed = True
        if not changed:
            break
    return size


def merge(exact_rows, size, exact_names):
    rows = []
    by_class = {r["class"]: r for r in exact_rows}
    for cls in sorted(size):
        value = size[cls]
        if not value or value > MAX_TRUSTED_SIZE:
            continue
        prior = by_class.get(cls)
        is_exact = cls in exact_names
        row = {
            "class": cls,
            "size": max(value, prior["size"]) if prior else value,
            "exact": is_exact,
            "sites": prior.get("sites", 0) if prior else 0,
        }
        if prior and prior.get("conflicting_sizes"):
            row["conflicting_sizes"] = prior["conflicting_sizes"]
        rows.append(row)
    return rows


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("outdir")
    args = ap.parse_args()
    d = args.outdir

    typeinfo = load_jsonl(os.path.join(d, "typeinfo.jsonl"))
    vtables = load_jsonl(os.path.join(d, "vtables.jsonl"))
    props = load_jsonl(os.path.join(d, "register_properties.jsonl"))
    sizes = load_jsonl(os.path.join(d, "class_sizes.jsonl"))

    exact = {r["class"]: r["size"] for r in sizes
             if r.get("size") and r["size"] <= MAX_TRUSTED_SIZE}
    edges = base_edges(typeinfo)
    vf = vptr_floors(vtables)
    mf = member_floors(props)

    seeds = defaultdict(int)
    for src in (vf, mf):
        for cls, value in src.items():
            seeds[cls] = max(seeds[cls], value)

    size = solve(exact, seeds, edges)
    rows = merge(sizes, size, set(exact))

    out = os.path.join(d, "class_sizes_merged.jsonl")
    with open(out, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=True) + "\n")

    gained = [r for r in rows if not r["exact"]]
    raised = [r for r in rows
              if not r["exact"] and r["size"] > vf.get(r["class"], 0)]
    print("typeinfo records     : %d" % len(typeinfo))
    print("inheritance edges    : %d" % sum(len(v) for v in edges.values()))
    print("classes with a vptr floor : %d" % len(vf))
    print("classes with a member floor: %d" % len(mf))
    print()
    print("exact sizes (allocation) : %d" % len(exact))
    print("bounded sizes (derived)  : %d" % len(gained))
    print("  raised above the bare vptr by a base: %d" % len(raised))
    print("total classes sized      : %d" % len(rows))
    print()
    print("[+] wrote %s" % out)


if __name__ == "__main__":
    main()
