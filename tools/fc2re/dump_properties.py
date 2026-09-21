# Dump Nomad property descriptors from CLASS::RegisterProperties (PyGhidra).
#
# A registering class builds one CMemberBase per serialized field and pushes
# it into the class descriptor, or into a group member's own child list:
#
#   p = (CMemberBase *)CMemMng::NMalloc(0x14, 0);
#   p->vptr = PTR_vtable_0a415dec + 8;       <- handler vtable, names kind + type
#   p->field_0x4 = "selDensity";             <- name, CRC32'd into field_0x8
#   p->field_0xc = 0x10;                     <- byte offset in the owner
#   CNomadObjectDescriptor::PushBackMember(ms_descriptor, p);
#
# Read from p-code rather than decompiled text, so it does not depend on how
# CMemberBase happens to be typed in the database.
#
# Output: register_properties.jsonl (one row per member),
# register_properties_classes.jsonl (one row per registrar) and a report.
#
# Two ways to run:
#   A) Ghidra Script Manager, with FarCry2_server open
#   B) headless, against an ALREADY ANALYZED program in a project:
#        python dump_properties.py OUT C:\path\to\projdir fc2 /FarCry2_server
#      Close the Ghidra GUI first, or the project lock will refuse the open.
#
# Read-only: opens no transaction and never mutates the program.
#
# @category FC2RE
# @runtime PyGhidra

import json
import os
import re

REGISTER_FN = "RegisterProperties"

# Descriptor slots, byte offsets into CMemberBase.
OFF_VPTR = 0x00
OFF_NAME = 0x04
OFF_OFFSET = 0x0C
OFF_ARG = 0x10
OFF_CHILD_NAME = 0x14
OFF_GETTER = 0x18
OFF_CHILD_NAME_2 = 0x1C

# An Itanium vtable pointer targets the first virtual slot, past the
# offset-to-top and typeinfo words.
VTABLE_HEADER = 8

MAX_CSTRING = 512

# Kinds whose template reads <Owner, ValueType, Handler, Flags, ...>.
VALUE_KINDS = frozenset((
    "CGenericMember", "CVirtualMember", "CVirtualMemberRef",
    "CVirtualMemberIntrinsicGetCopy", "CVirtualMemberIntrinsicGetRef",
    "COffsetMember", "CContainerMember", "CEnumContainerMember"))
ACCESSOR_KINDS = frozenset((
    "CVirtualMember", "CVirtualMemberRef",
    "CVirtualMemberIntrinsicGetCopy", "CVirtualMemberIntrinsicGetRef"))
CONTAINER_KINDS = frozenset(("CContainerMember", "CEnumContainerMember"))
ENUM_KINDS = frozenset(("CEnumMember", "CEnumContainerMember"))

# Descriptor kinds that describe something other than a field at an offset, so
# a missing offset is correct rather than a parse failure.
OFFSETLESS_KINDS = frozenset(("CSerializationEvent",))

RE_FLAGS = re.compile(r"^(?P<n>\d+)u$")

DecompInterface = None
ConsoleTaskMonitor = None
PcodeOp = None


def bind_java_types():
    global DecompInterface, ConsoleTaskMonitor, PcodeOp
    from ghidra.app.decompiler import DecompInterface as _DI
    from ghidra.util.task import ConsoleTaskMonitor as _CTM
    from ghidra.program.model.pcode import PcodeOp as _PO
    DecompInterface = _DI
    ConsoleTaskMonitor = _CTM
    PcodeOp = _PO


def jstr(v):
    """java.lang.String / None -> JSON-safe Python str.

    PyGhidra runs JPype with convertStrings=False, so Java strings arrive as
    JString objects that json refuses to serialize.
    """
    if v is None:
        return None
    try:
        return str(v)
    except Exception:
        return None


# ---------------------------------------------------------------------------
# pure logic -- kept Ghidra-free so it can be unit tested without a JVM
# ---------------------------------------------------------------------------
def split_template(name):
    """'A<B,C<D,E>,3u>' -> ('A', ['B', 'C<D,E>', '3u']); no '<' -> (name, [])."""
    if not name or "<" not in name:
        return name, []
    lt = name.index("<")
    args, depth, cur = [], 0, []
    for ch in name[lt + 1:name.rindex(">")]:
        if ch == "<":
            depth += 1
        elif ch == ">":
            depth -= 1
        if ch == "," and depth == 0:
            args.append("".join(cur))
            cur = []
        else:
            cur.append(ch)
    args.append("".join(cur))
    return name[:lt], args


