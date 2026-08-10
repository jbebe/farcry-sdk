"""Stage B: turn the extraction cache into a reference -> target address mapping.

Runs entirely offline against cache/*.jsonl, so widening the accepted-match bar
costs seconds rather than another Ghidra pass. Every binary fact this consumes
was produced by a shipped Ghidra API in ghidra_extract.py; what happens here is
joining and graph alignment, not disassembly.

Stages run in descending order of certainty, each seeded by everything already
confirmed:

  export            export table names, present and identical in both builds
  thunk             thunks forwarding to the same import
  exact_bytes       identical function bytes, unique on both sides
  exact_insn        identical instructions with addresses masked, unique
  string_unique     a string literal referenced by exactly one function each side
  symbol            identical non-placeholder symbol name, unique on both sides
  propagate         call-graph alignment from confirmed callers, to a fixed point

    python match.py [--config addrlib.toml]
"""

import argparse
import os
import re
import sys
from collections import defaultdict

import common

PLACEHOLDER = re.compile(
    r"^(FUN_|LAB_|SUB_|sub_|thunk_FUN_|switchD_|caseD_|DAT_|Ordinal_|entry$)")


class Anchor:
    __slots__ = ("ref", "tgt", "stage", "conf", "evidence")

    def __init__(self, ref, tgt, stage, conf, evidence=""):
        self.ref, self.tgt = ref, tgt
        self.stage, self.conf, self.evidence = stage, conf, evidence

    def row(self):
        return {"ref_rva": self.ref, "tgt_rva": self.tgt, "stage": self.stage,
                "conf": round(self.conf, 4), "evidence": self.evidence}


# ---------------------------------------------------------------------------
# loading
# ---------------------------------------------------------------------------
def load_side(cfg, key):
    build_id = cfg["builds"][key]["id"]
    cache = common.cache_dir(cfg, create=False)
    fpath = os.path.join(cache, "%s.functions.jsonl" % build_id)
    epath = os.path.join(cache, "%s.exports.jsonl" % build_id)
    if not os.path.exists(fpath):
        raise SystemExit("[!] missing %s - run ghidra_extract.py first" % fpath)
    funcs = {r["rva"]: r for r in common.read_jsonl(fpath)}
    exports = list(common.read_jsonl(epath))
    return build_id, funcs, exports


# ---------------------------------------------------------------------------
# generic unique-key join
# ---------------------------------------------------------------------------
def unique_join(ref_keys, tgt_keys, stage, conf, matched, rev, note=""):
    """Pair keys that map to exactly one function on each side.

    Non-unique keys are left alone rather than paired arbitrarily; ambiguity is
    propagation's problem to solve with context, not something to coin-flip here.
    """
    out = []
    for key, refs in ref_keys.items():
        if len(refs) != 1:
            continue
        tgts = tgt_keys.get(key)
        if not tgts or len(tgts) != 1:
            continue
        r, t = refs[0], tgts[0]
        if r in matched or t in rev:
            continue
        matched[r] = t
        rev[t] = r
        out.append(Anchor(r, t, stage, conf, note or str(key)[:60]))
    return out


def index_by(funcs, keyfn):
    idx = defaultdict(list)
    for rva, row in funcs.items():
        k = keyfn(row)
        if k is not None:
            idx[k].append(rva)
    return idx


