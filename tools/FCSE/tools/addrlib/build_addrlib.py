"""Build the FCSE address library end to end.

    python build_addrlib.py                 # full run
    python build_addrlib.py --from match    # reuse the Ghidra cache
    python build_addrlib.py --calibrate     # also run the name-holdout chain

Stages, in order:

    extract     Ghidra: function hashes, call graph, data/string references
    match       exact-hash joins + call-graph propagation
    score       tier assignment and structural guards
    candidates  narrow candidate sets for the residue
    bsim        Ghidra: decompiler-based similarity on those candidates
    score       re-run, now including BSim anchors
    data        map globals and vtables via the functions that touch them
    validate    score against export and string-literal ground truth  [GATE]
    mint        assign append-only numeric IDs
    emit        write the C++ table and the manifest

Only `extract` and `bsim` need Ghidra, and both refuse to run while the GUI
holds the project lock. Everything else is offline, so re-running with a
different accepted-match bar takes seconds.
"""

import argparse
import os
import subprocess
import sys
import time

import common

HERE = os.path.dirname(os.path.abspath(__file__))

STAGES = ["extract", "match", "score", "candidates", "bsim", "rescore",
          "data", "validate", "mint", "emit"]

COMMANDS = {
    "extract":    ["ghidra_extract.py"],
    "match":      ["match.py"],
    "score":      ["score.py"],
    "candidates": ["candidates.py"],
    "bsim":       ["ghidra_bsim.py"],
    "rescore":    ["score.py"],
    "data":       ["match_data.py"],
    "validate":   ["validate.py"],
    "mint":       ["mint_ids.py"],
    "emit":       ["emit.py"],
}


def run(stage, extra=(), config=None):
    cmd = [sys.executable, "-u", os.path.join(HERE, *COMMANDS[stage])] + list(extra)
    if config:
        cmd += ["--config", config]
    print("\n" + "=" * 72)
    print("[%s] %s" % (stage, " ".join(os.path.basename(c) for c in cmd[2:])))
    print("=" * 72, flush=True)
    started = time.time()
    result = subprocess.run(cmd, cwd=HERE)
    print("[%s] exit=%d in %.0fs" % (stage, result.returncode, time.time() - started),
          flush=True)
    return result.returncode


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--from", dest="start", choices=STAGES, default="extract",
                    help="resume from a stage, reusing earlier outputs")
    ap.add_argument("--only", choices=STAGES, default=None)
    ap.add_argument("--calibrate", action="store_true",
                    help="also run the name-holdout chain and report the honest "
                         "precision of the non-name stages")
    ap.add_argument("--keep-going", action="store_true",
                    help="do not stop when the validation gate fails")
    args = ap.parse_args()

    order = [args.only] if args.only else STAGES[STAGES.index(args.start):]

    for stage in order:
        rc = run(stage, config=args.config)
        if rc != 0:
            if stage == "validate" and args.keep_going:
                print("\n[!] validation gate FAILED - continuing because "
                      "--keep-going was given. Do not ship this table.")
                continue
            print("\n[!] stage '%s' failed (exit %d); stopping." % (stage, rc))
            return rc

    if args.calibrate:
        print("\n" + "#" * 72)
        print("# calibration: rebuild without name-based stages, so the export")
        print("# table becomes an independent test set for everything else")
        print("#" * 72, flush=True)
        for stage, extra in (("match", ["--holdout-names"]),
                             ("score", ["--prefix", "holdout_"]),
                             ("candidates", ["--prefix", "holdout_"]),
                             ("bsim", ["--prefix", "holdout_"]),
                             ("rescore", ["--prefix", "holdout_"]),
                             ("data", ["--prefix", "holdout_"])):
            rc = run(stage, extra, config=args.config)
            if rc != 0:
                return rc
        rc = run("validate", ["--mapping", "out/holdout_mapping.csv",
                              "--data-map", "holdout_data_map.jsonl",
                              "--precision-only"], config=args.config)
        if rc != 0:
            print("\n[!] calibration gate FAILED - a shipping tier produced a "
                  "wrong answer on ground truth. Tighten thresholds in "
                  "addrlib.toml before shipping.")
            return rc

    cfg = common.load_config(args.config)
    print("\n" + "=" * 72)
    print("done. reports in %s" % common.out_dir(cfg, create=False))
    print("=" * 72)
    return 0


if __name__ == "__main__":
    sys.exit(main())