def describe_handler(demangled):
    """Demangled vtable namespace -> kind, value type, type handler, flag bits."""
    kind, args = split_template(demangled)
    flags = None
    for arg in reversed(args):
        m = RE_FLAGS.match(arg)
        if m:
            flags = int(m.group("n"))
            break
    value_kind = kind in VALUE_KINDS and len(args) >= 4
    return {
        "kind": kind,
        "value_type": args[1] if value_kind else None,
        "handler": args[2] if value_kind else None,
        "flags": flags,
        # true: elements sit under a child named after the member; false: directly in the node.
        "wrapped": args[4] == "true" if kind in CONTAINER_KINDS and len(args) >= 5 else None,
    }


def member_pointer(slot):
    """An Itanium pointer-to-member: a function, or an odd vtable offset."""
    if slot is None:
        return None
    tag, value = slot[0], slot[1]
    if tag == "func":
        return value
    if tag == "int" and value & 1:
        return "virtual+0x%x" % (value - 1)
    return None


def slot_str(slots, off):
    slot = slots.get(off)
    return slot[1] if slot and slot[0] == "str" else None


def slot_int(slots, off):
    slot = slots.get(off)
    return slot[1] if slot and slot[0] == "int" else None


def assemble(events, owner, registrar, registrar_addr):
    """Construction events of one RegisterProperties -> member rows + class row.

    Events, in p-code order:
      ("alloc", mid, size)             a CMemberBase is allocated
      ("slot", mid, off, value)        a store into it; later stores win
      ("push", mid, parent_mid)        pushed into the descriptor or a group
      ("label", text)                  a string stored outside any member
      ("base", name)                   BASE::RegisterProperties() was called
      ("inherit", name)                BASE's members were copied wholesale
    A slot value is ("int", n), ("str", s), ("func", name) or
    ("vtable", demangled, mangled, addr).
    """
    allocs, slots, pushed, index_of = {}, {}, [], {}
    labels = {}
    last_enum = None
    bases, inherits = [], []
    for ev in events:
        tag = ev[0]
        if tag == "alloc":
            allocs[ev[1]] = (len(allocs), ev[2])
            slots[ev[1]] = {}
        elif tag == "slot" and ev[1] in slots:
            slots[ev[1]][ev[2]] = ev[3]
        elif tag == "push" and ev[1] in allocs and ev[1] not in index_of:
            index_of[ev[1]] = len(pushed)
            pushed.append((ev[1], ev[2]))
            vt = slots[ev[1]].get(OFF_VPTR)
            kind = describe_handler(vt[1])["kind"] if vt and vt[0] == "vtable" else None
            last_enum = ev[1] if kind in ENUM_KINDS else None
        elif tag == "label" and last_enum is not None:
            labels.setdefault(last_enum, []).append(ev[1])
        elif tag == "base" and ev[1] != owner and ev[1] not in bases:
            bases.append(ev[1])
        elif tag == "inherit" and ev[1] not in inherits:
            inherits.append(ev[1])

    rows = []
    for mid, parent in pushed:
        s = slots[mid]
        vt = s.get(OFF_VPTR)
        if vt and vt[0] == "vtable":
            desc = describe_handler(vt[1])
            mangled, vtable = vt[2], vt[3]
        else:
            desc = describe_handler(None)
            mangled = vtable = None
        kind = desc["kind"]
        name = slot_str(s, OFF_NAME)
        callback = member_pointer(s.get(OFF_OFFSET)) if kind in OFFSETLESS_KINDS else None
        offset = None if kind in OFFSETLESS_KINDS else slot_int(s, OFF_OFFSET)
        row = {
            "kind": kind,
            "owner": owner,
            "registrar": registrar,
            "registrar_addr": registrar_addr,
            "index": allocs[mid][0],
            "name": name,
            "offset": offset,
            "alloc_size": allocs[mid][1],
            "handler_vtable": vtable,
            "handler_symbol": mangled,
            "value_type": desc["value_type"],
            "handler": desc["handler"],
            "wrapped": desc["wrapped"],
            "flags": desc["flags"],
            "parent": allocs[parent][0] if parent in allocs else None,
            "element_index": slot_int(s, OFF_ARG) if kind == "COffsetMember" else None,
            "setter": member_pointer(s.get(OFF_ARG)) if kind in ACCESSOR_KINDS else None,
            "getter": member_pointer(s.get(OFF_GETTER))
            if kind in ACCESSOR_KINDS or kind == "CConditionalGroupMember" else None,
            "callback": callback,
            "child_name": slot_str(s, OFF_CHILD_NAME) if kind in CONTAINER_KINDS else None,
            "child_name_2": slot_str(s, OFF_CHILD_NAME_2) if kind in CONTAINER_KINDS else None,
            "labels": labels.get(mid),
            "complete": name is not None and kind is not None and (
                offset is not None or kind in OFFSETLESS_KINDS),
        }
        rows.append(row)

    klass = {
        "owner": owner,
        "registrar": registrar,
        "registrar_addr": registrar_addr,
        "bases": bases,
        "inherits": inherits,
        "copies_base_members": bool(inherits),
        "own_members": len(rows),
        "unpushed": len(allocs) - len(pushed),
    }
    return rows, klass


