"""Report how the addresses FCSE itself depends on fare in the mapping.

Coverage over 89k anonymous addresses is a statistic; whether
`kGamePageCtor` resolves is the question that decides if FCSE runs on GOG.
This checks every name in names.csv against the generated mapping, so a
regression in the addresses that matter is visible immediately rather than
buried in an aggregate.

    python check_symbols.py [--config addrlib.toml]
"""

import argparse
import os
import sys

import common


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: FCSE symbol coverage")

    names = common.read_csv_rows(os.path.join(common.tool_dir(), "names.csv"))
    if not names:
        raise SystemExit("[!] names.csv is empty - nothing to check")

    mapping = {}
    for r in common.read_csv_rows(os.path.join(out, "mapping.csv")):
        mapping[common.parse_rva(r["ref_rva"])] = (
            common.parse_rva(r["tgt_rva"]), r["tier"], r["stage"])

    data_map = {}
    data_path = os.path.join(out, "data_map.jsonl")
    if os.path.exists(data_path):
        for r in common.read_jsonl(data_path):
            data_map[r["ref_rva"]] = (r["tgt_rva"], r["evidence"])

    reporter.line("named addresses: %d (from names.csv)" % len(names))
    reporter.line("")
    reporter.line("  %-28s %-11s %-11s %-11s %s"
                  % ("name", "uplay rva", "retail rva", "tier", "stage"))
    reporter.line("  " + "-" * 84)

    resolved = via_data = unresolved = 0
    missing = []
    for row in sorted(names, key=lambda r: r["name"]):
        rva = common.parse_rva(row["steam_rva"])
        entry = mapping.get(rva)
        data_entry = data_map.get(rva)
        if entry:
            tgt, tier, stage = entry
            resolved += 1
            reporter.line("  %-28s %-11s %-11s %-11s %s"
                          % (row["name"], common.fmt_rva(rva), common.fmt_rva(tgt),
                             tier, stage))
        elif data_entry:
            tgt, evidence = data_entry
            via_data += 1
            reporter.line("  %-28s %-11s %-11s %-11s data(%s)"
                          % (row["name"], common.fmt_rva(rva), common.fmt_rva(tgt),
                             "near_exact", evidence))
        else:
            unresolved += 1
            missing.append(row)
            reporter.line("  %-28s %-11s %-11s %-11s %s"
                          % (row["name"], common.fmt_rva(rva), "MISSING", "-",
                             row.get("notes", "")))

    reporter.section("summary")
    reporter.line("    resolved by the function mapping : %d" % resolved)
    reporter.line("    resolved by the data mapping     : %d" % via_data)
    reporter.line("    UNRESOLVED                       : %d" % unresolved)
    if missing:
        reporter.line("")
        reporter.line("    An unresolved address resolves to 0 at runtime, which disables")
        reporter.line("    whatever feature needs it. Either grow the mapping, or work the")
        reporter.line("    address out and add a hand-verified line to overrides.csv:")
        reporter.line("")
        for row in missing:
            reporter.line("      python resolve_address.py %s" % row["steam_rva"])

    reporter.save(os.path.join(out, "fcse_symbols_report.txt"))
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
