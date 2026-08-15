# Recover exact sizeof(T) from allocation sites (PyGhidra).
#
# An allocation is immediately followed by the constructor for the class being
# built, so the pair gives the class its true size:
#
#   pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x14,0);
#   CFoo::CFoo(pCVar1, ...);          ->  sizeof(CFoo) == 0x14
#
# The constructor called at the allocation site is the most-derived one, so a
# `new Derived` records Derived rather than the base whose constructor it
# chains to.
#
# Only sites with a literal size are used: a computed size means an array or a
# variable-length allocation and says nothing about the class.
#
# Output: class_sizes.jsonl (one row per class) and class_size_sites.jsonl
# (one row per allocation site, so conflicts can be traced).
#
#   A) Ghidra Script Manager, with FarCry2_server open
#   B) headless:
#        python dump_class_sizes.py OUT C:\path\to\projdir fc2 /FarCry2_server
#
# Read-only: opens no transaction and never mutates the program.
#
# @category FC2RE
# @runtime PyGhidra

import json
import os
import re
import sys
import time
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from dump_properties import (jstr, parse_int, to_statements,
                             symbol_trailing_addr)

# Allocators whose first argument is a byte count.
ALLOCATOR_NAMES = ("NMalloc", "NMallocAligned", "operator.new",
                   "operator.new[]")
ALLOCATOR_SYMBOLS = ("_Znwj", "_Znaj")

# Anything larger is a buffer or a pool block, not a single object.
MAX_PLAUSIBLE_SIZE = 0x40000

PROGRESS_EVERY = 250

DecompInterface = None
ConsoleTaskMonitor = None


def bind_java_types():
    global DecompInterface, ConsoleTaskMonitor
    from ghidra.app.decompiler import DecompInterface as _DI
    from ghidra.util.task import ConsoleTaskMonitor as _CTM
    DecompInterface = _DI
    ConsoleTaskMonitor = _CTM


# ---------------------------------------------------------------------------
# pure logic -- kept Ghidra-free so it can be unit tested without a JVM
# ---------------------------------------------------------------------------
RE_ALLOC_CALL = re.compile(
    r"^(?P<var>\w+)\s*=\s*(?:\((?P<cast>[^()]*)\)\s*)?"
    r"(?:(?P<scope>[\w:]+)::)?(?P<fn>NMalloc\w*|operator\.new\[?\]?)"
    r"\(\s*(?P<size>0x[0-9a-fA-F]+|\d+)\s*[,)]")

# A constructor call reads Class::Class(var, ...) after template arguments are
# accounted for, so the two halves are compared rather than pattern-matched.
RE_CALL = re.compile(
    r"(?P<qual>[A-Za-z_][\w:<>,\s*&]*?)::(?P<method>[~A-Za-z_]\w*)"
    r"(?:<[^()]*>)?\s*\(\s*(?P<first>[\w&*()]+)")

RE_VPTR_STORE = re.compile(
    r"^\*\([^()]*\)\s*(?P<var>\w+)\s*=\s*&?(?P<sym>[A-Za-z_]\w*)"
    r"(?:\s*\+\s*(?:0x[0-9a-fA-F]+|\d+))?\s*;$")


def last_clause(stmt):
    """Drop any block punctuation a statement carries from the line before.

    Statements are split on ';', so the first one still holds the function
    header and an opening brace.
    """
    for ch in "{}":
        stmt = stmt.rsplit(ch, 1)[-1]
    return stmt.strip()


def base_class_name(qualified):
    """Strip template arguments and outer scopes: A::B<C>::D -> D."""
    depth = 0
    out = []
    for ch in qualified:
        if ch == "<":
            depth += 1
        elif ch == ">":
            depth = max(0, depth - 1)
        elif depth == 0:
            out.append(ch)
    return "".join(out).strip().rsplit("::", 1)[-1].strip()


def is_constructor(qualified, method):
    """Class::Class, allowing for templates and nested scopes."""
    if method.startswith("~"):
        return False
    return base_class_name(qualified) == base_class_name(method)