# ---------------------------------------------------------------------------
# Ghidra-side work
# ---------------------------------------------------------------------------
class Extractor(object):
    def __init__(self, program, monitor):
        self.program = program
        self.monitor = monitor
        self.fm = program.getFunctionManager()
        self.st = program.getSymbolTable()
        self.listing = program.getListing()
        self.memory = program.getMemory()
        self.space = program.getAddressFactory().getDefaultAddressSpace()
        self.decomp = DecompInterface()
        self.decomp.openProgram(program)

    def addr(self, value):
        try:
            return self.space.getAddress(value)
        except Exception:
            return None

    def read_word(self, value):
        try:
            return int(self.memory.getInt(self.addr(value))) & 0xFFFFFFFF
        except Exception:
            return None

    def read_cstring(self, value):
        out = []
        try:
            a = self.addr(value)
            for i in range(MAX_CSTRING):
                b = self.memory.getByte(a.add(i)) & 0xFF
                if b == 0:
                    break
                if b < 0x20 or b > 0x7E:
                    return None
                out.append(chr(b))
        except Exception:
            return None
        return "".join(out)

    def is_string_data(self, value):
        a = self.addr(value)
        data = self.listing.getDataContaining(a) if a is not None else None
        return data is not None and data.hasStringValue()

    def function_at(self, value):
        a = self.addr(value)
        f = self.fm.getFunctionAt(a) if a is not None else None
        return jstr(f.getName(True)) if f is not None else None

    def vtable_at(self, value):
        """vtable+8 -> (demangled namespace, _ZTV symbol, 0xaddr) or None."""
        base = value - VTABLE_HEADER
        demangled = mangled = None
        for sym in self.st.getSymbols(self.addr(base)):
            name = jstr(sym.getName(False))
            if name == "vtable":
                demangled = jstr(sym.getParentNamespace().getName(True))
            elif name and name.startswith("_ZTV"):
                mangled = name
        if demangled is None:
            return None
        return ("vtable", demangled, mangled, "0x%x" % base)

    # -- p-code value tracking ---------------------------------------------
    def unwrap(self, vn):
        while vn is not None and vn.getDef() is not None:
            op = vn.getDef()
            if op.getOpcode() in (PcodeOp.CAST, PcodeOp.COPY):
                vn = op.getInput(0)
                continue
            break
        return vn

    def base_offset(self, vn):
        """Address expression -> (base varnode, constant byte offset)."""
        off = 0
        vn = self.unwrap(vn)
        while vn is not None and vn.getDef() is not None:
            op = vn.getDef()
            code = op.getOpcode()
            if code in (PcodeOp.PTRSUB, PcodeOp.INT_ADD):
                a, b = op.getInput(0), op.getInput(1)
                if b.isConstant():
                    off += int(b.getOffset())
                    vn = self.unwrap(a)
                    continue
                if a.isConstant():
                    off += int(a.getOffset())
                    vn = self.unwrap(b)
                    continue
            elif code == PcodeOp.PTRADD:
                a, b, c = op.getInput(0), op.getInput(1), op.getInput(2)
                if b.isConstant() and c.isConstant():
                    off += int(b.getOffset()) * int(c.getOffset())
                    vn = self.unwrap(a)
                    continue
            break
        return vn, off

    def value_of(self, vn, depth=0):
        """Constant value of a varnode, reading through the PIC GOT."""
        vn = self.unwrap(vn)
        if vn is None or depth > 6:
            return None
        if vn.isConstant():
            return int(vn.getOffset()) & 0xFFFFFFFF
        if vn.isAddress():
            # A ram varnode is a global read: here, a GOT slot.
            return self.read_word(int(vn.getOffset()))
        op = vn.getDef()
        if op is None:
            return None
        code = op.getOpcode()
        if code in (PcodeOp.INT_ADD, PcodeOp.PTRSUB):
            a = self.value_of(op.getInput(0), depth + 1)
            b = self.value_of(op.getInput(1), depth + 1)
            return (a + b) & 0xFFFFFFFF if a is not None and b is not None else None
        if code == PcodeOp.PTRADD:
            a = self.value_of(op.getInput(0), depth + 1)
            i, size = op.getInput(1), op.getInput(2)
            if a is None or not (i.isConstant() and size.isConstant()):
                return None
            return (a + int(i.getOffset()) * int(size.getOffset())) & 0xFFFFFFFF
        if code == PcodeOp.LOAD:
            ptr = self.value_of(op.getInput(1), depth + 1)
            return self.read_word(ptr) if ptr is not None else None
        return None

    def slot_value(self, off, value):
        if off == OFF_VPTR:
            return self.vtable_at(value)
        if off in (OFF_NAME, OFF_CHILD_NAME, OFF_CHILD_NAME_2) and value > 0xFFFF:
            text = self.read_cstring(value)
            if text is not None:
                return ("str", text)
        if value > 0xFFFF:
            func = self.function_at(value)
            if func is not None:
                return ("func", func)
        return ("int", value)

    def callee(self, op):
        target = op.getInput(0)
        if not target.isAddress():
            return None, None
        f = self.fm.getFunctionAt(target.getAddress())
        if f is None:
            return None, None
        return jstr(f.getName(False)), jstr(f.getParentNamespace().getName(True))

    def symbol_owner(self, value):
        """ms_descriptor address -> the class that owns it."""
        sym = self.st.getPrimarySymbol(self.addr(value)) if value is not None else None
        return jstr(sym.getParentNamespace().getName(True)) if sym is not None else None

    def events_for(self, func):
        res = self.decomp.decompileFunction(func, 120, self.monitor)
        high = res.getHighFunction() if res is not None else None
        if high is None:
            return None
        ops = []
        it = high.getPcodeOps()
        while it.hasNext():
            ops.append(it.next())

        ids = {}
        events = []

        def member_id(vn):
            key = self.unwrap(vn)
            return ids.get(key.getUniqueId()) if key is not None else None

        for op in ops:
            code = op.getOpcode()
            if code == PcodeOp.CALL:
                name, ns = self.callee(op)
                if name == "NMalloc" and op.getOutput() is not None:
                    mid = len(ids)
                    ids[op.getOutput().getUniqueId()] = mid
                    events.append(("alloc", mid, self.value_of(op.getInput(1))))
                elif name == "PushBackMember" and op.getNumInputs() >= 3:
                    mid = member_id(op.getInput(2))
                    if mid is None:
                        continue
                    base, _ = self.base_offset(op.getInput(1))
                    parent = ids.get(base.getUniqueId()) if base is not None else None
                    events.append(("push", mid, parent))
                elif name == "PushBackMembers" and op.getNumInputs() >= 3:
                    src = self.symbol_owner(self.value_of(op.getInput(2)))
                    if src:
                        events.append(("inherit", src))
                elif name == REGISTER_FN and ns:
                    events.append(("base", ns))
            elif code == PcodeOp.STORE:
                base, off = self.base_offset(op.getInput(1))
                value = self.value_of(op.getInput(2))
                mid = ids.get(base.getUniqueId()) if base is not None else None
                if value is None:
                    continue
                if mid is not None:
                    slot = self.slot_value(off, value)
                    if slot is not None:
                        events.append(("slot", mid, off, slot))
                elif value > 0xFFFF and self.is_string_data(value):
                    text = self.read_cstring(value)
                    if text:
                        events.append(("label", text))
        return events

    def find_registrars(self):
        out = []
        it = self.fm.getFunctions(True)
        while it.hasNext() and not self.monitor.isCancelled():
            f = it.next()
            if jstr(f.getName(False)) != REGISTER_FN:
                continue
            ns = f.getParentNamespace()
            owner = None
            if ns is not None and not ns.isGlobal():
                owner = jstr(ns.getName(True))
            out.append((f, owner))
        return out

    def dispose(self):
        try:
            self.decomp.dispose()
        except Exception:
            pass


