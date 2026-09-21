# Harvest which classes each class looks up on its entity, and which classes the
# entity-component factory can create (PyGhidra).
#
# Components never declare what they need; a component reaches a sibling with
# CEntity::GetComponent<T>() from inside its own methods. Every such call site
# is recorded, so `uses` means "looks up", not "requires" -- most callers
# tolerate a null result.
#
# Output: component_uses.jsonl, one row per class with a CreateObject or a
# GetComponent<T> call:
#   {"class", "creatable", "uses": {T: [method, ...]}}
#
#   A) Ghidra Script Manager or MCP, with FarCry2_server open:
#        args: <outdir>
#   B) headless:
#        python dump_component_uses.py OUT C:\path\to\projdir fc2 /FarCry2_server
#
# Read-only: opens no transaction and never mutates the program.
#
# @category FC2RE
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from dump_properties import jstr

LOOKUP_PREFIX = "GetComponent<"
FACTORY_FN = "CreateObject"


def lookup_target(callee):
    """'GetComponent<CGraphicComponent>' -> 'CGraphicComponent'."""
    if not callee or not callee.startswith(LOOKUP_PREFIX) or not callee.endswith(">"):
        return None
    return callee[len(LOOKUP_PREFIX):-1]


def collect(calls, creatable):
    """(class, method, callee) triples + creatable class names -> rows."""
    uses = {}
    for cls, method, callee in calls:
        target = lookup_target(callee)
        if target and target != cls:
            methods = uses.setdefault(cls, {}).setdefault(target, [])
            if method not in methods:
                methods.append(method)
    rows = []
    for cls in sorted(set(uses) | set(creatable)):
        rows.append({
            "class": cls,
            "creatable": cls in creatable,
            "uses": {t: sorted(m) for t, m in sorted(uses.get(cls, {}).items())},
        })
    return rows


def run(program, monitor, outdir):
    fm = program.getFunctionManager()
    calls, creatable = [], set()
    it = fm.getFunctions(True)
    while it.hasNext() and not monitor.isCancelled():
        f = it.next()
        ns = f.getParentNamespace()
        if ns is None or ns.isGlobal():
            continue
        cls = jstr(ns.getName(True))
        name = jstr(f.getName(False))
        if name == FACTORY_FN:
            creatable.add(cls)
        for callee in f.getCalledFunctions(monitor):
            callee_name = jstr(callee.getName(False))
            if callee_name.startswith(LOOKUP_PREFIX):
                calls.append((cls, name, callee_name))

    rows = collect(calls, creatable)
    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, "component_uses.jsonl")
    with open(out, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=True) + "\n")
    print("[+] %d classes, %d creatable, %d with lookups -> %s" % (
        len(rows), len(creatable), sum(1 for r in rows if r["uses"]), out))


def main_script(g):
    argv = [jstr(a) for a in (g.get("getScriptArgs", lambda: [])() or [])]
    outdir = argv[0] if argv else g["askDirectory"](
        "Component uses output directory", "Choose").getAbsolutePath()
    run(g["currentProgram"], g["monitor"], outdir)


def main_headless():
    import argparse

    ap = argparse.ArgumentParser(
        description="Harvest GetComponent<T> lookups and creatable classes.")
    ap.add_argument("outdir")
    ap.add_argument("project_location")
    ap.add_argument("project_name")
    ap.add_argument("program")
    args = ap.parse_args()

    import pyghidra
    pyghidra.start()

    from ghidra.base.project import GhidraProject
    from ghidra.util.task import ConsoleTaskMonitor

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
    main_script(globals())
elif __name__ == "__main__":
    main_headless()
