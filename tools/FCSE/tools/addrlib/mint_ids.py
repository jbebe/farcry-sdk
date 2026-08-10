"""Stage E: assign stable numeric IDs to shipping entries, append-only.

The ID is the plugin ABI. A plugin compiled today against ID 4711 must resolve
the same function after any future regeneration, so this file is append-only and
never renumbered: an existing (id, steam_rva) pair changing is treated as a bug
and aborts the run rather than being written out.

IDs are dense from 0, which lets the emitted table be a flat array indexed by ID
-- no search, no hash, one bounds check at runtime. Density survives appending
because new IDs only ever go on the end.

An entry that stops shipping keeps its ID forever and simply resolves to
"absent" for the builds that lost it. Reusing the ID would silently repoint
every plugin that ever baked it.

    python mint_ids.py [--config addrlib.toml]
"""

import argparse
import os
import re
import sys

import common
from check_symbols import collect_symbols

REGISTRY = "registry.csv"
FIELDS = ["id", "steam_rva", "kind", "name", "notes"]


def load_shipping(cfg, out):
    """Everything eligible for an ID: matched functions plus mapped globals.

    Data addresses need IDs as much as functions do -- 6 of the 27 addresses
    FCSE bakes in are globals or a vtable, and a resolver that only knew about
    functions would leave those call sites hardcoded and build-specific.
    """
    shipping = {}
    for r in common.read_csv_rows(os.path.join(out, "mapping.csv")):
        if r["tier"] in common.SHIPPING_TIERS:
            shipping[common.parse_rva(r["ref_rva"])] = "func"
    data_path = os.path.join(out, "data_map.jsonl")
    if os.path.exists(data_path):
        for r in common.read_jsonl(data_path):
            shipping.setdefault(r["ref_rva"], "data")
    return shipping


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: id registry")
    reg_path = os.path.join(common.tool_dir(), REGISTRY)

    # ---- what ships now --------------------------------------------------
    shipping = load_shipping(cfg, out)
    n_func = sum(1 for v in shipping.values() if v == "func")
    reporter.line("shipping entries: %d (%d functions, %d data)"
                  % (len(shipping), n_func, len(shipping) - n_func))

    # ---- existing registry ----------------------------------------------
    existing_rows = common.read_csv_rows(reg_path)
    by_rva, by_id = {}, {}
    for row in existing_rows:
        rid = int(row["id"])
        rva = common.parse_rva(row["steam_rva"])
        by_rva[rva] = rid
        by_id[rid] = rva
    reporter.line("existing registry entries    : %d" % len(existing_rows))

    if by_id:
        expected = set(range(len(by_id)))
        if set(by_id) != expected:
            raise SystemExit(
                "[!] registry IDs are not dense 0..%d - the emitted table indexes "
                "by ID, so a gap would silently shift every entry after it.\n"
                "    Fix %s before regenerating." % (len(by_id) - 1, reg_path))

    # ---- names for the curated symbols ----------------------------------
    names = {}
    try:
        for s in collect_symbols(os.path.join(common.repo_root(), "tools", "FCSE", "src")):
            names[s["rva"]] = s["name"]
    except Exception:
        pass

    # ---- append ----------------------------------------------------------
    new_rvas = sorted(r for r in shipping if r not in by_rva)
    next_id = len(by_id)
    appended = []
    for rva in new_rvas:
        appended.append({"id": next_id, "steam_rva": common.fmt_rva(rva),
                         "kind": shipping[rva], "name": names.get(rva, ""),
                         "notes": ""})
        by_rva[rva] = next_id
        next_id += 1

    reporter.line("new IDs to append            : %d" % len(appended))
    if appended:
        reporter.line("    id range %d..%d" % (appended[0]["id"], appended[-1]["id"]))

    # ---- invariant check -------------------------------------------------
    for row in existing_rows:
        rid, rva = int(row["id"]), common.parse_rva(row["steam_rva"])
        if by_rva.get(rva) != rid:
            raise SystemExit("[!] ABI violation: id %d no longer maps to %s"
                             % (rid, common.fmt_rva(rva)))

    dropped = [r for r in by_rva if r not in shipping]
    if dropped:
        reporter.line("entries that no longer ship  : %d (IDs retained, resolve "
                      "to absent)" % len(dropped))

    # Backfill names discovered since the row was first written; the name is
    # documentation, not identity, so this cannot break the ABI.
    renamed = 0
    for row in existing_rows:
        rva = common.parse_rva(row["steam_rva"])
        want = names.get(rva)
        if want and row.get("name") != want:
            row["name"] = want
            renamed += 1
    if renamed:
        reporter.line("names backfilled             : %d" % renamed)

    if args.dry_run:
        reporter.line("")
        reporter.line("    dry run - registry not written")
    else:
        rows = existing_rows + appended
        rows.sort(key=lambda r: int(r["id"]))
        common.write_csv_rows(reg_path, FIELDS, rows)
        reporter.line("")
        reporter.line("    -> %s (%d entries)" % (REGISTRY, len(rows)))

    named = sum(1 for r in (existing_rows + appended) if r.get("name"))
    reporter.line("    named entries: %d" % named)
    reporter.save(os.path.join(out, "registry_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
