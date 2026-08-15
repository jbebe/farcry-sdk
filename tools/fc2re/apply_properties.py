# Apply recovered Nomad property layouts to the Ghidra type database.
#
# Consumes register_properties.jsonl from dump_properties.py and fills the
# empty /Demangler/<Class> placeholder structs with named members at their
# recovered offsets.
#
# What this does NOT claim: the true size of a class. Only the members the
# Nomad system serializes are known, so the struct is grown to the last known
# member and left undefined everywhere else. Registered offsets are not
# adjacent -- unregistered members sit between them -- so a member's size is
# never inferred from the gap to the next one, only capped by it.
#
# SAFE BY DEFAULT: without --write this reports what it would do, opens no
# transaction and saves nothing. Review the report, then add --write.
#
#   A) Ghidra Script Manager, with FarCry2_server open read-write
#   B) headless:
#        python apply_properties.py out\register_properties.jsonl ^
#            C:\path\to\projdir fc2 /FarCry2_server --report apply_report.txt
#        # add --write once the dry run looks right
#
# @category FC2RE
# @runtime PyGhidra

import argparse
import json
import os
import re
import sys
from collections import defaultdict

MARKER = "[fc2re:properties]"

# Sizes Ghidra will place for a named-but-untyped slot.
SLOT_SIZES = (4, 2, 1)

# Descriptors that back no storage of their own. They report offset 0 -- every
# CGroupMember and CConditionalGroupMember does, and 158 of 161
# CVirtualMembers -- so placing them would put a bogus field at the start of
# the class.
NO_STORAGE_KINDS = frozenset((
    "CSerializationEvent", "CGroupMember", "CConditionalGroupMember",
    "CVirtualMember", "CVirtualMemberRef", "CVirtualMemberIntrinsicGetCopy",
    "CVirtualMemberIntrinsicGetRef", "CContainedMember", "CEnumMember",
))

# An array element: several share one base offset and are told apart by the
# index in their flags slot. The element stride is not recorded anywhere, so
# only index 0 is placed and its siblings are named in the comment.
ARRAY_KIND = "COffsetMember"

Undefined1 = None
Undefined2 = None
Undefined4 = None
CategoryPath = None


def bind_java_types():
    global Undefined1, Undefined2, Undefined4, CategoryPath
    from ghidra.program.model.data import Undefined1DataType as _U1
    from ghidra.program.model.data import Undefined2DataType as _U2
    from ghidra.program.model.data import Undefined4DataType as _U4
    from ghidra.program.model.data import CategoryPath as _CP
    Undefined1 = _U1
    Undefined2 = _U2
    Undefined4 = _U4
    CategoryPath = _CP


def jstr(v):
    if v is None:
        return None
    try:
        return str(v)
    except Exception:
        return None


# ---------------------------------------------------------------------------
# pure logic -- kept Ghidra-free so it can be unit tested without a JVM
# ---------------------------------------------------------------------------
RE_ZTV_BODY = re.compile(r"^_ZTV(?P<len>\d+)(?P<rest>.+)$")

# Itanium builtin type codes, sized for the 32-bit target.
BUILTIN_SIZES = {
    "b": ("bool", 1), "c": ("char", 1), "a": ("signed char", 1),
    "h": ("unsigned char", 1), "s": ("short", 2), "t": ("unsigned short", 2),
    "i": ("int", 4), "j": ("unsigned int", 4), "l": ("long", 4),
    "m": ("unsigned long", 4), "f": ("float", 4), "d": ("double", 8),
    "x": ("long long", 8), "y": ("unsigned long long", 8),
}


def read_mangled_name(text, pos):
    """Read one length-prefixed Itanium name. Returns (name, next_pos)."""
    m = re.match(r"(\d+)", text[pos:])
    if not m:
        return None, pos
    n = int(m.group(1))
    start = pos + len(m.group(1))
    if start + n > len(text):
        return None, pos
    return text[start:start + n], start + n


