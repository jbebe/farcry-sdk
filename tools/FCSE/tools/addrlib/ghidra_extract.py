"""Stage A: pull everything the matcher needs out of Ghidra, once, into a cache.

This is the only stage that needs a JVM, and the only slow one. Everything it
emits is a *fact about the binary* produced by a shipped Ghidra API -- function
bounds, call graph, data references, string references, and the three function
hashes Ghidra's own exact-match correlators use. No matching decision is made
here; that all happens offline in match.py against this cache, so re-scoring
with a broader bar never re-runs Ghidra.

Read-only: programs are opened without write access and never saved, so running
this cannot disturb the RE work in reverse/ghidra.

    python ghidra_extract.py [--config addrlib.toml] [--only reference|target]

Requires the Ghidra GUI to be closed -- a held project lock blocks headless
access even for read-only work.
"""

import argparse
import os
import sys
import time

import common


# ---------------------------------------------------------------------------
# Ghidra bindings, resolved after the JVM is up
# ---------------------------------------------------------------------------
G = {}


def bind_ghidra():
    from ghidra.app.plugin.match import (ExactBytesFunctionHasher,
                                         ExactInstructionsFunctionHasher,
                                         ExactMnemonicsFunctionHasher)
    from ghidra.base.project import GhidraProject
    from ghidra.util.task import ConsoleTaskMonitor

    G["ExactBytes"] = ExactBytesFunctionHasher.INSTANCE
    G["ExactInstructions"] = ExactInstructionsFunctionHasher.INSTANCE
    G["ExactMnemonics"] = ExactMnemonicsFunctionHasher.INSTANCE
    G["GhidraProject"] = GhidraProject
    G["monitor"] = ConsoleTaskMonitor()


def u64(value):
    """Java longs are signed; store them unsigned so the offline join is exact."""
    return "%016x" % (int(value) & 0xFFFFFFFFFFFFFFFF)


def jvm_running():
    """Whether any Ghidra JVM is alive, to tell a held lock from a stale one."""
    try:
        import subprocess
        out = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq javaw.exe", "/FO", "CSV", "/NH"],
            capture_output=True, text=True, timeout=15).stdout
        return "javaw.exe" in out
    except Exception:
        return True  # unknown: assume held, the safer answer


# ---------------------------------------------------------------------------
# extraction
# ---------------------------------------------------------------------------
def build_string_map(program, reporter):
    """address -> literal, built once per program.

    Resolving a string per data reference would mean a JPype round trip for
    every reference in the binary. One pass over defined data is orders of
    magnitude cheaper and gives the same answer.
    """
    listing = program.getListing()
    base = program.getImageBase().getOffset()
    out = {}
    it = listing.getDefinedData(True)
    n = 0
    while it.hasNext():
        data = it.next()
        n += 1
        try:
            if not data.hasStringValue():
                continue
            value = data.getValue()
            if value is None:
                continue
            text = str(value)
            if len(text) >= 4:
                out[int(data.getAddress().getOffset()) - base] = text
        except Exception:
            continue
    reporter.line("    defined data items scanned: %d, strings kept: %d" % (n, len(out)))
    return out


def build_external_names(program, reporter):
    """External-address -> import name, for turning cross-image calls into pins."""
    st = program.getSymbolTable()
    out = {}
    try:
        it = st.getExternalSymbols()
        while it.hasNext():
            sym = it.next()
            try:
                out[int(sym.getAddress().getOffset())] = str(sym.getName(False))
            except Exception:
                continue
    except Exception:
        pass
    reporter.line("    external symbols: %d" % len(out))
    return out


def addr_token(to_addr, base, ext_names, default_space):
    """In-image address -> int RVA; anything else -> a string token.

    Out-of-image targets must stay in the list rather than be dropped: the
    matcher aligns call and data references *by position*, so removing an entry
    on one side only would shift every later slot. They are also not RVAs -- an
    EXTERNAL-space offset is small, and subtracting the image base from it
    yields a negative number that is not an address at all.
    """
    try:
        if to_addr.isExternalAddress():
            name = ext_names.get(int(to_addr.getOffset()))
            return "!" + (name if name else "ext_%x" % int(to_addr.getOffset()))
    except Exception:
        pass
    try:
        if default_space is not None and to_addr.getAddressSpace() != default_space:
            return "?space"
    except Exception:
        pass
    off = int(to_addr.getOffset())
    if off < base:
        return "?below_base"
    return off - base


