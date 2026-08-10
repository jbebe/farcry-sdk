"""Stage C: assign a shipping tier to every anchor, and apply the guards.

Reads out/anchors.jsonl (and out/bsim.jsonl when present), applies
overrides.csv, and writes out/mapping.csv -- the single artifact everything
downstream consumes.

Tiering follows the rule that an entry may only ship if it is either
byte/instruction identical, or close enough that the remaining difference is
explained by codegen noise rather than by being a different function:

  exact       export name, thunk target, identical bytes, or identical
              instructions with addresses masked
  near_exact  string/symbol/propagation/BSim evidence that also survives the
              structural guards below
  review      everything else - reported, never shipped

    python score.py [--config addrlib.toml]
"""

import argparse
import os
import sys
from collections import defaultdict

import common

EXACT_STAGES = {"export", "thunk", "exact_bytes", "exact_insn"}
NEAR_STAGES = {"string_unique", "symbol", "propagate", "bsim", "exact_mnem"}

# Precedence when several stages claim the same reference function. Higher wins.
STAGE_RANK = {"export": 100, "thunk": 90, "exact_bytes": 80, "exact_insn": 70,
              "string_unique": 60, "symbol": 50, "bsim": 40, "propagate": 30,
              "exact_mnem": 20}


def load_functions(cfg, key):
    build_id = cfg["builds"][key]["id"]
    path = os.path.join(common.cache_dir(cfg, create=False),
                        "%s.functions.jsonl" % build_id)
    return {r["rva"]: r for r in common.read_jsonl(path)}


def resolved_keys(callees, resolve):
    """Callee list reduced to target-space keys, unresolved entries as None."""
    out = []
    for t in callees:
        if isinstance(t, str):
            out.append(t)
        else:
            m = resolve(t)
            out.append(("f", m) if m is not None else None)
    return out


def guards_pass(ref_row, tgt_row, mapping, cfg, reasons):
    """Structural checks a non-exact pair must survive before it may ship."""
    acc = cfg["accept"]
    if ref_row is None or tgt_row is None:
        reasons["missing_function_row"] += 1
        return False

    a, b = ref_row["size"], tgt_row["size"]
    if max(a, b) > 0 and abs(a - b) / max(a, b) > acc["max_body_size_ratio"]:
        reasons["body_size"] += 1
        return False

    if acc["require_equal_call_count"] and \
            len(ref_row["callees"]) != len(tgt_row["callees"]):
        reasons["call_count"] += 1
        return False

    if acc["require_callee_correspondence"]:
        rk = resolved_keys(ref_row["callees"], lambda t: mapping.get(t))
        tk = resolved_keys(tgt_row["callees"],
                           lambda t: t if t in mapping.values_set else None)
        # Compare only the resolved skeleton: unresolved slots on either side
        # carry no information, but a resolved slot that disagrees is a real
        # contradiction and disqualifies the pair.
        rs = [k for k in rk if k is not None]
        ts = [k for k in tk if k is not None]
        if rs != ts:
            reasons["callee_correspondence"] += 1
            return False
    return True