def member_type_from_handler(symbol):
    """_ZTV14CGenericMemberI4Barkb18Generic... -> ('bool', 1).

    The handler's second template argument is the member type: the first is
    the owning class. Length prefixes make the walk exact.
    """
    m = RE_ZTV_BODY.match(symbol or "")
    if not m:
        return None, None
    kind_len = int(m.group("len"))
    rest = m.group("rest")
    if len(rest) < kind_len or not rest[kind_len:].startswith("I"):
        return None, None
    pos = kind_len + 1
    _owner, pos = read_mangled_name(rest, pos)
    if _owner is None:
        return None, None
    if pos < len(rest) and rest[pos] in BUILTIN_SIZES:
        return BUILTIN_SIZES[rest[pos]]
    name, _ = read_mangled_name(rest, pos)
    return (name, None) if name else (None, None)


def slot_size(declared, gap):
    """Largest placeable slot that fits both the known type and the gap."""
    limit = gap if gap is not None else 4
    if declared:
        limit = min(limit, declared)
    for size in SLOT_SIZES:
        if size <= limit:
            return size
    return 1


def plan_members(rows, polymorphic=False):
    """Rows for one class -> ordered member placements, plus skips.

    In a polymorphic class offset 0 is the vptr, so a descriptor claiming it
    is a grouping or accessor record rather than storage -- true of every
    CGroupMember, CVirtualMember, CConditionalGroupMember and CEnumMember,
    and of some whose handler never resolved to a kind.
    """
    usable, skipped = [], []
    for r in rows:
        if not r.get("name"):
            continue
        if r.get("kind") in NO_STORAGE_KINDS:
            skipped.append((r, "no storage: %s" % r.get("kind")))
        elif r.get("offset") is None:
            skipped.append((r, "no offset"))
        elif polymorphic and r["offset"] == 0:
            skipped.append((r, "offset 0 is the vptr"))
        else:
            usable.append(r)

    at_offset = defaultdict(list)
    for r in usable:
        at_offset[r["offset"]].append(r)

    offsets = sorted(at_offset)
    next_of = {o: offsets[i + 1] if i + 1 < len(offsets) else None
               for i, o in enumerate(offsets)}

    members = []
    used_names = set()
    for off in offsets:
        group = sorted(at_offset[off],
                       key=lambda r: (r.get("flags") if r.get("flags")
                                      is not None else 0, r.get("index", 0)))
        head = group[0]
        siblings = group[1:]
        if siblings:
            reason = ("array element 0 of %d" % len(group)
                      if all(r.get("kind") == ARRAY_KIND for r in group)
                      else "offset already taken")
            for r in siblings:
                skipped.append((r, reason))

        type_name, declared = member_type_from_handler(
            head.get("handler_symbol"))
        nxt = next_of[off]
        size = slot_size(declared, None if nxt is None else nxt - off)

        name = head["name"]
        suffix = 2
        while name in used_names:
            name = "%s_%d" % (head["name"], suffix)
            suffix += 1
        used_names.add(name)

        members.append({
            "offset": off,
            "size": size,
            "name": name,
            "type_name": type_name,
            "kind": head.get("kind"),
            "siblings": [r["name"] for r in siblings],
        })
    return members, skipped


# Past this a recorded size is a buffer that borrowed a class name from a
# cast, not an object.
MAX_TRUSTED_SIZE = 0x20000


def load_sizes(path):
    """class -> (size, exact).

    Sizes are already reconciled upwards, so a class whose sites disagree is
    still usable: the largest is the safe choice. No agreement threshold is
    applied, because low-agreement classes are precisely the ones a base-class
    cast would otherwise leave undersized. Entries carrying exact=false are
    bounds derived from the inheritance graph rather than allocation sites.
    """
    if not path:
        return {}
    return {row["class"]: (row["size"], bool(row.get("exact", True)))
            for row in load_jsonl(path)
            if row.get("size") and row["size"] <= MAX_TRUSTED_SIZE}


def struct_size(owner, members, sizes):
    """(size to grow to, whether it is the real sizeof).

    An undersized struct is the dangerous case: Ghidra renders accesses past
    the end as `this[1].Field`, reusing a member name for an offset it does
    not describe. Oversizing merely leaves undefined bytes.
    """
    last = members[-1] if members else None
    end = (last["offset"] + last["size"]) if last else 0
    entry = sizes.get(owner) or sizes.get(owner.rsplit("::", 1)[-1])
    if entry and entry[0] >= end:
        return entry[0], entry[1]
    return end, False