# ---------------------------------------------------------------------------
# call-graph propagation
# ---------------------------------------------------------------------------
def align_callees(ref_callees, tgt_callees, matched, rev):
    """Propose (ref_callee, tgt_callee) pairs from two matched functions' call sites.

    Both sides are reduced to keys in the *target* address space, so a resolved
    pair on either side produces the identical key. Resolved call sites form a
    skeleton of pins; if the two skeletons are not identical in content and
    order, the bodies diverged and nothing is proposed. Guessing here is uniquely
    damaging: an accepted wrong pair becomes a pin for the next hop and compounds.
    """
    # external import name pins on its own; internal callee pins once matched
    ref_keys = [t if isinstance(t, str) else
                (("f", matched[t]) if t in matched else None)
                for t in ref_callees]
    tgt_keys = [t if isinstance(t, str) else
                (("f", t) if t in rev else None)
                for t in tgt_callees]

    ref_pins = [(i, k) for i, k in enumerate(ref_keys) if k is not None]
    tgt_pins = [(i, k) for i, k in enumerate(tgt_keys) if k is not None]
    if len(ref_pins) != len(tgt_pins):
        return []
    if [k for _, k in ref_pins] != [k for _, k in tgt_pins]:
        return []

    proposals = []
    prev_r, prev_t = -1, -1
    for (ri, _), (ti, _) in list(zip(ref_pins, tgt_pins)) + [((len(ref_keys), None),
                                                              (len(tgt_keys), None))]:
        gap_r = [ref_callees[x] for x in range(prev_r + 1, ri) if ref_keys[x] is None]
        gap_t = [tgt_callees[x] for x in range(prev_t + 1, ti) if tgt_keys[x] is None]
        if len(gap_r) == len(gap_t):
            for a, b in zip(gap_r, gap_t):
                if isinstance(a, int) and isinstance(b, int):
                    proposals.append((a, b))
        prev_r, prev_t = ri, ti
    return proposals


def plausible(ref_row, tgt_row, cfg):
    """Cheap structural sanity, applied before a proposal may become a pin."""
    if ref_row is None or tgt_row is None:
        return False
    acc = cfg["accept"]
    a, b = ref_row["size"], tgt_row["size"]
    if max(a, b) > 0 and abs(a - b) / max(a, b) > acc["max_body_size_ratio"]:
        return False
    if acc["require_equal_call_count"] and \
            len(ref_row["callees"]) != len(tgt_row["callees"]):
        return False
    return True