def find_sites(lines):
    """Decompiled function -> [(class, size, evidence)] for each allocation.

    The class is taken from the first constructor invoked on the allocated
    pointer; failing that, from the vtable stored into it.
    """
    pending = {}
    sites = []
    for raw in to_statements(lines):
        stmt = last_clause(raw)
        m = RE_ALLOC_CALL.match(stmt)
        if m:
            size = parse_int(m.group("size"))
            if size and 0 < size <= MAX_PLAUSIBLE_SIZE:
                pending[m.group("var")] = {
                    "size": size,
                    "cast": (m.group("cast") or "").replace("*", "").strip(),
                }
            continue

        store = RE_VPTR_STORE.match(stmt)
        if store and store.group("var") in pending:
            rec = pending[store.group("var")]
            rec.setdefault("vtable_sym", store.group("sym"))
            continue

        for c in RE_CALL.finditer(stmt):
            var = c.group("first")
            if var not in pending:
                continue
            if not is_constructor(c.group("qual"), c.group("method")):
                continue
            rec = pending.pop(var)
            sites.append({
                "class": base_class_name(c.group("qual")),
                "size": rec["size"],
                "evidence": "constructor",
            })
            break

    # Allocations with no constructor still name a class if a vtable landed.
    for rec in pending.values():
        if rec.get("vtable_sym") or rec.get("cast"):
            sites.append({
                "class": base_class_name(rec.get("cast") or ""),
                "size": rec["size"],
                "evidence": "cast" if rec.get("cast") else "vtable",
                "vtable_sym": rec.get("vtable_sym"),
            })
    return [s for s in sites if s["class"]]


NON_CLASS_NAMES = frozenset((
    "void", "code", "char", "uchar", "byte", "short", "ushort", "int",
    "uint", "long", "ulong", "float", "double", "bool", "size_t",
))


def is_class_name(name):
    """A cast to char* or undefined4* names a buffer, not a class."""
    if not name or name in NON_CLASS_NAMES:
        return False
    if name.startswith("undefined"):
        return False
    return not name.islower()


def reconcile(rows):
    """Sites -> one row per class.

    Where sites disagree the largest size wins, not the most common one. A
    cast to a base class in front of a derived allocation credits the derived
    size to the base, and undersizing is the harmful direction: Ghidra renders
    accesses past the end of a struct as `this[1].Field`, reusing a member
    name for an offset it does not describe. Oversizing only leaves undefined
    bytes.
    """
    seen = defaultdict(Counter)
    for r in rows:
        if is_class_name(r["class"]):
            seen[r["class"]][r["size"]] += 1

    out = []
    for cls, counts in sorted(seen.items()):
        majority, hits = counts.most_common(1)[0]
        total = sum(counts.values())
        out.append({
            "class": cls,
            "size": max(counts),
            "majority_size": majority,
            "sites": total,
            "agreement": round(hits / float(total), 3),
            "conflicting_sizes": sorted(counts) if len(counts) > 1 else [],
        })
    return out


# ---------------------------------------------------------------------------
# Ghidra-side work
# ---------------------------------------------------------------------------
def find_allocators(program):
    """Every function that hands back a block of a caller-specified size."""
    fm = program.getFunctionManager()
    found = []
    it = fm.getFunctions(True)
    while it.hasNext():
        f = it.next()
        simple = jstr(f.getName(False))
        if simple in ALLOCATOR_NAMES:
            found.append(f)
            continue
        for sym in program.getSymbolTable().getSymbols(f.getEntryPoint()):
            if jstr(sym.getName(False)) in ALLOCATOR_SYMBOLS:
                found.append(f)
                break
    return found


def collect_callers(allocators, monitor):
    callers = {}
    for a in allocators:
        for f in a.getCallingFunctions(monitor):
            callers[jstr(f.getEntryPoint().toString())] = f
    return list(callers.values())