# ---------------------------------------------------------------------------
# output
# ---------------------------------------------------------------------------
def write_jsonl(path, rows):
    with open(path, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=True) + "\n")


def build_report(stats, rows, classes):
    owners = {r["owner"] for r in rows if r["owner"]}
    complete = [r for r in rows if r["complete"]]
    named = [r for r in rows if r["name"]]
    typed = [r for r in rows if r["kind"]]
    with_bases = [c for c in classes if c["bases"]]
    enums = [r for r in rows if r["labels"]]
    lines = [
        "registrars found      : %d" % stats["registrars"],
        "  parsed              : %d" % stats["parsed"],
        "  decompile failed    : %d" % stats["decompile_failed"],
        "  no own members      : %d" % stats["no_own_members"],
        "    of which inherit-only: %d" % stats["inherit_only"],
        "unpushed descriptors  : %d" % stats["leftovers"],
        "",
        "member rows           : %d" % len(rows),
        "  complete            : %d" % len(complete),
        "  with a name         : %d" % len(named),
        "  with a kind         : %d" % len(typed),
        "  enums with labels   : %d" % len(enums),
        "classes with own fields: %d" % len(owners),
        "classes with a base    : %d" % len(with_bases),
        "",
        "== descriptor kinds ==",
    ]
    kinds = {}
    for r in rows:
        k = r["kind"] or "<unresolved>"
        kinds[k] = kinds.get(k, 0) + 1
    for k in sorted(kinds, key=lambda k: -kinds[k]):
        lines.append("  %-30s %5d" % (k, kinds[k]))
    lines += ["", "== flag bits (1 load, 2 save, 4 load state, 8 save state) =="]
    flags = {}
    for r in rows:
        flags[r["flags"]] = flags.get(r["flags"], 0) + 1
    for f in sorted(flags, key=lambda f: -flags[f]):
        lines.append("  %-6s %5d" % (f, flags[f]))
    if len(rows) and len(complete) != len(rows):
        lines += ["", "Incomplete rows are kept in the output with "
                      "complete=false; inspect before trusting coverage."]
    return "\n".join(lines)


