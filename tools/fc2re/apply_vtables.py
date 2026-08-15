# Apply harvested vtables to the Ghidra type database.
#
# For each subobject table a `<Class>_vtable` struct of named function-pointer
# slots is created, and a vptr field pointing at it is placed in the class
# struct. A table whose offset-to-top is -T describes the base subobject that
# sits at offset T, so secondary vptrs get named as well as the primary.
#
# Slots share one generic `vfunc` function definition rather than a per-slot
# signature: the method signatures are not recovered yet, so a distinct type
# per slot would add ~110k types for no information. The win here is that the
# slot is named, which turns (**(code **)(*p + 4))(p) into p->vtbl->Update.
#
# SAFE BY DEFAULT: without --write this reports what it would do, opens no
# transaction and saves nothing.
#
#   python apply_vtables.py out\vtables.jsonl C:\projdir fc2 /FarCry2_server
#   # add --write once the dry run looks right
#
# @category FC2RE
# @runtime PyGhidra

import argparse
import json
import os
import re
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from dump_properties import jstr

MARKER = "[fc2re:vtable]"
VTABLE_CATEGORY = "/fc2re/vtables"
VFUNC_NAME = "vfunc"
POINTER_SIZE = 4

# A table with more slots than this is a misparse, not a class.
MAX_SLOTS = 1024

# Past this a recorded size is a buffer that borrowed a class name, not an
# object.
MAX_TRUSTED_SIZE = 0x20000

CategoryPath = None
StructureDataType = None
PointerDataType = None
FunctionDefinitionDataType = None
Undefined4 = None
DataTypeConflictHandler = None


def bind_java_types():
    global CategoryPath, StructureDataType, PointerDataType, \
        FunctionDefinitionDataType, Undefined4, DataTypeConflictHandler
    from ghidra.program.model.data import CategoryPath as _CP
    from ghidra.program.model.data import StructureDataType as _SDT
    from ghidra.program.model.data import PointerDataType as _PDT
    from ghidra.program.model.data import \
        FunctionDefinitionDataType as _FDDT
    from ghidra.program.model.data import Undefined4DataType as _U4
    from ghidra.program.model.data import DataTypeConflictHandler as _DTCH
    CategoryPath = _CP
    StructureDataType = _SDT
    PointerDataType = _PDT
    FunctionDefinitionDataType = _FDDT
    Undefined4 = _U4
    DataTypeConflictHandler = _DTCH


# ---------------------------------------------------------------------------
# pure logic -- kept Ghidra-free so it can be unit tested without a JVM
# ---------------------------------------------------------------------------
RE_BAD_CHARS = re.compile(r"[/\\\s]")


def split_scopes(name):
    """Template-aware split on '::'.

    CryVector<CEntitySystem::DeathRowCell,NoLock>::CodeObject is two scopes,
    not three -- a naive split cuts inside the template argument and the
    resulting category path matches nothing.
    """
    parts = []
    buf = []
    depth = 0
    i = 0
    while i < len(name):
        ch = name[i]
        if ch == "<":
            depth += 1
        elif ch == ">":
            depth = max(0, depth - 1)
        elif depth == 0 and name[i:i + 2] == "::":
            parts.append("".join(buf))
            buf = []
            i += 2
            continue
        buf.append(ch)
        i += 1
    parts.append("".join(buf))
    return [p for p in parts if p]


def type_name(class_name, offset_to_top):
    """CFoo -> CFoo_vtable; secondary tables carry their subobject offset."""
    base = RE_BAD_CHARS.sub("_", class_name)
    if offset_to_top:
        return "%s_vtable_at_%x" % (base, -offset_to_top)
    return "%s_vtable" % base


def field_name(qualified, taken):
    """CFoo::Update -> Update, kept unique within one table.

    Itanium emits two entries for a virtual destructor -- the complete-object
    and deleting forms, both demangling to ~CFoo -- so duplicates are real and
    must be disambiguated rather than dropped.
    """
    simple = qualified.rsplit("::", 1)[-1] if qualified else "slot"
    if simple.startswith("~"):
        simple = "dtor_" + simple[1:]
    simple = RE_BAD_CHARS.sub("_", simple)
    if not simple or simple[0].isdigit():
        simple = "_" + simple
    name = simple
    n = 2
    while name in taken:
        name = "%s_%d" % (simple, n)
        n += 1
    taken.add(name)
    return name


def vptr_field(offset_to_top):
    """A table at offset-to-top -T describes the subobject at offset T."""
    at = -offset_to_top
    return "vptr" if at == 0 else "vptr_%x" % at, at


