"""Report how the addresses FCSE actually depends on fare in the mapping.

Coverage over 60k anonymous functions is a statistic; whether
`kGamePageCtorRva` resolves is the question that decides if FCSE runs on GOG.
This scans the FCSE sources for the addresses they bake in and reports each
one's tier, so a regression in the symbols that matter is visible immediately
rather than buried in an aggregate.

    python check_symbols.py [--config addrlib.toml]
"""

import argparse
import os
import re
import sys

import common

DECL = re.compile(r"constexpr\s+uintptr_t\s+(k\w+Rva)\s*=\s*(0x[0-9A-Fa-f]+)\s*;")

# Not functions, so the function-level mapping cannot contain them. They are
# resolved from the code that references them; listed here so the report says
# "expected, handled elsewhere" rather than silently showing 6 failures.
NON_FUNCTION = {
    "kPageVtableRva": ".rdata vtable - via the ctor that installs it",
    "kEmptyStringProxyRva": ".data - via referencing code",
    "kEngineSingletonRva": ".data - via referencing code",
    "kTimeBlockRva": ".data - via referencing code",
    "kYesTextGlobalRva": ".data - via referencing code",
    "kNoTextGlobalRva": ".data - via referencing code",
}


def collect_symbols(src_dir):
    out = []
    for root, _, files in os.walk(src_dir):
        for f in sorted(files):
            if not f.endswith((".cpp", ".h", ".hpp")):
                continue
            path = os.path.join(root, f)
            text = open(path, encoding="utf-8", errors="replace").read()
            for m in DECL.finditer(text):
                out.append({
                    "name": m.group(1),
                    "rva": common.parse_rva(m.group(2)),
                    "file": os.path.relpath(path, src_dir).replace("\\", "/"),
                    "line": text[:m.start()].count("\n") + 1,
                })
    return sorted(out, key=lambda s: s["rva"])


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--src", default=None, help="default tools/FCSE/src")
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: FCSE symbol coverage")

    src = args.src or os.path.join(common.repo_root(), "tools", "FCSE", "src")
    symbols = collect_symbols(src)

    mapping = {}
    for r in common.read_csv_rows(os.path.join(out, "mapping.csv")):
        mapping[common.parse_rva(r["ref_rva"])] = (
            common.parse_rva(r["tgt_rva"]), r["tier"], r["stage"])

    # Globals and vtables live in a separate map, produced by match_data.py from
    # the agreement of the functions that reference them.
    data_map = {}
    data_path = os.path.join(out, "data_map.jsonl")
    if os.path.exists(data_path):
        for r in common.read_jsonl(data_path):
            data_map[r["ref_rva"]] = (r["tgt_rva"], r["evidence"])

    reporter.line("FCSE sources: %s" % src)
    reporter.line("baked addresses: %d" % len(symbols))
    reporter.line("")
    reporter.line("  %-28s %-11s %-11s %-11s %-14s %s"
                  % ("symbol", "steam rva", "gog rva", "tier", "stage", "source"))
    reporter.line("  " + "-" * 104)

    resolved = via_data = unresolved = 0
    missing = []
    for s in symbols:
        entry = mapping.get(s["rva"])
        data_entry = data_map.get(s["rva"])
        if entry:
            tgt, tier, stage = entry
            resolved += 1
            reporter.line("  %-28s %-11s %-11s %-11s %-14s %s:%d"
                          % (s["name"], common.fmt_rva(s["rva"]),
                             common.fmt_rva(tgt), tier, stage,
                             s["file"], s["line"]))
        elif data_entry:
            tgt, evidence = data_entry
            via_data += 1
            reporter.line("  %-28s %-11s %-11s %-11s %-14s %s:%d"
                          % (s["name"], common.fmt_rva(s["rva"]),
                             common.fmt_rva(tgt), "near_exact",
                             "data(%s)" % evidence, s["file"], s["line"]))
        else:
            unresolved += 1
            missing.append(s)
            note = NON_FUNCTION.get(s["name"], "")
            reporter.line("  %-28s %-11s %-11s %-11s %-14s %s"
                          % (s["name"], common.fmt_rva(s["rva"]), "MISSING",
                             "-", "-", note or ("%s:%d" % (s["file"], s["line"]))))

    reporter.section("summary")
    reporter.line("    resolved by the function mapping : %d" % resolved)
    reporter.line("    resolved by the data mapping     : %d" % via_data)
    reporter.line("    UNRESOLVED                       : %d" % unresolved)
    if missing:
        reporter.line("")
        reporter.line("    Unresolved function symbols block the port of their feature.")
        reporter.line("    Either grow the mapping, or add a hand-verified line to")
        reporter.line("    overrides.csv for each:")
        for s in missing:
            reporter.line("      %s,<gog_rva>,\"%s hand-verified\"" %
                          (common.fmt_rva(s["rva"]), s["name"]))

    reporter.save(os.path.join(out, "fcse_symbols_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
