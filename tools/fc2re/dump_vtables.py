# Harvest vtables and the RTTI inheritance graph (PyGhidra).
#
# Both live in .data.rel.ro and cross-reference each other, so they are read
# in one pass.
#
# An Itanium vtable is one or more sub-tables, each laid out as
#
#   [virtual base offsets...]  offset-to-top  typeinfo*  fn*  fn*  ...
#
# A class with multiple bases emits one sub-table per base subobject. The
# typeinfo pointer is what marks the start of each, which is how the boundary
# is found -- the technique `tmp/compare-dlls/dump_inventory.py` uses.
#
# Typeinfo records are ABI-specified and self-identifying: the vptr at +0
# names which of the three __cxxabiv1 layouts is in use, and the vmi variant
# encodes each base's offset in the top 24 bits of its offset_flags word. That
# gives exact base subobject offsets rather than inferred ones.
#
# Output: vtables.jsonl (ordered virtual method list per class per subobject)
# and typeinfo.jsonl (the inheritance graph with offsets).
#
#   A) Ghidra Script Manager or MCP, with FarCry2_server open:
#        args: <outdir>
#   B) headless:
#        python dump_vtables.py OUT C:\path\to\projdir fc2 /FarCry2_server
#
# Read-only: opens no transaction and never mutates the program.
#
# @category FC2RE
# @runtime PyGhidra

import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from dump_properties import jstr

VTABLE_PREFIX = "_ZTV"
TYPEINFO_PREFIX = "_ZTI"
TYPENAME_PREFIX = "_ZTS"

# The three libstdc++ typeinfo layouts, by the vtable their records point at.
CXXABI_KINDS = {
    "_ZTVN10__cxxabiv117__class_type_infoE": "none",
    "_ZTVN10__cxxabiv120__si_class_type_infoE": "single",
    "_ZTVN10__cxxabiv121__vmi_class_type_infoE": "multiple",
}

# A vptr points past the offset-to-top and typeinfo words of its vtable.
VTABLE_HEADER = 8

MAX_SLOTS = 4096
PROGRESS_EVERY = 2000

# A secondary subobject's offset-to-top is a small signed displacement, not a
# pointer; anything wilder than this ends the table.
MAX_OFFSET_TO_TOP = 0x100000
MAX_GAP_WORDS = 2


def jint(v):
    """Signed 32-bit."""
    v &= 0xFFFFFFFF
    return v - 0x100000000 if v & 0x80000000 else v


def read_component(text, pos):
    """One length-prefixed Itanium name component."""
    m = re.match(r"(\d+)", text[pos:])
    if not m:
        return None, pos
    n = int(m.group(1))
    start = pos + len(m.group(1))
    if start + n > len(text):
        return None, pos
    return text[start:start + n], start + n


def demangled_class(mangled, prefix):
    """_ZTV14CGenericMemberI... -> CGenericMember<...>.

    Nested names use the N...E form with one length-prefixed component per
    scope, so CEntitySystem::DeathRowCell arrives as N13CEntitySystem12...E.
    Anything that fails to parse returns None rather than a guess.
    """
    body = mangled[len(prefix):]
    nested = body.startswith("N")
    pos = 1 if nested else 0

    parts = []
    while True:
        name, nxt = read_component(body, pos)
        if name is None:
            break
        parts.append(name)
        pos = nxt
        if not nested:
            break

    if not parts:
        return None
    out = "::".join(parts)
    tail = body[pos:]
    if nested:
        tail = tail[:-1] if tail.endswith("E") else tail
    return "%s<%s>" % (out, tail) if tail else out


class Reader(object):
    def __init__(self, program):
        self.program = program
        self.memory = program.getMemory()
        self.st = program.getSymbolTable()
        self.fm = program.getFunctionManager()
        self.base = program.getImageBase()

    def addr(self, value):
        try:
            return self.base.getNewAddress(value & 0xFFFFFFFF)
        except Exception:
            return None

    def word(self, addr):
        try:
            return int(self.memory.getInt(addr)) & 0xFFFFFFFF
        except Exception:
            return None

    def owner_at(self, addr):
        """The class Ghidra's demangler attributed to an address.

        It writes `<Class>::vtable` and `<Class>::typeinfo`, so the parent
        namespace spells the class the same way the type database does --
        template arguments included, which a second-hand demangler will not
        reproduce.
        """
        for s in self.st.getSymbols(addr):
            ns = s.getParentNamespace()
            if ns is not None and not ns.isGlobal():
                return jstr(ns.getName(True))
        return None

    def names_at(self, value):
        a = self.addr(value)
        if a is None or not self.memory.contains(a):
            return []
        return [jstr(s.getName(False)) for s in self.st.getSymbols(a)]

    def pick(self, value, prefix):
        for n in self.names_at(value):
            if n and n.startswith(prefix):
                return n
        return None

    def function_at(self, value):
        a = self.addr(value)
        if a is None:
            return None
        f = self.fm.getFunctionAt(a)
        return jstr(f.getName(True)) if f is not None else None

    def cstring(self, value, limit=512):
        a = self.addr(value)
        if a is None:
            return None
        out = []
        try:
            for i in range(limit):
                b = self.memory.getByte(a.add(i)) & 0xFF
                if b == 0:
                    break
                out.append(chr(b))
        except Exception:
            return None
        return "".join(out) or None


