"""Stage D: measure the mapping against ground truth, and gate on the result.

Ground truth is the PE export directory of the two DLLs, read with pefile. It
owes nothing to Ghidra and nothing to this pipeline: an export name present in
both builds names the same function by definition, so the (reference RVA,
target RVA) pair it implies is a fact, not an inference.

Used two ways:

  python validate.py                       # audit the shipping mapping
  python match.py --holdout-names          # rebuild without name-based stages
  python validate.py --anchors out/holdout_anchors.jsonl

The second form is the one that actually measures precision. In the normal run
the export stage produces those pairs itself, so scoring it against exports is
circular and always says 100%. Holding names out makes the export table a real
test set for the hash, string and propagation stages -- the ones whose accuracy
is genuinely unknown.

Exit code is non-zero if a shipping tier fails its threshold, so this can gate
a release.
"""

import argparse
import os
import sys
from collections import defaultdict

import pefile

import common


def export_pairs(cfg, reporter):
    """name -> (reference_rva, target_rva) for every export shared by both builds."""
    sides = {}
    for key in ("reference", "target"):
        spec = cfg["builds"][key]
        path = spec.get("dll")
        if not path:
            raise SystemExit("[!] builds.%s.dll is not set in the config" % key)
        if not os.path.isabs(path):
            path = os.path.join(common.repo_root(), path)
        if not os.path.exists(path):
            raise SystemExit("[!] %s not found (builds.%s.dll)" % (path, key))
        pe = pefile.PE(path, fast_load=True)
        pe.parse_data_directories(directories=[
            pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"]])
        table = {}
        for sym in pe.DIRECTORY_ENTRY_EXPORT.symbols:
            if sym.name:
                table[sym.name.decode("utf-8", "replace")] = sym.address
        pe.close()
        sides[key] = table
        reporter.line("    %-10s %-46s %d exports"
                      % (key, os.path.basename(path), len(table)))

    shared = set(sides["reference"]) & set(sides["target"])
    pairs = {n: (sides["reference"][n], sides["target"][n]) for n in shared}
    reporter.line("    shared export names: %d" % len(pairs))
    return pairs


def validate_data_map(cfg, path, reporter):
    """Independent check on the data mapping, using string literals as truth.

    The data mapping is inferred from agreement between functions, so it needs a
    check that owes it nothing. String literals provide one: if a pairing says
    reference address A corresponds to target address B, and both addresses hold
    a string, then the two strings must be identical. A mismatch is a definite
    error, not a judgement call -- and the check covers a large, arbitrary
    sample of the map rather than a hand-picked few.
    """
    if not os.path.exists(path):
        reporter.line("    (no data map to check)")
        return {"checked": 0, "wrong": 0, "right": 0}

    cache = common.cache_dir(cfg, create=False)
    strings = {}
    for key in ("reference", "target"):
        bid = cfg["builds"][key]["id"]
        spath = os.path.join(cache, "%s.strings.jsonl" % bid)
        if not os.path.exists(spath):
            reporter.line("    (no string table for %s - re-run ghidra_extract.py)" % bid)
            return {"checked": 0, "wrong": 0, "right": 0}
        strings[key] = {r["rva"]: r["s"] for r in common.read_jsonl(spath)}

    right = wrong = 0
    examples = []
    for r in common.read_jsonl(path):
        a = strings["reference"].get(r["ref_rva"])
        b = strings["target"].get(r["tgt_rva"])
        if a is None or b is None:
            continue
        if a == b:
            right += 1
        else:
            wrong += 1
            if len(examples) < 20:
                examples.append((r["ref_rva"], r["tgt_rva"], a, b))

    checked = right + wrong
    reporter.line("    pairs where both sides hold a string : %d" % checked)
    reporter.line("    identical literal                    : %d" % right)
    reporter.line("    MISMATCHED literal                   : %d" % wrong)
    if checked:
        reporter.line("    agreement                            : %.4f%%"
                      % (100.0 * right / checked))
    for a, b, sa, sb in examples:
        reporter.line("      %s -> %s : %r vs %r"
                      % (common.fmt_rva(a), common.fmt_rva(b), sa[:40], sb[:40]))
    return {"checked": checked, "wrong": wrong, "right": right}


def load_mapping(path):
    rows = common.read_csv_rows(path)
    out = {}
    for r in rows:
        out[common.parse_rva(r["ref_rva"])] = (
            common.parse_rva(r["tgt_rva"]), r["tier"], r["stage"])
    return out


def load_anchors(path):
    """Score raw anchors when a scored mapping.csv is not what we want to audit."""
    out = {}
    for r in common.read_jsonl(path):
        cur = out.get(r["ref_rva"])
        if cur is None or r["conf"] > cur[3]:
            out[r["ref_rva"]] = (r["tgt_rva"], "-", r["stage"], r["conf"])
    return {k: (v[0], v[1], v[2]) for k, v in out.items()}


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--mapping", default=None, help="default out/mapping.csv")
    ap.add_argument("--anchors", default=None,
                    help="score an anchors.jsonl instead of a scored mapping")
    ap.add_argument("--no-gate", action="store_true",
                    help="report only; never exit non-zero")
    ap.add_argument("--data-map", default=None,
                    help="data map filename under out/ (default data_map.jsonl)")
    ap.add_argument("--precision-only", action="store_true",
                    help="gate on wrong answers but not on coverage. Correct for "
                         "a holdout run, where the export stage is switched off "
                         "on purpose and low export coverage is the expected "
                         "outcome rather than a regression")
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: validation")

    reporter.section("ground truth (PE export directories)")
    truth = export_pairs(cfg, reporter)

    if args.anchors:
        mapping = load_anchors(args.anchors)
        label = args.anchors
    else:
        path = args.mapping or os.path.join(out, "mapping.csv")
        mapping = load_mapping(path)
        label = path
    reporter.line("")
    reporter.line("    scoring: %s (%d entries)" % (label, len(mapping)))

    # A reference RVA can carry several export names when the linker folded
    # identical bodies; the pair is correct if it agrees with any of them.
    accept_targets = defaultdict(set)
    for name, (r, t) in truth.items():
        accept_targets[r].add(t)

    per_tier = defaultdict(lambda: {"right": 0, "wrong": 0, "absent": 0})
    per_stage = defaultdict(lambda: {"right": 0, "wrong": 0})
    wrong_rows = []

    for ref_rva, targets in accept_targets.items():
        entry = mapping.get(ref_rva)
        if entry is None:
            per_tier["(unmapped)"]["absent"] += 1
            continue
        tgt, tier, stage = entry
        ok = tgt in targets
        per_tier[tier]["right" if ok else "wrong"] += 1
        per_stage[stage]["right" if ok else "wrong"] += 1
        if not ok:
            wrong_rows.append((ref_rva, tgt, sorted(targets), tier, stage))

    reporter.section("accuracy on export ground truth")
    total_truth = len(accept_targets)
    covered = total_truth - per_tier["(unmapped)"]["absent"]
    reporter.line("    export addresses in truth set : %d" % total_truth)
    reporter.line("    of those, present in mapping  : %d (%.1f%%)"
                  % (covered, 100.0 * covered / max(total_truth, 1)))
    reporter.line("")
    reporter.line("    %-14s %8s %8s" % ("tier", "correct", "WRONG"))
    for tier in sorted(per_tier):
        if tier == "(unmapped)":
            continue
        d = per_tier[tier]
        reporter.line("    %-14s %8d %8d" % (tier, d["right"], d["wrong"]))

    reporter.section("accuracy per stage")
    reporter.line("    %-16s %8s %8s" % ("stage", "correct", "WRONG"))
    for stage in sorted(per_stage, key=lambda s: -per_stage[s]["right"]):
        d = per_stage[stage]
        flag = "   <-- " if d["wrong"] else ""
        reporter.line("    %-16s %8d %8d%s" % (stage, d["right"], d["wrong"], flag))

    if wrong_rows:
        reporter.section("wrong pairings (first 40)")
        for ref_rva, got, want, tier, stage in wrong_rows[:40]:
            reporter.line("    ref=%s got=%s want=%s  tier=%s stage=%s"
                          % (common.fmt_rva(ref_rva), common.fmt_rva(got),
                             ",".join(common.fmt_rva(w) for w in want), tier, stage))

    reporter.section("data mapping vs string literals (independent)")
    data_stats = validate_data_map(
        cfg, os.path.join(out, args.data_map or "data_map.jsonl"), reporter)

    # ---- gate ------------------------------------------------------------
    v = cfg["validate"]
    failures = []
    if data_stats["wrong"] > v.get("max_wrong_data", 0):
        failures.append("data mapping has %d mismatched string literals (max %d)"
                        % (data_stats["wrong"], v.get("max_wrong_data", 0)))
    if per_tier[common.TIER_EXACT]["wrong"] > v["max_wrong_exact"]:
        failures.append("tier exact has %d wrong (max %d)"
                        % (per_tier[common.TIER_EXACT]["wrong"], v["max_wrong_exact"]))
    if per_tier[common.TIER_NEAR_EXACT]["wrong"] > v["max_wrong_near_exact"]:
        failures.append("tier near_exact has %d wrong (max %d)"
                        % (per_tier[common.TIER_NEAR_EXACT]["wrong"],
                           v["max_wrong_near_exact"]))
    coverage = covered / max(total_truth, 1)
    if not args.precision_only and coverage < v["min_export_coverage"]:
        failures.append("export coverage %.3f below minimum %.3f"
                        % (coverage, v["min_export_coverage"]))

    reporter.section("gate")
    if failures:
        for f in failures:
            reporter.line("    FAIL: %s" % f)
    else:
        reporter.line("    PASS")

    reporter.save(os.path.join(out, "validation_report.txt"))
    common.write_json(os.path.join(out, "validation.json"), {
        "truth_addresses": total_truth,
        "covered": covered,
        "coverage": round(coverage, 4),
        "per_tier": {k: dict(v_) for k, v_ in per_tier.items()},
        "per_stage": {k: dict(v_) for k, v_ in per_stage.items()},
        "wrong": [{"ref": common.fmt_rva(a), "got": common.fmt_rva(b),
                   "want": [common.fmt_rva(w) for w in c], "tier": d, "stage": e}
                  for a, b, c, d, e in wrong_rows],
        "passed": not failures,
    })
    return 1 if (failures and not args.no_gate) else 0


if __name__ == "__main__":
    sys.exit(main())
