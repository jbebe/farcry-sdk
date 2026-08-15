# Dump Nomad property descriptors from CLASS::RegisterProperties (PyGhidra).
#
# A registering class builds one CMemberBase per serialized field and pushes
# it into the class descriptor. The construction is straight-line and carries
# the field's name, byte offset and handler type:
#
#   p = (CMemberBase *)CMemMng::NMalloc(0x14, 0);
#   *(char **)(p + 4) = "BarkEventTag";
#   *(undefined4 *)(p + 0xc) = 0;            <- byte offset in the owner
#   *(undefined ***)p = &PTR_Load_0a3a3468;  <- handler vtable, names the type
#   CNomadObjectDescriptor::PushBackMember((CryVector *)ms_descriptor, p);
#
# Output: register_properties.jsonl (one row per field) and a coverage report.
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

# Descriptor layout, byte offsets into CMemberBase.
OFF_VPTR = 0x00
OFF_NAME = 0x04
OFF_NAME_ID = 0x08
OFF_OFFSET = 0x0C
OFF_FLAGS = 0x10
OFF_CHILD_NAME = 0x14
OFF_CHILD_NAME_2 = 0x1C

# An Itanium vtable pointer targets the first virtual slot, past the
# offset-to-top and typeinfo words.
VTABLE_HEADER = 8

MAX_CSTRING = 512

DecompInterface = None
ConsoleTaskMonitor = None


def bind_java_types():
    global DecompInterface, ConsoleTaskMonitor
    from ghidra.app.decompiler import DecompInterface as _DI
    from ghidra.util.task import ConsoleTaskMonitor as _CTM
    DecompInterface = _DI
    ConsoleTaskMonitor = _CTM


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
RE_ALLOC = re.compile(
    r"^\s*(?P<var>\w+)\s*=\s*(?:\([^()]*\)\s*)?"
    r"(?:\w+::)?NMalloc\(\s*(?P<size>0x[0-9a-fA-F]+|\d+)")
RE_STORE_OFF = re.compile(
    r"^\s*\*\((?P<cast>[^()]*)\)\s*\(\s*(?P<var>\w+)\s*\+\s*"
    r"(?P<off>0x[0-9a-fA-F]+|\d+)\s*\)\s*=\s*(?P<val>.+?);\s*$")
RE_STORE_BASE = re.compile(
    r"^\s*\*\((?P<cast>[^()]*)\)\s*(?P<var>\w+)\s*=\s*(?P<val>.+?);\s*$")
RE_PUSHBACK = re.compile(
    r"PushBackMember\([^;]*?,\s*(?P<var>\w+)\s*\)\s*;")
RE_ASSIGN = re.compile(r"^\s*(?P<var>\w+)\s*=\s*(?P<val>.+?);\s*$")

# A class with no fields of its own just replays its base's registration and
# copies the base member list wholesale.
RE_BASE_REGISTER = re.compile(
    r"(?P<base>[A-Za-z_]\w*(?:::[A-Za-z_]\w*)*)::RegisterProperties\(\s*\)")
RE_PUSHBACK_ALL = re.compile(r"\bPushBackMembers\b")


def to_statements(lines):
    """Decompiled C -> one whitespace-normalised statement per element.

    Ghidra wraps long calls across lines, so matching per line splits
    `PushBackMember` from its arguments.
    """
    joined = " ".join(line.strip() for line in lines)
    # The leading block comment repeats the signature with empty parens, which
    # otherwise reads as the class registering itself as its own base.
    joined = re.sub(r"/\*.*?\*/", " ", joined, flags=re.S)
    return [re.sub(r"\s+", " ", s).strip() + ";" for s in joined.split(";")]

RE_STR_LITERAL = re.compile(r'^"(?P<s>.*)"$', re.S)
RE_INT_LITERAL = re.compile(r"^(?P<n>-?0x[0-9a-fA-F]+|-?\d+)$")
RE_SYMBOL_REF = re.compile(r"^&?(?P<sym>[A-Za-z_]\w*)$")
RE_TRAILING_ADDR = re.compile(r"_(?P<addr>[0-9a-fA-F]{6,16})$")
RE_ZTV_NAME = re.compile(r"^_ZTV(?P<len>\d+)(?P<rest>.+)$")

# Descriptor kinds that describe something other than a field at an offset, so
# a missing offset is correct rather than a parse failure.
OFFSETLESS_KINDS = frozenset(("CSerializationEvent",))

# PIC code reaches a vtable through the GOT, so the vptr store reads either
# `PTR_vtable_<gotslot> + 8` (indirect, the common form) or `&PTR_x_<vtable+8>`
# (direct, when the decompiler already folded the load).
RE_VPTR = re.compile(
    r"^(?P<amp>&?)(?P<sym>[A-Za-z_]\w*)"
    r"(?:\s*\+\s*(?P<add>0x[0-9a-fA-F]+|\d+))?$")


def parse_int(text):
    m = RE_INT_LITERAL.match(text.strip())
    if not m:
        return None
    n = m.group("n")
    return int(n, 16) if "x" in n.lower() else int(n)