class Harvester(object):
    def __init__(self, program, monitor):
        self.r = Reader(program)
        self.monitor = monitor
        self.program = program
        self.kind_by_vptr = self._typeinfo_kinds()

    def _typeinfo_kinds(self):
        """vptr value -> which __cxxabiv1 layout the record uses."""
        out = {}
        for sym, kind in CXXABI_KINDS.items():
            for s in self.program.getSymbolTable().getSymbols(sym):
                a = s.getAddress()
                if a is not None:
                    out[a.getOffset() + VTABLE_HEADER] = kind
        return out

    def symbols_with(self, prefix):
        seen = {}
        it = self.program.getSymbolTable().getAllSymbols(True)
        while it.hasNext():
            if self.monitor.isCancelled():
                break
            s = it.next()
            name = jstr(s.getName(False))
            if not name or not name.startswith(prefix):
                continue
            a = s.getAddress()
            if a is None or not self.r.memory.contains(a):
                continue
            # Versioned aliases (name@@CXXABI_1.3) sit on the same address.
            seen.setdefault(a.getOffset(), name)
        return seen

    def read_vtable(self, start, limit):
        """Split one _ZTV blob into its per-subobject tables."""
        prefix = []
        subs = []
        cur = None
        offset = 0
        misses = 0
        while offset < MAX_SLOTS * 4:
            here = self.r.addr(start + offset)
            if here is None or (limit and start + offset >= limit):
                break
            raw = self.r.word(here)
            if raw is None:
                break

            ti = self.r.pick(raw, TYPEINFO_PREFIX)
            if ti is not None:
                top = self.r.word(self.r.addr(start + offset - 4)) \
                    if offset >= 4 else 0
                cur = {"offset_to_top": jint(top or 0), "typeinfo": ti,
                       "functions": []}
                subs.append(cur)
                offset += 4
                misses = 0
                continue

            fn = self.r.function_at(raw)
            if fn is not None:
                if cur is None:
                    prefix.append(jint(raw))
                else:
                    cur["functions"].append({"index": len(cur["functions"]),
                                             "offset": offset, "name": fn})
                offset += 4
                misses = 0
                continue

            if cur is None:
                # Virtual base offsets and the leading offset-to-top.
                prefix.append(jint(raw))
                offset += 4
                continue

            # Each secondary subobject table restarts with its own
            # offset-to-top, which is a small negative number rather than a
            # pointer. Breaking here would truncate every multiply-inheriting
            # class to its primary table, so a short run of non-pointers is
            # tolerated and the typeinfo that follows reopens a table.
            if misses < MAX_GAP_WORDS and abs(jint(raw)) <= MAX_OFFSET_TO_TOP:
                offset += 4
                misses += 1
                continue
            break
        return prefix, subs

    def harvest_vtables(self, next_symbol):
        rows = []
        table = self.symbols_with(VTABLE_PREFIX)
        print("[*] %d vtable symbols" % len(table), flush=True)
        for i, (addr, name) in enumerate(sorted(table.items()), 1):
            if self.monitor.isCancelled():
                break
            if i % PROGRESS_EVERY == 0:
                print("    vtables %d/%d" % (i, len(table)), flush=True)
            prefix, subs = self.read_vtable(addr, next_symbol(addr))
            rows.append({
                "address": "%08x" % addr,
                "symbol": name,
                "class": demangled_class(name, VTABLE_PREFIX),
                "ghidra_class": self.r.owner_at(self.r.addr(addr)),
                "leading_words": prefix,
                "subobjects": subs,
                "total_functions": sum(len(s["functions"]) for s in subs),
            })
        return rows

    def read_typeinfo(self, addr, name):
        vptr = self.r.word(self.r.addr(addr))
        kind = self.kind_by_vptr.get(vptr or -1, "unknown")
        name_ptr = self.r.word(self.r.addr(addr + 4))
        row = {
            "address": "%08x" % addr,
            "symbol": name,
            "class": demangled_class(name, TYPEINFO_PREFIX),
            "ghidra_class": self.r.owner_at(self.r.addr(addr)),
            "kind": kind,
            "type_name": self.r.cstring(name_ptr) if name_ptr else None,
            "bases": [],
        }
        if kind == "single":
            base = self.r.word(self.r.addr(addr + 8))
            sym = self.r.pick(base, TYPEINFO_PREFIX) if base else None
            if sym:
                row["bases"].append({"typeinfo": sym, "offset": 0,
                                     "virtual": False, "public": True})
        elif kind == "multiple":
            row["flags"] = self.r.word(self.r.addr(addr + 8))
            count = self.r.word(self.r.addr(addr + 12)) or 0
            for i in range(min(count, 64)):
                base = self.r.word(self.r.addr(addr + 16 + i * 8))
                of = self.r.word(self.r.addr(addr + 20 + i * 8))
                sym = self.r.pick(base, TYPEINFO_PREFIX) if base else None
                if sym is None or of is None:
                    continue
                row["bases"].append({
                    "typeinfo": sym,
                    "offset": jint(of) >> 8,
                    "virtual": bool(of & 1),
                    "public": bool(of & 2),
                })
        return row

    def harvest_typeinfo(self):
        rows = []
        table = self.symbols_with(TYPEINFO_PREFIX)
        print("[*] %d typeinfo symbols" % len(table), flush=True)
        for i, (addr, name) in enumerate(sorted(table.items()), 1):
            if self.monitor.isCancelled():
                break
            if i % PROGRESS_EVERY == 0:
                print("    typeinfo %d/%d" % (i, len(table)), flush=True)
            rows.append(self.read_typeinfo(addr, name))
        return rows


