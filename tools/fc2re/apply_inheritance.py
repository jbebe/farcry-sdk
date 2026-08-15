# Flatten inherited property members into derived class structs.
#
# A class only registers its own fields, so everything it inherits reads as
# field_0xNN. The RTTI graph gives each base's offset within the derived
# object, so a base's recovered members can be replayed at base_offset +
# member_offset.
#
# Members are placed individually rather than by embedding the base struct as
# a composite. Base sizes reconcile upwards -- the largest of several
# allocation sites -- so a base whose size was inflated by a derived
# allocation would, as a composite, swallow the derived class's own members.
# Placing fields one at a time cannot overlap anything already there.
#
# Runs after apply_properties and apply_vtables: it only fills offsets that
# are still unnamed, so their output takes precedence. Re-running
# apply_properties clears its members and this pass must be repeated.
#
# SAFE BY DEFAULT: without --write nothing is written.
#
#   python apply_inheritance.py out C:\projdir fc2 /FarCry2_server
#   # add --write once the dry run looks right
#
# @category FC2RE
# @runtime PyGhidra

import argparse
import json
import os
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from apply_properties import plan_members
from apply_vtables import split_scopes
from dump_properties import jstr

MARKER = "[fc2re:inherited]"
POINTER_SIZE = 4

# Guards a hierarchy that is not the clean tree it should be.
MAX_DEPTH = 24

Undefined1 = None
Undefined2 = None
Undefined4 = None


def bind_java_types():
    global Undefined1, Undefined2, Undefined4
    from ghidra.program.model.data import Undefined1DataType as _U1
    from ghidra.program.model.data import Undefined2DataType as _U2
    from ghidra.program.model.data import Undefined4DataType as _U4
    Undefined1 = _U1
    Undefined2 = _U2
    Undefined4 = _U4