def unescape(s):
    """Undo the C escaping Ghidra applies to string literals."""
    out = []
    i = 0
    while i < len(s):
        ch = s[i]
        if ch != "\\" or i + 1 >= len(s):
            out.append(ch)
            i += 1
            continue
        nxt = s[i + 1]
        simple = {"n": "\n", "t": "\t", "r": "\r", "0": "\0",
                  "\\": "\\", '"': '"', "'": "'"}
        if nxt in simple:
            out.append(simple[nxt])
            i += 2
        elif nxt == "x":
            hexpart = s[i + 2:i + 4]
            try:
                out.append(chr(int(hexpart, 16)))
                i += 2 + len(hexpart)
            except ValueError:
                out.append(ch)
                i += 1
        else:
            out.append(nxt)
            i += 2
    return "".join(out)


def kind_from_handler(symbol):
    """_ZTV14CGenericMemberI4Bark... -> CGenericMember.

    Itanium mangling length-prefixes the name, so the count is exact rather
    than a guess at where the template arguments start.
    """
    m = RE_ZTV_NAME.match(symbol or "")
    if not m:
        return None
    n = int(m.group("len"))
    rest = m.group("rest")
    return rest[:n] if len(rest) >= n else None


def symbol_trailing_addr(name):
    """PTR_Load_0a3a3468 -> 0x0a3a3468. Ghidra appends the target address."""
    m = RE_TRAILING_ADDR.search(name or "")
    return int(m.group("addr"), 16) if m else None


class Descriptor(object):
    __slots__ = ("var", "alloc_size", "slots", "order")

    def __init__(self, var, alloc_size, order):
        self.var = var
        self.alloc_size = alloc_size
        self.slots = {}
        self.order = order


def parse_registrations(lines):
    """Decompiled RegisterProperties body -> descriptors, in push order.

    Records are bracketed by NMalloc and PushBackMember and keyed by the
    variable holding the pointer, so interleaved construction still resolves.
    Later writes to a slot win, which is what makes the real handler vtable
    override the base-constructor one.
    """
    live = {}
    aliases = {}
    done = []
    bases = []
    copies_base_members = False
    order = 0

    for line in to_statements(lines):
        for m in RE_BASE_REGISTER.finditer(line):
            base = m.group("base")
            if base not in bases:
                bases.append(base)
        if RE_PUSHBACK_ALL.search(line):
            copies_base_members = True

        m = RE_ALLOC.match(line)
        if m:
            var = m.group("var")
            live[var] = Descriptor(var, parse_int(m.group("size")), order)
            order += 1
            continue

        m = RE_STORE_OFF.match(line) or RE_STORE_BASE.match(line)
        if m:
            var = m.group("var")
            if var in live:
                off = parse_int(m.groupdict().get("off") or "0") or 0
                # Resolved here, not at emit time: a variable reassigned later
                # in the function would otherwise overwrite the value that was
                # actually live at this store.
                live[var].slots[off] = resolve_value(m.group("val"), aliases)
                continue

        for m in RE_PUSHBACK.finditer(line):
            var = m.group("var")
            if var in live:
                done.append(live.pop(var))
        if RE_PUSHBACK.search(line):
            continue

        m = RE_ASSIGN.match(line)
        if m and m.group("var") not in live:
            aliases[m.group("var")] = m.group("val").strip()

    # Anything never pushed is still reported; the caller counts it as a miss.
    leftovers = sorted(live.values(), key=lambda d: d.order)
    return done, leftovers, {"bases": bases,
                             "copies_base_members": copies_base_members}


def resolve_value(raw, aliases, depth=0):
    """Follow simple assignment chains to a literal or symbol reference."""
    if raw is None or depth > 4:
        return None
    raw = raw.strip()
    if RE_STR_LITERAL.match(raw) or RE_INT_LITERAL.match(raw):
        return raw
    m = RE_SYMBOL_REF.match(raw)
    if m and not raw.startswith("&"):
        target = aliases.get(m.group("sym"))
        if target is not None and target != raw:
            return resolve_value(target, aliases, depth + 1)
    return raw


