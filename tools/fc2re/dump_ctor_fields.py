# Recover fields by walking constructors (PyGhidra).
#
# The classes with no RegisterProperties -- CEntity, CEntitySystem, the
# containers, the job system -- are the engine's runtime machinery and are
# still empty. In this non-inlined build their constructors spell the layout
# out: a member of class type gets its own constructor called at this+K, and a
# scalar member is stored to directly in the same init sequence.
#
#   CFoo::CFoo(CFoo *this) {
#     this->vptr = &PTR_...;
#     CBase::CBase(this);                    <- base at 0
#     CryVector::CryVector(&this->field_0x1c);  <- member of type CryVector
#     *(undefined4 *)&this->field_0x28 = 0;     <- scalar, 4 bytes
#   }
#
# Offsets are read off Ghidra's field_0xNN naming rather than from pointer
# arithmetic: `this` is typed, so `this + 1` means one whole object, not one
# byte, and reading it as a byte offset would be silently wrong.
#
# A destructor walks the same members in reverse, so it is harvested too and
# used as an independent check.
#
# Output: ctor_fields.jsonl, one row per recovered field.
#
#   python dump_ctor_fields.py OUT C:\projdir fc2 /FarCry2_server \
#       --classes CEntity,CEntitySystem,CryVector
#
# Read-only: opens no transaction and never mutates the program.
#
# @category FC2RE
# @runtime PyGhidra

import argparse
import json
import os
import re
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from apply_vtables import split_scopes
from dump_class_sizes import base_class_name
from dump_properties import jstr, to_statements

PROGRESS_EVERY = 200

# Anything past this is pointer arithmetic being misread, not a field.
MAX_FIELD_OFFSET = 0x40000

# Width implied by the cast in front of a scalar store.
CAST_WIDTHS = {
    "undefined": 1, "undefined1": 1, "byte": 1, "char": 1, "bool": 1,
    "uchar": 1, "int8_t": 1, "undefined2": 2, "short": 2, "ushort": 2,
    "wchar_t": 2, "undefined4": 4, "int": 4, "uint": 4, "long": 4,
    "ulong": 4, "float": 4, "undefined8": 8, "double": 8, "longlong": 8,
    "ulonglong": 8,
}


# ---------------------------------------------------------------------------
# pure logic -- kept Ghidra-free so it can be unit tested without a JVM
# ---------------------------------------------------------------------------
RE_FIELD = re.compile(r"field_0x([0-9a-fA-F]+)")
RE_CALL = re.compile(
    r"(?P<qual>[A-Za-z_][\w:<>,\s*&]*?)::(?P<method>~?[A-Za-z_]\w*)"
    r"(?:<[^()]*>)?\s*\(\s*(?P<arg>[^,;)]*)")
RE_STORE = re.compile(
    r"^(?:\*\(\s*(?P<cast>[A-Za-z_]\w*)\s*\*+\s*\)\s*)?"
    r"&?(?P<lhs>[^=;]*?)\s*=\s*(?P<rhs>[^=].*)$")

# GCC inlines a small member constructor into its owner, so a member of class
# type leaves no call -- but if it is polymorphic its vptr store survives, and
# the vtable names the member's class.
RE_VPTR_RHS = re.compile(
    r"^\(?[\w:<>,\s*&()]*\)?\s*\(?(?P<sym>PTR_\w*?_[0-9a-fA-F]{6,16})\s*"
    r"(?:\+\s*(?P<add>0x[0-9a-fA-F]+|\d+))?\s*\)?\s*;?$")
RE_TRAILING_HEX = re.compile(r"_([0-9a-fA-F]{6,16})$")


def vtable_slot(rhs):
    """A `PTR_vtable_<got> + 8` right-hand side -> the GOT slot address."""
    m = RE_VPTR_RHS.match((rhs or "").strip())
    if not m or not m.group("add"):
        return None
    h = RE_TRAILING_HEX.search(m.group("sym"))
    return int(h.group(1), 16) if h else None


def field_offset(text):
    """The single field_0xNN an expression refers to, if exactly one."""
    found = RE_FIELD.findall(text or "")
    if len(found) != 1:
        return None
    try:
        value = int(found[0], 16)
    except ValueError:
        return None
    return value if 0 <= value <= MAX_FIELD_OFFSET else None


def is_self(expr):
    """`this` on its own, ignoring casts and address-of."""
    cleaned = re.sub(r"\(\s*[\w:<>,\s*&]*\*+\s*\)", "", expr or "")
    return cleaned.replace("&", "").strip() == "this"