def propagate(ref_funcs, tgt_funcs, matched, rev, cfg, reporter):
    p = cfg["propagate"]
    anchors = []
    for rnd in range(1, p["max_rounds"] + 1):
        proposals = defaultdict(list)      # ref_callee -> [(tgt_callee, caller)]
        for ref_rva, tgt_rva in list(matched.items()):
            rf = ref_funcs.get(ref_rva)
            tf = tgt_funcs.get(tgt_rva)
            if rf is None or tf is None or not rf["callees"]:
                continue
            for a, b in align_callees(rf["callees"], tf["callees"], matched, rev):
                if a in matched or b in rev:
                    continue
                proposals[a].append((b, ref_rva))

        # Accept only mutual, conflict-free, structurally plausible proposals.
        target_votes = defaultdict(set)
        for a, votes in proposals.items():
            for b, _ in votes:
                target_votes[b].add(a)

        accepted = 0
        for a, votes in proposals.items():
            distinct = {b for b, _ in votes}
            if len(distinct) != 1:
                continue                        # callers disagree
            b = next(iter(distinct))
            if len(target_votes[b]) != 1:
                continue                        # two refs want the same target
            if a in matched or b in rev:
                continue
            if not plausible(ref_funcs.get(a), tgt_funcs.get(b), cfg):
                continue
            callers = {c for _, c in votes}
            conf = min(0.999, p["hop_decay"] +
                       p["multi_caller_bonus"] * (len(callers) - 1))
            matched[a] = b
            rev[b] = a
            anchors.append(Anchor(a, b, "propagate", conf,
                                  "callers=%d" % len(callers)))
            accepted += 1

        reporter.line("    round %-2d  proposals=%-7d accepted=%-6d total=%d"
                      % (rnd, len(proposals), accepted, len(matched)))
        if accepted == 0:
            break
    return anchors


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--holdout-names", action="store_true",
                    help="disable the export and symbol stages so the export "
                         "table becomes an independent test set for everything "
                         "else; writes to out/holdout_* instead of out/anchors")
    ap.add_argument("--out-prefix", default=None)
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    prefix = args.out_prefix or ("holdout_" if args.holdout_names else "")
    reporter = common.Reporter("addrlib :: matching%s"
                               % (" (names held out)" if args.holdout_names else ""))

    ref_id, ref_funcs, ref_exports = load_side(cfg, "reference")
    tgt_id, tgt_funcs, tgt_exports = load_side(cfg, "target")
    reporter.line("reference %s: %d functions, %d exports"
                  % (ref_id, len(ref_funcs), len(ref_exports)))
    reporter.line("target    %s: %d functions, %d exports"
                  % (tgt_id, len(tgt_funcs), len(tgt_exports)))

    matched, rev, anchors = {}, {}, []
    stages = cfg["stages"]

    def run(name, ref_idx, tgt_idx, conf):
        found = unique_join(ref_idx, tgt_idx, name, conf, matched, rev)
        anchors.extend(found)
        reporter.line("    %-16s +%-6d total=%d" % (name, len(found), len(matched)))

    reporter.section("anchor stages")

    # Exports: names are string-equal because both builds are MSVC. These are
    # also the ground truth validate.py scores against, so they are recorded
    # separately as well.
    if not args.holdout_names:
        ref_exp_idx = defaultdict(list)
        for e in ref_exports:
            for n in e["names"]:
                if not PLACEHOLDER.match(n):
                    ref_exp_idx[n].append(e["rva"])
        tgt_exp_idx = defaultdict(list)
        for e in tgt_exports:
            for n in e["names"]:
                if not PLACEHOLDER.match(n):
                    tgt_exp_idx[n].append(e["rva"])
        run("export", ref_exp_idx, tgt_exp_idx, 1.0)
    else:
        reporter.line("    export           held out")

    if stages.get("exact_bytes", True):
        run("thunk", index_by(ref_funcs, lambda r: r["thunk_of"]),
            index_by(tgt_funcs, lambda r: r["thunk_of"]), 0.99)
        run("exact_bytes", index_by(ref_funcs, lambda r: r["hb"]),
            index_by(tgt_funcs, lambda r: r["hb"]), 0.99)

    if stages.get("exact_instructions", True):
        run("exact_insn", index_by(ref_funcs, lambda r: r["hi"]),
            index_by(tgt_funcs, lambda r: r["hi"]), 0.98)

    if stages.get("exact_mnemonics", False):
        run("exact_mnem", index_by(ref_funcs, lambda r: r["hm"]),
            index_by(tgt_funcs, lambda r: r["hm"]), 0.90)

    # A string referenced by exactly one function on each side is a strong,
    # completely independent signal - it survives any amount of codegen change.
    ref_str = defaultdict(list)
    for rva, row in ref_funcs.items():
        for s in row["strings"]:
            ref_str[s].append(rva)
    tgt_str = defaultdict(list)
    for rva, row in tgt_funcs.items():
        for s in row["strings"]:
            tgt_str[s].append(rva)
    run("string_unique", ref_str, tgt_str, 0.95)

    # Held out alongside exports: Ghidra names an exported function after its
    # export, so leaving this in would smuggle the answer back into a run whose
    # whole point is not having it.
    if not args.holdout_names:
        run("symbol", index_by(ref_funcs, lambda r: None if PLACEHOLDER.match(r["name"])
                               else r["name"]),
            index_by(tgt_funcs, lambda r: None if PLACEHOLDER.match(r["name"])
                     else r["name"]), 0.97)
    else:
        reporter.line("    symbol           held out")

    if stages.get("propagate", True):
        reporter.section("call-graph propagation")
        anchors.extend(propagate(ref_funcs, tgt_funcs, matched, rev, cfg, reporter))

    reporter.section("result")
    by_stage = defaultdict(int)
    for a in anchors:
        by_stage[a.stage] += 1
    for k in sorted(by_stage, key=lambda k: -by_stage[k]):
        reporter.line("    %-16s %d" % (k, by_stage[k]))
    cov_ref = len(matched) / max(len(ref_funcs), 1)
    reporter.line("    matched %d of %d reference functions (%.1f%%)"
                  % (len(matched), len(ref_funcs), 100 * cov_ref))

    out = common.out_dir(cfg)
    common.write_jsonl(os.path.join(out, prefix + "anchors.jsonl"),
                       (a.row() for a in anchors))

    # Residue drives BSim candidate generation; writing it out keeps that stage
    # independent of this one.
    common.write_jsonl(os.path.join(out, prefix + "residue.jsonl"), [{
        "ref_unmatched": sorted(r for r in ref_funcs if r not in matched),
        "tgt_unmatched": sorted(t for t in tgt_funcs if t not in rev),
    }])
    reporter.line("    -> out/%sanchors.jsonl, out/%sresidue.jsonl" % (prefix, prefix))
    reporter.save(os.path.join(out, prefix + "match_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