# ---------------------------------------------------------------------------
# Ghidra-side work
# ---------------------------------------------------------------------------
class Extractor(object):
    def __init__(self, program, monitor):
        self.program = program
        self.monitor = monitor
        self.fm = program.getFunctionManager()
        self.st = program.getSymbolTable()
        self.memory = program.getMemory()
        self.af = program.getAddressFactory()
        self.decomp = DecompInterface()
        self.decomp.openProgram(program)

    def to_addr(self, value):
        try:
            return self.af.getDefaultAddressSpace().getAddress(value)
        except Exception:
            return None

    def read_cstring(self, addr):
        if addr is None:
            return None
        out = []
        try:
            for i in range(MAX_CSTRING):
                b = self.memory.getByte(addr.add(i)) & 0xFF
                if b == 0:
                    break
                out.append(chr(b))
        except Exception:
            return None
        return "".join(out) if out else None

    def string_value(self, raw):
        """A name slot is either an inline literal or a reference to .rodata."""
        if raw is None:
            return None
        m = RE_STR_LITERAL.match(raw)
        if m:
            return unescape(m.group("s"))
        m = RE_SYMBOL_REF.match(raw)
        if not m:
            return None
        addr_val = symbol_trailing_addr(m.group("sym"))
        return self.read_cstring(self.to_addr(addr_val)) if addr_val else None

    def read_pointer(self, addr):
        try:
            return int(self.memory.getInt(addr)) & 0xFFFFFFFF
        except Exception:
            return None

    def handler_from_vptr(self, raw):
        """Vtable pointer -> the mangled _ZTV symbol naming the handler."""
        if raw is None:
            return None, None
        m = RE_VPTR.match(raw.strip())
        if not m:
            return None, None
        anchor = symbol_trailing_addr(m.group("sym"))
        if anchor is None:
            return None, None
        if m.group("add"):
            # Indirect: the symbol is a GOT slot holding the vtable base, and
            # the addend is the header skip already accounted for.
            base = self.read_pointer(self.to_addr(anchor))
        else:
            base = anchor - VTABLE_HEADER
        if base is None:
            return None, None
        vtable = self.to_addr(base)
        addr_text = "0x%x" % base
        if vtable is None:
            return addr_text, None
        # Only a _ZTV symbol counts. Anything else means the pointer was not a
        # vtable at all -- PIC code is full of PTR_<target>_<gotslot> names
        # that would otherwise resolve to plausible-looking nonsense.
        for sym in self.st.getSymbols(vtable):
            name = jstr(sym.getName(False))
            if name and name.startswith("_ZTV"):
                return addr_text, name
        return addr_text, None

    def child_name(self, desc, off):
        if not desc.alloc_size or desc.alloc_size < off + 4:
            return None
        return self.string_value(desc.slots.get(off))

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

    def decompile(self, func):
        res = self.decomp.decompileFunction(func, 120, self.monitor)
        if res is None or not res.decompileCompleted():
            return None
        high = res.getDecompiledFunction()
        return jstr(high.getC()) if high is not None else None

    def rows_for(self, func, owner):
        text = self.decompile(func)
        if text is None:
            return None, None, "decompile_failed", 0

        pushed, leftovers, meta = parse_registrations(text.splitlines())
        rows = []
        for desc in pushed:
            get = desc.slots.get
            name = self.string_value(get(OFF_NAME))
            offset = parse_int(get(OFF_OFFSET) or "")
            vtable, handler = self.handler_from_vptr(get(OFF_VPTR))
            kind = kind_from_handler(handler)
            rows.append({
                "kind": kind,
                "owner": owner,
                "registrar": jstr(func.getName(True)),
                "registrar_addr": jstr(func.getEntryPoint().toString()),
                "index": desc.order,
                "name": name,
                "offset": offset,
                "flags": parse_int(get(OFF_FLAGS) or ""),
                "alloc_size": desc.alloc_size,
                "handler_vtable": vtable,
                "handler_symbol": handler,
                # Descriptors come in six sizes; the child-element slots only
                # exist on the larger ones, and reading them off a short
                # descriptor would invent names out of neighbouring data.
                "child_name": self.child_name(desc, OFF_CHILD_NAME),
                "child_name_2": self.child_name(desc, OFF_CHILD_NAME_2),
                "complete": name is not None and (
                    offset is not None or kind in OFFSETLESS_KINDS),
            })
        klass = {
            "owner": owner,
            "registrar": jstr(func.getName(True)),
            "registrar_addr": jstr(func.getEntryPoint().toString()),
            "bases": [b for b in meta["bases"] if b != owner],
            "copies_base_members": meta["copies_base_members"],
            "own_members": len(rows),
            "unpushed": len(leftovers),
        }
        return rows, klass, None, len(leftovers)

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
    typed = [r for r in rows if r["handler_symbol"]]
    with_bases = [c for c in classes if c["bases"]]
    lines = [
        "registrars found      : %d" % stats["registrars"],
        "  parsed              : %d" % stats["parsed"],
        "  decompile failed    : %d" % stats["decompile_failed"],
        "  no own members      : %d" % stats["no_own_members"],
        "    of which inherit-only: %d" % stats["inherit_only"],
        "unpushed descriptors  : %d" % stats["leftovers"],
        "",
        "member rows           : %d" % len(rows),
        "  with name + offset  : %d" % len(complete),
        "  with a name         : %d" % len(named),
        "  with a handler type : %d" % len(typed),
        "classes with own fields: %d" % len(owners),
        "classes with a base    : %d" % len(with_bases),
        "",
        "== descriptor kinds ==",
    ]
    kinds = {}
    for r in rows:
        k = r["kind"] or "<unresolved>"
        slot = kinds.setdefault(k, [0, 0])
        slot[0] += 1
        if r["offset"] is not None:
            slot[1] += 1
    for k in sorted(kinds, key=lambda k: -kinds[k][0]):
        total, with_off = kinds[k]
        lines.append("  %-26s %5d  (%d with offset)" % (k, total, with_off))
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
            got, klass, err, leftovers = ex.rows_for(func, owner)
            if err:
                stats["decompile_failed"] += 1
                continue
            stats["parsed"] += 1
            stats["leftovers"] += leftovers
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