def describe(owner, members, skipped, size, exact):
    return ("%s %d serialized members, size 0x%x (%s). "
            "Recovered from %s::RegisterProperties; unregistered members are "
            "not represented."
            % (MARKER, len(members), size,
               "exact, from allocation sites" if exact
               else "lower bound, true size unknown", owner)
            + (" %d descriptors shared an offset with a group wrapper."
               % len(skipped) if skipped else ""))


def load_jsonl(path):
    with open(path, "r", encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


# ---------------------------------------------------------------------------
# Ghidra-side work
# ---------------------------------------------------------------------------
class Applier(object):
    def __init__(self, program):
        self.dtm = program.getDataTypeManager()
        self.by_name = defaultdict(list)
        it = self.dtm.getAllStructures()
        while it.hasNext():
            s = it.next()
            self.by_name[jstr(s.getName())].append(s)

    def resolve(self, owner):
        """The placeholder struct for a class, or a reason it is unusable.

        A nested class is stored under its enclosing scope as a category, so
        CAIWorld::SWagerData is /Demangler/CAIWorld/SWagerData named
        SWagerData -- looking up the qualified name alone finds nothing.
        """
        leaf = owner.rsplit("::", 1)[-1]
        candidates = self.by_name.get(leaf) or []
        if not candidates:
            return None, "no_struct"
        wanted = "/Demangler/%s" % owner.replace("::", "/")
        preferred = [s for s in candidates
                     if jstr(s.getPathName()) == wanted]
        pool = preferred or candidates
        if len(pool) > 1:
            return None, "ambiguous_%d" % len(pool)
        return pool[0], None

    def slot_type(self, size):
        return {1: Undefined1.dataType,
                2: Undefined2.dataType,
                4: Undefined4.dataType}[size]

    def apply(self, struct, owner, members, skipped, sizes, opts):
        existing = struct.getNumComponents()
        described = jstr(struct.getDescription()) or ""
        ours = MARKER in described
        if existing and not ours and not opts.force:
            return "skipped_already_defined", existing
        if not members:
            return "skipped_no_members", 0

        end, exact = struct_size(owner, members, sizes)
        if not opts.write:
            return ("would_apply_exact" if exact else "would_apply"), \
                len(members)

        # A previous run's members are replaced wholesale rather than merged,
        # so reruns converge instead of accumulating.
        if ours and existing:
            struct.deleteAll()
        # An undefined structure reports length 1 while actually being 0, so a
        # single growStructure(end - getLength()) lands a byte short. Grow
        # until it really fits rather than trusting the arithmetic.
        for _ in range(4):
            have = struct.getLength()
            if have >= end:
                break
            struct.growStructure(end - have)
        if struct.getLength() < end:
            return "error_grow_failed", 0
        for m in members:
            bits = [b for b in (m["kind"], m["type_name"]) if b]
            if m["siblings"]:
                bits.append("array with: %s" % ", ".join(m["siblings"]))
            comment = "; ".join(bits) or None
            struct.replaceAtOffset(m["offset"], self.slot_type(m["size"]),
                                   m["size"], m["name"], comment)
        struct.setDescription(describe(owner, members, skipped, end, exact))
        return ("applied_exact" if exact else "applied"), len(members)


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
def load_polymorphic(path):
    """Leaf names of classes that own a vtable."""
    if not path:
        return set()
    out = set()
    for row in load_jsonl(path):
        cls = row.get("class")
        if cls:
            out.add(cls.rsplit("::", 1)[-1])
    return out


def run(program, rows, opts):
    ap = Applier(program)
    sizes = load_sizes(getattr(opts, "sizes", None))
    poly = load_polymorphic(getattr(opts, "vtables", None))
    if sizes:
        exact = sum(1 for _, is_exact in sizes.values() if is_exact)
        print("[*] %d classes sized: %d exact from allocation sites, "
              "%d derived bounds" % (len(sizes), exact, len(sizes) - exact))
    if poly:
        print("[*] %d classes own a vtable; offset 0 reserved for the vptr"
              % len(poly))
    by_owner = defaultdict(list)
    for r in rows:
        if r.get("owner"):
            by_owner[r["owner"]].append(r)

    counts = defaultdict(int)
    members_total = 0
    skipped_total = 0
    problems = []

    tx = program.startTransaction("fc2re: apply property layouts") \
        if opts.write else None
    processed = 0
    try:
        for owner in sorted(by_owner):
            struct, why = ap.resolve(owner)
            if struct is None:
                counts[why] += 1
                problems.append((owner, why))
                continue
            members, skipped = plan_members(
                by_owner[owner], owner.rsplit("::", 1)[-1] in poly)
            try:
                status, n = ap.apply(struct, owner, members, skipped,
                                     sizes, opts)
            except Exception as e:
                # One rejected placement must not abandon the other 663.
                status, n = "error", 0
                problems.append((owner, "%s: %s" % (type(e).__name__, e)))
            counts[status] += 1
            if status.startswith(("applied", "would_apply")):
                members_total += n
                skipped_total += len(skipped)
            elif status.startswith("skipped"):
                problems.append((owner, status))
            processed += 1
            if opts.write and opts.checkpoint_every and \
                    processed % opts.checkpoint_every == 0:
                program.endTransaction(tx, True)
                tx = program.startTransaction("fc2re: apply property layouts")
    finally:
        if tx is not None:
            program.endTransaction(tx, True)

    return counts, members_total, skipped_total, problems


def write_report(path, counts, members, shared, problems, opts):
    lines = [
        "mode                 : %s" % ("WRITE" if opts.write else "DRY RUN"),
        "",
        "== classes ==",
    ]
    for k in sorted(counts, key=lambda k: -counts[k]):
        lines.append("  %-28s %d" % (k, counts[k]))
    lines += [
        "",
        "members placed       : %d" % members,
        "offsets shared with a group wrapper: %d" % shared,
    ]
    if problems:
        lines += ["", "== first 25 problems =="]
        for owner, why in problems[:25]:
            lines.append("  %-44s %s" % (owner, why))
    if not opts.write:
        lines += ["", "Nothing was written. Re-run with --write to commit."]
    text = "\n".join(lines)
    if path:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    return text


def add_common_args(ap):
    ap.add_argument("properties", help="register_properties.jsonl")
    ap.add_argument("--write", action="store_true",
                    help="actually mutate the program; default is dry-run")
    ap.add_argument("--force", action="store_true",
                    help="also overwrite structs defined by someone else")
    ap.add_argument("--sizes", default=None,
                    help="class_sizes.jsonl; without it every struct is "
                         "grown only to its last known member, which "
                         "under-sizes any class with unregistered tail "
                         "members")
    ap.add_argument("--vtables", default=None,
                    help="vtables.jsonl; classes owning a vtable keep offset "
                         "0 free for the vptr")
    ap.add_argument("--checkpoint-every", type=int, default=200)
    ap.add_argument("--report", default="apply_properties_report.txt")


def main_script():
    bind_java_types()
    g = globals()
    ap = argparse.ArgumentParser()
    add_common_args(ap)
    argv = [jstr(a) for a in (g.get("getScriptArgs", lambda: [])() or [])]
    opts = ap.parse_args(argv)
    rows = load_jsonl(opts.properties)
    counts, members, shared, problems = run(g["currentProgram"], rows, opts)
    print(write_report(opts.report, counts, members, shared, problems, opts))
    if opts.write:
        print("[*] transaction committed; save the program to persist.")


def main_headless():
    ap = argparse.ArgumentParser(
        description="Apply recovered property layouts to placeholder structs.")
    add_common_args(ap)
    ap.add_argument("project_location")
    ap.add_argument("project_name")
    ap.add_argument("program")
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
            rows = load_jsonl(opts.properties)
            counts, members, shared, problems = run(program, rows, opts)
            print()
            print(write_report(opts.report, counts, members, shared,
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


if "currentProgram" in globals():
    main_script()
elif __name__ == "__main__":
    main_headless()