def plan(row):
    """One vtable row -> [(vptr offset, type name, [slot names])]."""
    cls = row.get("class")
    if not cls:
        return []
    out = []
    for sub in row.get("subobjects", []):
        fns = sub.get("functions") or []
        if not fns or len(fns) > MAX_SLOTS:
            continue
        taken = set()
        slots = [field_name(f.get("name"), taken) for f in fns]
        name, at = vptr_field(sub.get("offset_to_top") or 0)
        out.append({
            "vptr_offset": at,
            "vptr_name": name,
            "type_name": type_name(cls, sub.get("offset_to_top") or 0),
            "slots": slots,
        })
    return out


def load_jsonl(path):
    with open(path, "r", encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


# ---------------------------------------------------------------------------
# Ghidra-side work
# ---------------------------------------------------------------------------
class Applier(object):
    def __init__(self, program):
        self.dtm = program.getDataTypeManager()
        self.st = program.getSymbolTable()
        self.base = program.getImageBase()
        self.category = CategoryPath(VTABLE_CATEGORY)
        self.by_name = defaultdict(list)
        it = self.dtm.getAllStructures()
        while it.hasNext():
            s = it.next()
            self.by_name[jstr(s.getName())].append(s)
        self.vfunc_ptr = None

    def class_at(self, addr_hex):
        """The demangled class owning a _ZTV address.

        Ghidra's demangler already produced `<Class>::vtable` here, so its
        parent namespace is the class in exactly the spelling the type
        database uses -- template arguments included. Re-deriving that from
        the mangled name would mean writing a second Itanium demangler and
        getting `hkpSymmetricAgent<I19hkpHeightFieldAgentE>` wrong.
        """
        try:
            a = self.base.getNewAddress(int(addr_hex, 16))
        except Exception:
            return None
        for s in self.st.getSymbols(a):
            ns = s.getParentNamespace()
            if ns is not None and not ns.isGlobal():
                return jstr(ns.getName(True))
        return None

    def ensure_vfunc(self):
        """One shared function definition backs every slot."""
        if self.vfunc_ptr is None:
            fd = FunctionDefinitionDataType(CategoryPath(VTABLE_CATEGORY),
                                            VFUNC_NAME, self.dtm)
            fd.setReturnType(Undefined4.dataType)
            stored = self.dtm.addDataType(
                fd, DataTypeConflictHandler.KEEP_HANDLER)
            self.vfunc_ptr = PointerDataType(stored, POINTER_SIZE, self.dtm)
        return self.vfunc_ptr

    def resolve_class(self, owner):
        """The struct for a class, by full path rather than leaf name.

        Nested types are filed under their enclosing scope as a category, and
        not always beneath /Demangler -- CryVector<X>::CodeObject sits at
        /CryVector<X>/CodeObject. Matching on the leaf alone makes every one
        of the ~756 CodeObject structs a candidate.
        """
        scopes = split_scopes(owner)
        if not scopes:
            return None, "no_struct"
        pool = self.by_name.get(scopes[-1]) or []
        if not pool:
            return None, "no_struct"
        scoped = "/".join(scopes)
        wanted = ("/Demangler/%s" % scoped, "/%s" % scoped)
        preferred = [s for s in pool if jstr(s.getPathName()) in wanted]
        if len(preferred) == 1:
            return preferred[0], None
        if preferred:
            return None, "ambiguous_path_%d" % len(preferred)
        if len(pool) == 1:
            return pool[0], None
        return None, "ambiguous_%d" % len(pool)

    def build_table(self, spec):
        """Create or replace the <Class>_vtable struct."""
        slot_ptr = self.ensure_vfunc()
        st = StructureDataType(self.category, spec["type_name"], 0, self.dtm)
        taken = set()
        for name in spec["slots"]:
            st.add(slot_ptr, POINTER_SIZE, name, None)
            taken.add(name)
        st.setDescription("%s %d virtual slots" % (MARKER, len(spec["slots"])))
        return self.dtm.addDataType(st,
                                    DataTypeConflictHandler.REPLACE_HANDLER)

    def place_vptr(self, struct, spec, table, opts, want_size=0):
        # Growing only to the vptr leaves a 4-byte struct, and Ghidra renders
        # every access past it as this[N].vptr -- a member name attached to an
        # offset it does not describe. The allocation-site size avoids that.
        end = max(spec["vptr_offset"] + POINTER_SIZE, want_size)
        for _ in range(4):
            have = struct.getLength()
            if have >= end:
                break
            struct.growStructure(end - have)
        if struct.getLength() < end:
            return "error_grow_failed"

        existing = struct.getComponentAt(spec["vptr_offset"])
        if existing is not None and existing.getFieldName():
            current = jstr(existing.getFieldName())
            if not current.startswith("vptr") and not opts.force:
                return "skipped_offset_taken:%s" % current

        struct.replaceAtOffset(
            spec["vptr_offset"],
            PointerDataType(table, POINTER_SIZE, self.dtm),
            POINTER_SIZE, spec["vptr_name"], MARKER)
        return "placed"


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
def load_sizes(path):
    if not path:
        return {}
    return {r["class"]: r["size"] for r in load_jsonl(path)
            if r.get("size") and r["size"] <= MAX_TRUSTED_SIZE}


def size_for(sizes, owner):
    return sizes.get(owner) or sizes.get(split_scopes(owner)[-1], 0)


def run(program, rows, opts):
    ap = Applier(program)
    sizes = load_sizes(getattr(opts, "sizes", None))
    if sizes:
        print("[*] %d classes have a size (exact or derived bound)"
              % len(sizes))
    counts = defaultdict(int)
    problems = []
    tables = slots = 0

    tx = program.startTransaction("fc2re: apply vtables") \
        if opts.write else None
    processed = 0
    try:
        for row in rows:
            specs = plan(row)
            if not specs:
                counts["no_usable_table"] += 1
                continue
            owner = ap.class_at(row.get("address")) or row["class"]
            struct, why = ap.resolve_class(owner)
            if struct is None:
                counts[why] += 1
                problems.append((owner, why))
                continue
            if not opts.write:
                counts["would_apply"] += 1
                tables += len(specs)
                slots += sum(len(s["slots"]) for s in specs)
                continue
            want = size_for(sizes, owner)
            try:
                for spec in specs:
                    table = ap.build_table(spec)
                    status = ap.place_vptr(struct, spec, table, opts, want)
                    counts[status] += 1
                    if status == "placed":
                        tables += 1
                        slots += len(spec["slots"])
                    else:
                        problems.append((row["class"], status))
            except Exception as e:
                counts["error"] += 1
                problems.append((row["class"],
                                 "%s: %s" % (type(e).__name__, e)))
            processed += 1
            if opts.checkpoint_every and \
                    processed % opts.checkpoint_every == 0:
                program.endTransaction(tx, True)
                tx = program.startTransaction("fc2re: apply vtables")
    finally:
        if tx is not None:
            program.endTransaction(tx, True)

    return counts, tables, slots, problems


def write_report(path, counts, tables, slots, problems, opts):
    lines = ["mode                 : %s"
             % ("WRITE" if opts.write else "DRY RUN"), "", "== outcomes =="]
    for k in sorted(counts, key=lambda k: -counts[k]):
        lines.append("  %-30s %d" % (k, counts[k]))
    lines += ["",
              "vtable structs       : %d" % tables,
              "virtual slots named  : %d" % slots]
    if problems:
        lines += ["", "== first 25 problems =="]
        for cls, why in problems[:25]:
            lines.append("  %-46s %s" % (cls[:46], why[:60]))
    if not opts.write:
        lines += ["", "Nothing was written. Re-run with --write to commit."]
    text = "\n".join(lines)
    if path:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    return text


def main_headless():
    ap = argparse.ArgumentParser(
        description="Apply harvested vtables to the type database.")
    ap.add_argument("vtables", help="vtables.jsonl")
    ap.add_argument("project_location")
    ap.add_argument("project_name")
    ap.add_argument("program")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--force", action="store_true",
                    help="also overwrite a non-vptr field at offset 0")
    ap.add_argument("--sizes", default=None,
                    help="class_sizes.jsonl; grows each class struct to its "
                         "real size so accesses past the vptr do not wrap "
                         "into this[N]")
    ap.add_argument("--checkpoint-every", type=int, default=200)
    ap.add_argument("--report", default="apply_vtables_report.txt")
    opts = ap.parse_args()

    import pyghidra
    pyghidra.start()
    bind_java_types()

    from ghidra.base.project import GhidraProject
    from ghidra.util.task import ConsoleTaskMonitor

    monitor = ConsoleTaskMonitor()
    project = GhidraProject.openProject(opts.project_location,
                                        opts.project_name, True)
    try:
        path = opts.program if opts.program.startswith("/") \
            else "/" + opts.program
        folder, _, pname = path.rpartition("/")
        program = project.openProgram(folder or "/", pname, not opts.write)
        try:
            rows = load_jsonl(opts.vtables)
            print("[*] %d vtables" % len(rows))
            counts, tables, slots, problems = run(program, rows, opts)
            print()
            print(write_report(opts.report, counts, tables, slots,
                               problems, opts))
            if opts.write:
                print("\n[*] saving ...")
                try:
                    project.save(program)
                except Exception:
                    program.getDomainFile().save(monitor)
                print("[+] saved")
        finally:
            project.close(program)
    finally:
        project.close()


if __name__ == "__main__":
    main_headless()