def parse_ctor(lines, owner, bases=(), resolve_vtable=None):
    """Constructor body -> [(offset, type or None, width or None)].

    A call whose argument is bare `this` is the base subobject at 0, not a
    member, so it is only recorded when the callee is not a known base.
    """
    leaf = base_class_name(owner)
    base_names = {base_class_name(b) for b in bases}
    members = {}

    for raw in to_statements(lines):
        stmt = raw.strip()

        for m in RE_CALL.finditer(stmt):
            callee = base_class_name(m.group("qual"))
            method = m.group("method").lstrip("~")
            if base_class_name(method) != callee:
                continue                      # not a constructor/destructor
            if callee == leaf:
                continue                      # the class's own ctor
            arg = m.group("arg")
            if is_self(arg):
                if callee not in base_names:
                    members.setdefault(0, {"type": callee, "width": None})
                continue
            off = field_offset(arg)
            if off is not None:
                members.setdefault(off, {"type": callee, "width": None})

        if "::" in stmt:
            continue                          # a call, already handled
        m = RE_STORE.match(stmt)
        if not m:
            continue
        off = field_offset(m.group("lhs"))
        if off is None:
            continue
        width = CAST_WIDTHS.get((m.group("cast") or "").lower())
        cur = members.setdefault(off, {"type": None, "width": width})
        if cur["width"] is None:
            cur["width"] = width

        # An inlined member constructor still stores the member's vptr.
        if cur["type"] is None and resolve_vtable is not None:
            slot = vtable_slot(m.group("rhs"))
            if slot is not None:
                found = resolve_vtable(slot)
                if found and base_class_name(found) != base_class_name(owner):
                    cur["type"] = found
                    cur["width"] = cur["width"] or 4

    return [{"offset": o, "type_name": v["type"], "width": v["width"]}
            for o, v in sorted(members.items())]


def merge(ctor_rows, dtor_rows):
    """Constructor evidence wins; a destructor only corroborates or adds."""
    out = {}
    for r in ctor_rows:
        out[r["offset"]] = dict(r, evidence="constructor")
    for r in dtor_rows:
        cur = out.get(r["offset"])
        if cur is None:
            out[r["offset"]] = dict(r, evidence="destructor")
        elif cur.get("type_name") == r.get("type_name") and r.get("type_name"):
            cur["evidence"] = "both"
    return [out[o] for o in sorted(out)]