def extract_functions(program, cfg, string_map, ext_names, reporter):
    """One row per function: identity, hashes, call graph, data and string refs."""
    fm = program.getFunctionManager()
    ref_mgr = program.getReferenceManager()
    base = program.getImageBase().getOffset()
    default_space = program.getAddressFactory().getDefaultAddressSpace()
    monitor = G["monitor"]
    min_bytes = cfg["extract"]["min_function_bytes"]
    max_callees = cfg["extract"]["max_callees_recorded"]

    total = fm.getFunctionCount()
    reporter.line("    functions reported by Ghidra: %d" % total)

    rows = []
    started = time.time()
    it = fm.getFunctions(True)
    seen = 0
    while it.hasNext():
        func = it.next()
        seen += 1
        if seen % 20000 == 0:
            rate = seen / max(time.time() - started, 1e-6)
            reporter.line("    ... %d/%d functions (%.0f/s)" % (seen, total, rate))

        body = func.getBody()
        size = int(body.getNumAddresses())
        if size < min_bytes:
            continue

        entry = int(func.getEntryPoint().getOffset()) - base

        # Ghidra's own hashers: identical bytes, identical instructions with
        # addresses masked, identical mnemonics. The middle one is what makes a
        # differing call target a non-difference.
        try:
            h_bytes = u64(G["ExactBytes"].hash(func, monitor))
            h_insn = u64(G["ExactInstructions"].hash(func, monitor))
            h_mnem = u64(G["ExactMnemonics"].hash(func, monitor))
        except Exception:
            continue

        # Walking only addresses that *have* references skips the vast majority
        # of instructions and keeps call sites in address order, which is what
        # the propagation stage aligns on.
        #
        # A callee is recorded as an int RVA when it is internal, and as
        # "!<name>" when it leaves the image. External calls are the strongest
        # pins propagation has: a call to CreateFileA is unmistakably the same
        # call in both builds, needing no prior match to establish it.
        callees, data_refs, strings = [], [], []
        src_iter = ref_mgr.getReferenceSourceIterator(body, True)
        while src_iter.hasNext():
            addr = src_iter.next()
            for ref in ref_mgr.getReferencesFrom(addr):
                rtype = ref.getReferenceType()
                to_addr = ref.getToAddress()
                if rtype.isCall():
                    if len(callees) < max_callees:
                        callees.append(addr_token(to_addr, base, ext_names,
                                                  default_space))
                elif rtype.isData():
                    token = addr_token(to_addr, base, ext_names, default_space)
                    if len(data_refs) < max_callees:
                        data_refs.append(token)
                    if isinstance(token, int):
                        text = string_map.get(token)
                        if text is not None:
                            strings.append(text)

        # A thunk's identity is whatever it forwards to, not its own 6 bytes.
        thunk_of = None
        if func.isThunk():
            try:
                thunked = func.getThunkedFunction(True)
                if thunked is not None:
                    thunk_of = addr_token(thunked.getEntryPoint(), base,
                                          ext_names, default_space)
            except Exception:
                pass

        rows.append({
            "rva": entry,
            "size": size,
            "name": str(func.getName(True)),
            "thunk_of": thunk_of,
            "hb": h_bytes,
            "hi": h_insn,
            "hm": h_mnem,
            "callees": callees,
            "data": data_refs,
            "strings": sorted(set(strings))[:64],
        })

    reporter.line("    functions extracted: %d (skipped %d below %d bytes or unhashable)"
                  % (len(rows), seen - len(rows), min_bytes))
    return rows