def run(program, monitor, outdir):
    nfuncs = program.getFunctionManager().getFunctionCount()
    print("[*] %s, %d functions" % (jstr(program.getName()), nfuncs))
    if nfuncs == 0:
        raise SystemExit("[!] 0 functions: wrong or unanalyzed program.")

    allocators = find_allocators(program)
    print("[*] allocators: %s"
          % ", ".join(sorted(jstr(a.getName(True)) for a in allocators)))
    if not allocators:
        raise SystemExit("[!] no allocator functions found.")

    callers = collect_callers(allocators, monitor)
    print("[*] %d functions call an allocator" % len(callers))

    decomp = DecompInterface()
    decomp.openProgram(program)
    monitor.initialize(len(callers))

    sites = []
    failed = 0
    started = time.time()
    try:
        for i, f in enumerate(callers, 1):
            if monitor.isCancelled():
                break
            monitor.incrementProgress(1)
            if i % PROGRESS_EVERY == 0 or i == len(callers):
                done = time.time() - started
                rate = i / done if done else 0
                left = (len(callers) - i) / rate if rate else 0
                print("    %5d/%d  %4.0fs elapsed, ~%.0fs left, %d sites"
                      % (i, len(callers), done, left, len(sites)),
                      flush=True)
            res = decomp.decompileFunction(f, 90, monitor)
            if res is None or not res.decompileCompleted():
                failed += 1
                continue
            high = res.getDecompiledFunction()
            if high is None:
                failed += 1
                continue
            where = jstr(f.getEntryPoint().toString())
            for s in find_sites(jstr(high.getC()).splitlines()):
                s["function"] = jstr(f.getName(True))
                s["address"] = where
                sites.append(s)
    finally:
        try:
            decomp.dispose()
        except Exception:
            pass

    classes = reconcile(sites)
    os.makedirs(outdir, exist_ok=True)
    write_jsonl(os.path.join(outdir, "class_size_sites.jsonl"), sites)
    write_jsonl(os.path.join(outdir, "class_sizes.jsonl"), classes)

    report = build_report(len(callers), failed, sites, classes)
    with open(os.path.join(outdir, "class_sizes_report.txt"), "w",
              encoding="utf-8") as fh:
        fh.write(report + "\n")
    print()
    print(report)


def write_jsonl(path, rows):
    with open(path, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=True) + "\n")


def build_report(callers, failed, sites, classes):
    ev = Counter(s["evidence"] for s in sites)
    clean = [c for c in classes if not c["conflicting_sizes"]]
    conflicted = [c for c in classes if c["conflicting_sizes"]]
    lines = [
        "allocator callers    : %d" % callers,
        "  decompile failed   : %d" % failed,
        "allocation sites used: %d" % len(sites),
    ]
    for k, v in ev.most_common():
        lines.append("  by %-16s %d" % (k, v))
    lines += [
        "",
        "classes sized        : %d" % len(classes),
        "  single size        : %d" % len(clean),
        "  conflicting sizes  : %d" % len(conflicted),
    ]
    if conflicted:
        lines.append("")
        lines.append("== worst conflicts ==")
        worst = sorted(conflicted, key=lambda c: c["agreement"])[:15]
        for c in worst:
            lines.append("  %-40s %s  agreement=%.2f"
                         % (c["class"],
                            [hex(s) for s in c["conflicting_sizes"]][:6],
                            c["agreement"]))
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# entry points
# ---------------------------------------------------------------------------
def main_script():
    bind_java_types()
    g = globals()
    argv = [jstr(a) for a in (g.get("getScriptArgs", lambda: [])() or [])]
    outdir = argv[0] if argv else g["askDirectory"](
        "Class size output directory", "Choose").getAbsolutePath()
    run(g["currentProgram"], g["monitor"], outdir)


def main_headless():
    import argparse

    ap = argparse.ArgumentParser(
        description="Recover sizeof(T) from allocation sites. Never writes.")
    ap.add_argument("outdir")
    ap.add_argument("project_location", nargs="?")
    ap.add_argument("project_name", nargs="?")
    ap.add_argument("program", nargs="?")
    ap.add_argument("--from-sites", default=None,
                    help="re-reconcile an existing class_size_sites.jsonl "
                         "instead of re-scanning; needs no Ghidra")
    args = ap.parse_args()

    if args.from_sites:
        with open(args.from_sites, encoding="utf-8") as fh:
            sites = [json.loads(l) for l in fh if l.strip()]
        classes = reconcile(sites)
        os.makedirs(args.outdir, exist_ok=True)
        write_jsonl(os.path.join(args.outdir, "class_sizes.jsonl"), classes)
        report = build_report(0, 0, sites, classes)
        with open(os.path.join(args.outdir, "class_sizes_report.txt"), "w",
                  encoding="utf-8") as fh:
            fh.write(report + "\n")
        print(report)
        return

    if not (args.project_location and args.project_name and args.program):
        ap.error("project_location, project_name and program are required "
                 "unless --from-sites is given")

    import pyghidra
    pyghidra.start()
    bind_java_types()

    from ghidra.base.project import GhidraProject

    monitor = ConsoleTaskMonitor()
    project = GhidraProject.openProject(args.project_location,
                                        args.project_name, True)
    try:
        path = args.program if args.program.startswith("/") \
            else "/" + args.program
        folder, _, pname = path.rpartition("/")
        program = project.openProgram(folder or "/", pname, True)
        try:
            run(program, monitor, args.outdir)
        finally:
            project.close(program)
    finally:
        project.close()


if "currentProgram" in globals():
    main_script()
elif __name__ == "__main__":
    main_headless()