def load_jsonl(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


# ---------------------------------------------------------------------------
# pure logic -- kept Ghidra-free so it can be unit tested without a JVM
# ---------------------------------------------------------------------------
def name_of(row):
    return row.get("ghidra_class") or row.get("class")


def base_edges(typeinfo):
    by_symbol = {t["symbol"]: name_of(t) for t in typeinfo if t.get("symbol")}
    edges = defaultdict(list)
    for t in typeinfo:
        cls = name_of(t)
        if not cls:
            continue
        for b in t.get("bases") or []:
            base = by_symbol.get(b.get("typeinfo"))
            if base and base != cls:
                edges[cls].append((base, b.get("offset") or 0))
    return edges


def own_members(properties, polymorphic):
    """class -> its own placed members, reusing the property planner."""
    by_owner = defaultdict(list)
    for r in properties:
        if r.get("owner"):
            by_owner[r["owner"]].append(r)
    out = {}
    for owner, rows in by_owner.items():
        leaf = split_scopes(owner)[-1]
        members, _ = plan_members(rows, leaf in polymorphic)
        if members:
            out[owner] = members
    return out


def inherited_members(cls, edges, own, cache, depth=0):
    """Every ancestor's members, shifted into this class's coordinates."""
    if depth > MAX_DEPTH:
        return []
    if cls in cache:
        return cache[cls]
    cache[cls] = []

    collected = []
    for base, offset in edges.get(cls, []):
        for m in own.get(base, []):
            collected.append((offset + m["offset"], base, m))
        for at, src, m in inherited_members(base, edges, own, cache,
                                            depth + 1):
            collected.append((offset + at, src, m))

    cache[cls] = collected
    return collected


def plan_inherited(cls, edges, own, cache):
    """Ordered placements, nearest ancestor winning any shared offset."""
    taken = {m["offset"] for m in own.get(cls, [])}
    out = []
    seen = set()
    for at, src, m in sorted(inherited_members(cls, edges, own, cache),
                             key=lambda t: t[0]):
        if at in taken or at in seen:
            continue
        seen.add(at)
        out.append({
            "offset": at,
            "size": m["size"],
            "name": m["name"],
            "from": src,
            "type_name": m.get("type_name"),
        })
    return out


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
        scopes = split_scopes(owner)
        pool = self.by_name.get(scopes[-1]) if scopes else None
        if not pool:
            return None, "no_struct"
        scoped = "/".join(scopes)
        wanted = ("/Demangler/%s" % scoped, "/%s" % scoped)
        preferred = [s for s in pool if jstr(s.getPathName()) in wanted]
        if len(preferred) == 1:
            return preferred[0], None
        if not preferred and len(pool) == 1:
            return pool[0], None
        return None, "ambiguous_%d" % len(preferred or pool)

    def slot_type(self, size):
        return {1: Undefined1.dataType, 2: Undefined2.dataType,
                4: Undefined4.dataType}.get(size, Undefined4.dataType)

    def place(self, struct, members, opts):
        """Fill only offsets nothing has named yet."""
        placed = skipped = 0
        used = set()
        for c in struct.getComponents():
            if c.getFieldName():
                used.add(c.getOffset())

        for m in members:
            end = m["offset"] + m["size"]
            if struct.getLength() < end:
                if not opts.grow:
                    skipped += 1
                    continue
                for _ in range(4):
                    have = struct.getLength()
                    if have >= end:
                        break
                    struct.growStructure(end - have)
                if struct.getLength() < end:
                    skipped += 1
                    continue
            if m["offset"] in used:
                skipped += 1
                continue
            existing = struct.getComponentAt(m["offset"])
            if existing is not None and existing.getFieldName():
                skipped += 1
                continue
            comment = "%s from %s" % (MARKER, m["from"])
            if m["type_name"]:
                comment += "; %s" % m["type_name"]
            struct.replaceAtOffset(m["offset"], self.slot_type(m["size"]),
                                   m["size"], m["name"], comment)
            used.add(m["offset"])
            placed += 1
        return placed, skipped


def run(program, data, opts):
    typeinfo, properties, vtables = data
    polymorphic = {split_scopes(name_of(v))[-1] for v in vtables
                   if name_of(v)}
    edges = base_edges(typeinfo)
    own = own_members(properties, polymorphic)
    print("[*] %d classes with own members, %d inheritance edges"
          % (len(own), sum(len(v) for v in edges.values())))

    cache = {}
    targets = []
    for cls in sorted(edges):
        members = plan_inherited(cls, edges, own, cache)
        if members:
            targets.append((cls, members))
    print("[*] %d classes inherit at least one recovered member"
          % len(targets))

    ap = Applier(program)
    counts = defaultdict(int)
    problems = []
    placed = skipped = 0

    tx = program.startTransaction("fc2re: flatten inherited members") \
        if opts.write else None
    n = 0
    try:
        for cls, members in targets:
            struct, why = ap.resolve(cls)
            if struct is None:
                counts[why] += 1
                problems.append((cls, why))
                continue
            if not opts.write:
                counts["would_apply"] += 1
                placed += len(members)
                continue
            try:
                got, miss = ap.place(struct, members, opts)
            except Exception as e:
                counts["error"] += 1
                problems.append((cls, "%s: %s" % (type(e).__name__, e)))
                continue
            counts["applied" if got else "nothing_free"] += 1
            placed += got
            skipped += miss
            n += 1
            if opts.checkpoint_every and n % opts.checkpoint_every == 0:
                program.endTransaction(tx, True)
                tx = program.startTransaction(
                    "fc2re: flatten inherited members")
    finally:
        if tx is not None:
            program.endTransaction(tx, True)

    return counts, placed, skipped, problems


def write_report(path, counts, placed, skipped, problems, opts):
    lines = ["mode                 : %s"
             % ("WRITE" if opts.write else "DRY RUN"), "", "== classes =="]
    for k in sorted(counts, key=lambda k: -counts[k]):
        lines.append("  %-28s %d" % (k, counts[k]))
    lines += ["",
              "inherited members placed : %d" % placed,
              "skipped (offset taken or past end): %d" % skipped]
    if problems:
        lines += ["", "== first 20 problems =="]
        for cls, why in problems[:20]:
            lines.append("  %-46s %s" % (cls[:46], why[:50]))
    if not opts.write:
        lines += ["", "Nothing was written. Re-run with --write to commit."]
    text = "\n".join(lines)
    if path:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    return text


def main_headless():
    ap = argparse.ArgumentParser(
        description="Flatten inherited property members into derived classes.")
    ap.add_argument("outdir")
    ap.add_argument("project_location")
    ap.add_argument("project_name")
    ap.add_argument("program")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--grow", action="store_true",
                    help="grow a struct that is too short; off by default so "
                         "a bad size cannot inflate a class")
    ap.add_argument("--checkpoint-every", type=int, default=200)
    ap.add_argument("--report", default=None)
    opts = ap.parse_args()

    import pyghidra
    pyghidra.start()
    bind_java_types()

    from ghidra.base.project import GhidraProject
    from ghidra.util.task import ConsoleTaskMonitor

    monitor = ConsoleTaskMonitor()
    d = opts.outdir
    data = (load_jsonl(os.path.join(d, "typeinfo.jsonl")),
            load_jsonl(os.path.join(d, "register_properties.jsonl")),
            load_jsonl(os.path.join(d, "vtables.jsonl")))
    report = opts.report or os.path.join(d, "apply_inheritance_report.txt")

    project = GhidraProject.openProject(opts.project_location,
                                        opts.project_name, True)
    try:
        path = opts.program if opts.program.startswith("/") \
            else "/" + opts.program
        folder, _, pname = path.rpartition("/")
        program = project.openProgram(folder or "/", pname, not opts.write)
        try:
            counts, placed, skipped, problems = run(program, data, opts)
            print()
            print(write_report(report, counts, placed, skipped, problems,
                               opts))
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
