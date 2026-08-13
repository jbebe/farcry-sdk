"""Stage B4: map global data addresses, using the function mapping as leverage.

Globals, vtables and string tables are not functions, so nothing in the function
pipeline can reach them -- yet 6 of the 27 addresses FCSE depends on are exactly
that. They are reachable indirectly: once two functions are known to be the same
function, the data they touch, in the order they touch it, corresponds too.

For every matched function pair whose data-reference lists have the same length,
each position casts a vote for a (reference address, target address) pairing.
A pairing is accepted when every function that saw it agrees, enough distinct
functions voted, and no other reference address wants the same target. One
function agreeing with itself proves nothing; several unrelated functions
independently touching the same two addresses in the same slot is strong.

Runs offline against the extraction cache -- the data reference lists were
recorded by ghidra_extract.py.

    python match_data.py [--config addrlib.toml]
"""

import argparse
import os
import sys
from collections import defaultdict

import common


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--prefix", default="")
    ap.add_argument("--min-votes", type=int, default=None,
                    help="override [data].min_votes for a calibration run")
    ap.add_argument("--out-prefix", default=None,
                    help="write results under a different prefix than the one "
                         "read, so a calibration run cannot clobber the real one")
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    pfx = args.prefix
    opfx = args.out_prefix if args.out_prefix is not None else pfx
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: data mapping%s"
                               % (" [%s]" % pfx if pfx else ""))

    ref_funcs = common.load_functions(cfg, "reference")
    tgt_funcs = common.load_functions(cfg, "target")

    mapping = {}
    tier_of = {}
    for r in common.read_csv_rows(os.path.join(out, pfx + "mapping.csv")):
        if r["tier"] in common.SHIPPING_TIERS:
            a = common.parse_rva(r["ref_rva"])
            mapping[a] = common.parse_rva(r["tgt_rva"])
            tier_of[a] = r["tier"]
    reporter.line("function pairs available: %d" % len(mapping))

    dcfg = cfg.get("data", {})
    min_votes = args.min_votes if args.min_votes is not None \
        else dcfg.get("min_votes", 2)
    exact_only = dcfg.get("exact_functions_only", True)
    min_data_rva = dcfg.get("min_data_rva", 0x1000)
    reporter.line("min_votes=%d exact_functions_only=%s min_data_rva=0x%X"
                  % (min_votes, exact_only, min_data_rva))

    votes = defaultdict(lambda: defaultdict(int))   # ref_data -> tgt_data -> votes
    voters = defaultdict(set)                        # ref_data -> {function pairs}
    used_pairs = skipped = 0

    for ref_rva, tgt_rva in mapping.items():
        # Voting from an inferred function pair would let one uncertain match
        # seed a whole family of uncertain data addresses.
        if exact_only and tier_of.get(ref_rva) != common.TIER_EXACT:
            continue
        rf, tf = ref_funcs.get(ref_rva), tgt_funcs.get(tgt_rva)
        if rf is None or tf is None:
            continue
        rd, td = rf["data"], tf["data"]
        if not rd or len(rd) != len(td):
            skipped += 1
            continue
        used_pairs += 1
        for a, b in zip(rd, td):
            # Out-of-image references are kept in the list to preserve slot
            # alignment, but they are tokens rather than addresses and there is
            # nothing to map.
            if isinstance(a, int) and isinstance(b, int) \
                    and a >= min_data_rva and b >= min_data_rva:
                votes[a][b] += 1
                voters[a].add(ref_rva)

    reporter.line("functions voting        : %d (skipped %d with unequal ref counts)"
                  % (used_pairs, skipped))
    reporter.line("distinct data addresses : %d" % len(votes))

    # String literals are ground truth for data addresses, so use them to *reject*
    # before they are used to audit. The failure this catches is real and was
    # measured: a weakly-witnessed pairing put an interior pointer into a
    # character table opposite the table's start. Both held strings, and the
    # strings disagreed, which settles it with no judgement call.
    cache = common.cache_dir(cfg, create=False)
    strings = {}
    for key, side in (("reference", "ref"), ("target", "tgt")):
        spath = os.path.join(cache, "%s.strings.jsonl" % cfg["builds"][key]["id"])
        strings[side] = ({r["rva"]: r["s"] for r in common.read_jsonl(spath)}
                         if os.path.exists(spath) else {})
    if not strings["ref"] or not strings["tgt"]:
        reporter.line("[!] no string tables in cache - re-run ghidra_extract.py to "
                      "enable the string consistency filter")

    # Accept only unanimous, well-witnessed, non-conflicting pairings.
    proposals = {}
    stats = defaultdict(int)
    for a, options in votes.items():
        if len(options) != 1:
            stats["disagreement"] += 1
            continue
        b = next(iter(options))
        if len(voters[a]) < min_votes:
            stats["too_few_voters"] += 1
            continue
        sa, sb = strings["ref"].get(a), strings["tgt"].get(b)
        if sa is not None and sb is not None and sa != sb:
            stats["string_contradiction"] += 1
            continue
        proposals[a] = (b, len(voters[a]))

    claimed = defaultdict(list)
    for a, (b, n) in proposals.items():
        claimed[b].append(a)
    rows = []
    for a, (b, n) in sorted(proposals.items()):
        if len(claimed[b]) != 1:
            stats["target_contested"] += 1
            continue
        rows.append({"ref_rva": a, "tgt_rva": b, "stage": "data",
                     "conf": 0.99, "evidence": "voters=%d" % n})
        stats["accepted"] += 1

    reporter.section("outcome")
    for k in sorted(stats, key=lambda k: -stats[k]):
        reporter.line("    %-20s %d" % (k, stats[k]))

    common.write_jsonl(os.path.join(out, opfx + "data_map.jsonl"), rows)
    reporter.line("")
    reporter.line("    -> out/%sdata_map.jsonl (%d data addresses)" % (opfx, len(rows)))
    reporter.save(os.path.join(out, opfx + "data_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