def run(program, monitor, outdir):
    name = jstr(program.getName())
    nfuncs = program.getFunctionManager().getFunctionCount()
    print("[*] %s, %d functions" % (name, nfuncs))
    if nfuncs == 0:
        raise SystemExit(
            "[!] 0 functions: this is not your analyzed program.\n"
            "    Either the wrong program was opened, or it was imported "
            "fresh and never analyzed.")

    ex = Extractor(program, monitor)
    try:
        registrars = ex.find_registrars()
        print("[*] %d %s functions" % (len(registrars), REGISTER_FN))
        monitor.initialize(len(registrars))

        rows = []
        classes = []
        stats = {"registrars": len(registrars), "parsed": 0,
                 "decompile_failed": 0, "no_own_members": 0,
                 "inherit_only": 0, "leftovers": 0}
        for func, owner in registrars:
            if monitor.isCancelled():
                break
            monitor.incrementProgress(1)
            events = ex.events_for(func)
            if events is None:
                stats["decompile_failed"] += 1
                continue
            got, klass = assemble(events, owner, jstr(func.getName(True)),
                                  jstr(func.getEntryPoint().toString()))
            stats["parsed"] += 1
            stats["leftovers"] += klass["unpushed"]
            if not got:
                stats["no_own_members"] += 1
                if klass["bases"] or klass["copies_base_members"]:
                    stats["inherit_only"] += 1
            rows.extend(got)
            classes.append(klass)
    finally:
        ex.dispose()

    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, "register_properties.jsonl")
    write_jsonl(out, rows)
    write_jsonl(os.path.join(outdir, "register_properties_classes.jsonl"),
                classes)
    report = build_report(stats, rows, classes)
    with open(os.path.join(outdir, "register_properties_report.txt"),
              "w", encoding="utf-8") as fh:
        fh.write(report + "\n")
    print()
    print(report)
    print()
    print("[+] done -> %s" % out)


# ---------------------------------------------------------------------------
# entry points
# ---------------------------------------------------------------------------
def main_script():
    bind_java_types()
    g = globals()
    argv = [jstr(a) for a in (g.get("getScriptArgs", lambda: [])() or [])]
    if argv:
        outdir = argv[0]
    else:
        outdir = g["askDirectory"]("Property dump output directory",
                                   "Choose").getAbsolutePath()
    run(g["currentProgram"], g["monitor"], outdir)


def main_headless():
    import argparse

    ap = argparse.ArgumentParser(
        description="Dump Nomad property descriptors from an EXISTING, "
                    "already analyzed program. Never imports, never writes.")
    ap.add_argument("outdir")
    ap.add_argument("project_location",
                    help="directory containing the .gpr / .rep")
    ap.add_argument("project_name", help="project name, without .gpr")
    ap.add_argument("program", help="program path, e.g. /FarCry2_server")
    args = ap.parse_args()

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