def extract_exports(program, reporter):
    """The export table -- ground truth for calibration, and free anchors."""
    st = program.getSymbolTable()
    base = program.getImageBase().getOffset()
    rows = []
    it = st.getExternalEntryPointIterator()
    while it.hasNext():
        addr = it.next()
        rva = int(addr.getOffset()) - base
        names = []
        for sym in st.getSymbols(addr):
            try:
                names.append(str(sym.getName(False)))
            except Exception:
                pass
        if names:
            rows.append({"rva": rva, "names": sorted(set(names))})
    reporter.line("    exports: %d" % len(rows))
    return rows


def extract_program(project, spec, cfg, reporter):
    name = spec["program"]
    build_id = spec["id"]
    reporter.section("%s  (%s)" % (build_id, name))

    program = project.openProgram("/", name, True)  # read-only
    try:
        sha = None
        try:
            sha = str(program.getExecutableSHA256())
        except Exception:
            pass
        expected = spec.get("sha256")
        reporter.line("    executable sha256: %s" % sha)
        if expected and sha and sha.lower() != expected.lower():
            raise SystemExit(
                "[!] %s is not the expected binary.\n"
                "    Ghidra program '%s' has sha256 %s\n"
                "    but addrlib.toml expects           %s\n"
                "    Refusing to build a mapping from the wrong build."
                % (build_id, name, sha, expected))

        base = int(program.getImageBase().getOffset())
        reporter.line("    image base: 0x%08X" % base)

        string_map = build_string_map(program, reporter)
        ext_names = build_external_names(program, reporter)
        functions = extract_functions(program, cfg, string_map, ext_names, reporter)
        exports = extract_exports(program, reporter)

        cache = common.cache_dir(cfg)
        common.write_jsonl(os.path.join(cache, "%s.functions.jsonl" % build_id), functions)
        common.write_jsonl(os.path.join(cache, "%s.exports.jsonl" % build_id), exports)
        # Persisted so validate.py can check the data mapping independently: if
        # two addresses are paired and both hold a string literal, the literals
        # must be identical. That is ground truth for data, in the same way the
        # export table is ground truth for functions.
        common.write_jsonl(os.path.join(cache, "%s.strings.jsonl" % build_id),
                           ({"rva": k, "s": v} for k, v in sorted(string_map.items())))
        common.write_json(os.path.join(cache, "%s.meta.json" % build_id), {
            "build_id": build_id,
            "program": name,
            "sha256": sha,
            "image_base": base,
            "function_count": len(functions),
            "export_count": len(exports),
            "string_count": len(string_map),
            "extracted_at": time.strftime("%Y-%m-%dT%H:%M:%S"),
        })
        reporter.line("    -> cache/%s.{functions,exports}.jsonl" % build_id)
    finally:
        project.close(program)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--only", choices=["reference", "target"], default=None)
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    reporter = common.Reporter("addrlib :: ghidra extraction")

    location = common.resolve_project_location(cfg)
    project_name = cfg["project"]["name"]
    # A lock with no JVM behind it is stale - typically left by a run that was
    # killed rather than closed. Say which case this is, because "close Ghidra"
    # is useless advice when Ghidra is already closed.
    lock = os.path.join(location, project_name + ".lock")
    if os.path.exists(lock):
        reporter.line("[!] project lock present: %s" % lock)
        if jvm_running():
            reporter.line("    A Ghidra JVM is running - close the GUI and re-run.")
        else:
            reporter.line("    No Ghidra JVM is running, so this lock is STALE")
            reporter.line("    (usually left by an interrupted run). Delete it and")
            reporter.line("    its '~' sibling, then re-run:")
            reporter.line("      Remove-Item %s, %s~ -Force" % (lock, lock))
        return 1

    import pyghidra
    pyghidra.start()
    bind_ghidra()

    reporter.line("project: %s / %s" % (location, project_name))
    project = G["GhidraProject"].openProject(location, project_name, True)
    try:
        which = ["reference", "target"] if args.only is None else [args.only]
        for key in which:
            extract_program(project, cfg["builds"][key], cfg, reporter)
    finally:
        project.close()

    reporter.save(os.path.join(common.out_dir(cfg), "extract_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