def load_jsonl(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


# ---------------------------------------------------------------------------
# Ghidra-side work
# ---------------------------------------------------------------------------
class Walker(object):
    def __init__(self, program, monitor, vtables=()):
        self.program = program
        self.monitor = monitor
        self.fm = program.getFunctionManager()
        self.memory = program.getMemory()
        self.base = program.getImageBase()
        self.by_vtable = {}
        for v in vtables:
            cls = v.get("ghidra_class") or v.get("class")
            if cls and v.get("address"):
                self.by_vtable[int(v["address"], 16)] = cls
        from ghidra.app.decompiler import DecompInterface
        self.decomp = DecompInterface()
        self.decomp.openProgram(program)

    def class_at_got(self, slot):
        """GOT slot -> the class whose vtable it points at."""
        try:
            addr = self.base.getNewAddress(slot)
            target = int(self.memory.getInt(addr)) & 0xFFFFFFFF
        except Exception:
            return None
        return self.by_vtable.get(target)

    def structors(self):
        """class -> {'ctor': [...], 'dtor': [...]} by name convention."""
        out = defaultdict(lambda: {"ctor": [], "dtor": []})
        it = self.fm.getFunctions(True)
        while it.hasNext():
            f = it.next()
            ns = f.getParentNamespace()
            if ns is None or ns.isGlobal():
                continue
            owner = jstr(ns.getName(True))
            simple = jstr(f.getName(False))
            leaf = base_class_name(owner)
            if simple == leaf:
                out[owner]["ctor"].append(f)
            elif simple == "~" + leaf:
                out[owner]["dtor"].append(f)
        return out

    def body(self, f):
        res = self.decomp.decompileFunction(f, 90, self.monitor)
        if res is None or not res.decompileCompleted():
            return None
        high = res.getDecompiledFunction()
        return jstr(high.getC()).splitlines() if high is not None else None

    def richest(self, funcs, owner, bases):
        """Several constructors exist; take whichever names the most."""
        best = []
        for f in funcs:
            lines = self.body(f)
            if not lines:
                continue
            got = parse_ctor(lines, owner, bases, self.class_at_got)
            if len(got) > len(best):
                best = got
        return best

    def dispose(self):
        try:
            self.decomp.dispose()
        except Exception:
            pass


def base_map(typeinfo):
    by_symbol = {t["symbol"]: (t.get("ghidra_class") or t.get("class"))
                 for t in typeinfo if t.get("symbol")}
    out = defaultdict(list)
    for t in typeinfo:
        cls = t.get("ghidra_class") or t.get("class")
        if not cls:
            continue
        for b in t.get("bases") or []:
            base = by_symbol.get(b.get("typeinfo"))
            if base:
                out[cls].append(base)
    return out


def known_offsets(properties):
    """Offsets the property pass already owns, so they are not re-reported."""
    out = defaultdict(set)
    for r in properties:
        if r.get("owner") and r.get("offset") is not None:
            out[r["owner"]].add(r["offset"])
    return out


def run(program, monitor, outdir, opts):
    typeinfo = load_jsonl(os.path.join(outdir, "typeinfo.jsonl"))
    properties = load_jsonl(os.path.join(outdir,
                                         "register_properties.jsonl"))
    vtables = load_jsonl(os.path.join(outdir, "vtables.jsonl"))
    bases = base_map(typeinfo)
    already = known_offsets(properties)

    w = Walker(program, monitor, vtables)
    structors = w.structors()
    print("[*] %d classes have a constructor or destructor" % len(structors))

    wanted = None
    if opts.classes:
        wanted = {c.strip() for c in opts.classes.split(",") if c.strip()}
        targets = [c for c in structors
                   if c in wanted or base_class_name(c) in wanted]
    else:
        targets = sorted(structors)
    print("[*] walking %d classes" % len(targets))

    rows = []
    stats = Counter()
    try:
        for i, owner in enumerate(sorted(targets), 1):
            if monitor.isCancelled():
                break
            if i % PROGRESS_EVERY == 0:
                print("    %d/%d, %d fields" % (i, len(targets), len(rows)),
                      flush=True)
            group = structors[owner]
            ctor = w.richest(group["ctor"], owner, bases.get(owner, ()))
            dtor = w.richest(group["dtor"], owner, bases.get(owner, ()))
            merged = merge(ctor, dtor)
            if not merged:
                stats["no_fields"] += 1
                continue
            stats["with_fields"] += 1
            seen = already.get(owner, set())
            for m in merged:
                stats["evidence:" + m["evidence"]] += 1
                if m["offset"] in seen:
                    stats["already_known"] += 1
                rows.append({
                    "owner": owner,
                    "offset": m["offset"],
                    "type_name": m["type_name"],
                    "width": m["width"],
                    "evidence": m["evidence"],
                    "new": m["offset"] not in seen,
                })
    finally:
        w.dispose()

    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, "ctor_fields.jsonl")
    with open(out, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=True) + "\n")

    typed = [r for r in rows if r["type_name"]]
    fresh = [r for r in rows if r["new"]]
    lines = [
        "classes walked        : %d" % len(targets),
        "  yielded fields      : %d" % stats["with_fields"],
        "  yielded nothing     : %d" % stats["no_fields"],
        "",
        "fields recovered      : %d" % len(rows),
        "  with a member type  : %d" % len(typed),
        "  scalar (width only) : %d" % (len(rows) - len(typed)),
        "  not already known   : %d" % len(fresh),
        "",
        "by evidence:",
    ]
    for k in sorted(stats):
        if k.startswith("evidence:"):
            lines.append("  %-20s %d" % (k.split(":", 1)[1], stats[k]))
    report = "\n".join(lines)
    with open(os.path.join(outdir, "ctor_fields_report.txt"), "w",
              encoding="utf-8") as fh:
        fh.write(report + "\n")
    print()
    print(report)
    print("\n[+] wrote %s" % out)


def main_headless():
    ap = argparse.ArgumentParser(
        description="Recover fields by walking constructors. Never writes.")
    ap.add_argument("outdir")
    ap.add_argument("project_location")
    ap.add_argument("project_name")
    ap.add_argument("program")
    ap.add_argument("--classes", default=None,
                    help="comma-separated class names; omit to walk all")
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
            run(program, monitor, args.outdir, args)
        finally:
            project.close(program)
    finally:
        project.close()


if __name__ == "__main__":
    main_headless()
