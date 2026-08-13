"""Stage B2: propose a small candidate set for each still-unmatched function.

BSim scoring is a decompilation per function, so it cannot be run all-pairs over
a 60k x 60k residue. It does not need to be: two builds of the same source lay
their functions out in nearly the same order, so an unmatched function is almost
always bracketed by two matched neighbours, and its counterpart lies between
their images. That window is a far better prior than a global scan, and it works
for functions with no callers at all -- which is exactly the case call-graph
propagation cannot reach.

Two candidate sources, unioned:

  window     unmatched target functions lying between the mapped images of the
             nearest matched reference neighbours
  callgraph  unmatched callees of the target counterparts of this function's
             matched callers

    python candidates.py [--config addrlib.toml]
"""

import argparse
import bisect
import os
import sys
from collections import defaultdict

import common


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--prefix", default="")
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    pfx = args.prefix
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: candidate generation%s"
                               % (" [%s]" % pfx if pfx else ""))

    ref_funcs = common.load_functions(cfg, "reference")
    tgt_funcs = common.load_functions(cfg, "target")

    mapping = {}
    for r in common.read_csv_rows(os.path.join(out, pfx + "mapping.csv")):
        if r["tier"] in common.SHIPPING_TIERS:
            mapping[common.parse_rva(r["ref_rva"])] = common.parse_rva(r["tgt_rva"])
    taken = set(mapping.values())

    ref_unmatched = sorted(r for r in ref_funcs if r not in mapping)
    tgt_unmatched = sorted(t for t in tgt_funcs if t not in taken)
    reporter.line("unmatched: reference=%d target=%d"
                  % (len(ref_unmatched), len(tgt_unmatched)))

    anchors_sorted = sorted(mapping)
    tgt_sorted = tgt_unmatched

    # reference callee -> callers, for the call-graph candidate source
    callers = defaultdict(list)
    for rva, row in ref_funcs.items():
        for c in row["callees"]:
            if isinstance(c, int):
                callers[c].append(rva)

    bc = cfg["bsim"]
    max_cand = bc["max_candidates_per_function"]
    size_ratio = bc["candidate_size_ratio"]

    rows = []
    stats = defaultdict(int)
    for rva in ref_unmatched:
        row = ref_funcs[rva]
        cands = set()

        # --- window ------------------------------------------------------
        i = bisect.bisect_left(anchors_sorted, rva)
        lo_ref = anchors_sorted[i - 1] if i > 0 else None
        hi_ref = anchors_sorted[i] if i < len(anchors_sorted) else None
        if lo_ref is not None and hi_ref is not None:
            lo, hi = mapping[lo_ref], mapping[hi_ref]
            if lo < hi:
                a = bisect.bisect_right(tgt_sorted, lo)
                b = bisect.bisect_left(tgt_sorted, hi)
                span = tgt_sorted[a:b]
                if len(span) <= max_cand:
                    cands.update(span)
                    stats["window"] += 1
                else:
                    stats["window_too_wide"] += 1

        # --- call graph ---------------------------------------------------
        for caller in callers.get(rva, []):
            tgt_caller = mapping.get(caller)
            if tgt_caller is None:
                continue
            trow = tgt_funcs.get(tgt_caller)
            if trow is None:
                continue
            for c in trow["callees"]:
                if isinstance(c, int) and c not in taken:
                    cands.add(c)
            stats["callgraph"] += 1

        # --- structural filter -------------------------------------------
        kept = []
        for c in cands:
            trow = tgt_funcs.get(c)
            if trow is None:
                continue
            a, b = row["size"], trow["size"]
            if max(a, b) > 0 and abs(a - b) / max(a, b) > size_ratio:
                continue
            kept.append(c)

        if not kept:
            stats["no_candidates"] += 1
            continue
        kept.sort(key=lambda c: abs(tgt_funcs[c]["size"] - row["size"]))
        rows.append({"ref_rva": rva, "candidates": kept[:max_cand]})
        stats["with_candidates"] += 1

    reporter.section("candidate sources")
    for k in sorted(stats):
        reporter.line("    %-20s %d" % (k, stats[k]))
    total = sum(len(r["candidates"]) for r in rows)
    reporter.line("")
    reporter.line("    functions with candidates : %d" % len(rows))
    reporter.line("    candidate pairs to score  : %d" % total)
    if rows:
        reporter.line("    mean candidates/function  : %.1f" % (total / len(rows)))

    common.write_jsonl(os.path.join(out, pfx + "candidates.jsonl"), rows)
    reporter.line("    -> out/%scandidates.jsonl" % pfx)
    reporter.save(os.path.join(out, pfx + "candidates_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