class Mapping(dict):
    """ref_rva -> tgt_rva, with a reverse membership set for the guard above."""

    def __init__(self, *a, **kw):
        super().__init__(*a, **kw)
        self.values_set = set(self.values())


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--prefix", default="",
                    help="operate on out/<prefix>anchors.jsonl etc; used to run "
                         "the name-holdout chain alongside the real one")
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    pfx = args.prefix
    reporter = common.Reporter("addrlib :: scoring%s" % (" [%s]" % pfx if pfx else ""))
    out = common.out_dir(cfg)

    ref_funcs = load_functions(cfg, "reference")
    tgt_funcs = load_functions(cfg, "target")

    rows = list(common.read_jsonl(os.path.join(out, pfx + "anchors.jsonl")))
    bsim_path = os.path.join(out, pfx + "bsim.jsonl")
    if os.path.exists(bsim_path):
        rows.extend(common.read_jsonl(bsim_path))
    reporter.line("anchors loaded: %d" % len(rows))

    # Keep the strongest claim per reference function, and drop any claim on a
    # target already taken by a stronger one.
    best = {}
    for r in rows:
        cur = best.get(r["ref_rva"])
        if cur is None or STAGE_RANK.get(r["stage"], 0) > STAGE_RANK.get(cur["stage"], 0):
            best[r["ref_rva"]] = r

    taken, kept = {}, {}
    for ref_rva, r in sorted(best.items(),
                             key=lambda kv: -STAGE_RANK.get(kv[1]["stage"], 0)):
        t = r["tgt_rva"]
        if t in taken:
            continue
        taken[t] = ref_rva
        kept[ref_rva] = r
    reporter.line("after conflict resolution: %d" % len(kept))

    mapping = Mapping({k: v["tgt_rva"] for k, v in kept.items()})

    # Overrides are hand-verified and unconditional: they are how a bad entry
    # found in the wild gets corrected without touching anything else.
    overrides = {}
    ov_path = os.path.join(common.tool_dir(), "overrides.csv")
    for row in common.read_csv_rows(ov_path):
        if not row.get("steam_rva"):
            continue
        overrides[common.parse_rva(row["steam_rva"])] = (
            common.parse_rva(row["gog_rva"]), row.get("reason", ""))
    if overrides:
        reporter.line("overrides applied: %d" % len(overrides))

    reasons = defaultdict(int)
    final = []
    for ref_rva, r in kept.items():
        stage = r["stage"]
        if ref_rva in overrides:
            tgt, why = overrides[ref_rva]
            final.append((ref_rva, tgt, common.TIER_EXACT, "override", 1.0, why))
            continue
        if stage in EXACT_STAGES:
            tier = common.TIER_EXACT
        elif guards_pass(ref_funcs.get(ref_rva), tgt_funcs.get(r["tgt_rva"]),
                         mapping, cfg, reasons):
            tier = common.TIER_NEAR_EXACT
        else:
            tier = common.TIER_REVIEW
        final.append((ref_rva, r["tgt_rva"], tier, stage, r["conf"],
                      r.get("evidence", "")))

    for ref_rva, (tgt, why) in overrides.items():
        if ref_rva not in kept:
            final.append((ref_rva, tgt, common.TIER_EXACT, "override", 1.0, why))

    final.sort()
    common.write_csv_rows(
        os.path.join(out, pfx + "mapping.csv"),
        ["ref_rva", "tgt_rva", "tier", "stage", "conf", "evidence"],
        [{"ref_rva": common.fmt_rva(a), "tgt_rva": common.fmt_rva(b),
          "tier": t, "stage": s, "conf": c, "evidence": e}
         for a, b, t, s, c, e in final])

    reporter.section("tiers")
    counts = defaultdict(int)
    per_stage = defaultdict(lambda: defaultdict(int))
    for _, _, t, s, _, _ in final:
        counts[t] += 1
        per_stage[s][t] += 1
    for t in (common.TIER_EXACT, common.TIER_NEAR_EXACT, common.TIER_REVIEW):
        reporter.line("    %-12s %d" % (t, counts[t]))
    reporter.line("    shipping     %d of %d reference functions"
                  % (counts[common.TIER_EXACT] + counts[common.TIER_NEAR_EXACT],
                     len(ref_funcs)))

    reporter.section("per stage")
    for s in sorted(per_stage, key=lambda s: -sum(per_stage[s].values())):
        d = per_stage[s]
        reporter.line("    %-16s exact=%-6d near=%-6d review=%d"
                      % (s, d[common.TIER_EXACT], d[common.TIER_NEAR_EXACT],
                         d[common.TIER_REVIEW]))

    if reasons:
        reporter.section("guard rejections")
        for k in sorted(reasons, key=lambda k: -reasons[k]):
            reporter.line("    %-24s %d" % (k, reasons[k]))

    reporter.line("")
    reporter.line("    -> out/%smapping.csv" % pfx)
    reporter.save(os.path.join(out, pfx + "score_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