def write_jsonl(path, rows):
    with open(path, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=True) + "\n")


def build_report(vtables, typeinfo):
    kinds = {}
    for t in typeinfo:
        kinds[t["kind"]] = kinds.get(t["kind"], 0) + 1
    multi = [t for t in typeinfo if len(t["bases"]) > 1]
    virt = [t for t in typeinfo
            if any(b["virtual"] for b in t["bases"])]
    named = [v for v in vtables if v["class"]]
    with_fns = [v for v in vtables if v["total_functions"]]
    multi_sub = [v for v in vtables if len(v["subobjects"]) > 1]
    lines = [
        "vtables              : %d" % len(vtables),
        "  with a class name  : %d" % len(named),
        "  with functions     : %d" % len(with_fns),
        "  multiple subobjects: %d" % len(multi_sub),
        "  virtual functions  : %d" % sum(v["total_functions"]
                                          for v in vtables),
        "",
        "typeinfo records     : %d" % len(typeinfo),
    ]
    for k in sorted(kinds, key=lambda k: -kinds[k]):
        lines.append("  %-18s %d" % (k, kinds[k]))
    lines += [
        "  with >1 base       : %d" % len(multi),
        "  with a virtual base: %d" % len(virt),
        "inheritance edges    : %d" % sum(len(t["bases"]) for t in typeinfo),
    ]
    return "\n".join(lines)


def run(program, monitor, outdir):
    nfuncs = program.getFunctionManager().getFunctionCount()
    print("[*] %s, %d functions" % (jstr(program.getName()), nfuncs))
    if nfuncs == 0:
        raise SystemExit("[!] 0 functions: wrong or unanalyzed program.")

    h = Harvester(program, monitor)
    if not h.kind_by_vptr:
        raise SystemExit("[!] no __cxxabiv1 typeinfo vtables found; this "
                         "does not look like an Itanium-ABI binary.")

    # Vtables run until the next symbol of any kind.
    st = program.getSymbolTable()

    def next_symbol(addr):
        a = program.getImageBase().getNewAddress(addr)
        nxt = st.getSymbolIterator(a.add(1), True)
        while nxt.hasNext():
            s = nxt.next()
            sa = s.getAddress()
            if sa is not None and sa.getOffset() > addr:
                return sa.getOffset()
            break
        return None

    typeinfo = h.harvest_typeinfo()
    vtables = h.harvest_vtables(next_symbol)

    os.makedirs(outdir, exist_ok=True)
    write_jsonl(os.path.join(outdir, "typeinfo.jsonl"), typeinfo)
    write_jsonl(os.path.join(outdir, "vtables.jsonl"), vtables)
    report = build_report(vtables, typeinfo)
    with open(os.path.join(outdir, "vtables_report.txt"), "w",
              encoding="utf-8") as fh:
        fh.write(report + "\n")
    print()
    print(report)


def main_script(g):
    argv = [jstr(a) for a in (g.get("getScriptArgs", lambda: [])() or [])]
    outdir = argv[0] if argv else g["askDirectory"](
        "Vtable output directory", "Choose").getAbsolutePath()
    run(g["currentProgram"], g["monitor"], outdir)


def main_headless():
    import argparse

    ap = argparse.ArgumentParser(
        description="Harvest vtables and the RTTI inheritance graph.")
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
