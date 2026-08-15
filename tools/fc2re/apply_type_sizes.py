# Give unsized placeholder types a real size (PyGhidra).
#
# 5,331 functions have a parameter Ghidra cannot assign storage to, because
# its type is a 1-byte placeholder. An incomplete prototype is discarded
# wholesale, so the decompiler falls back to its own guesses even where the
# rest of the signature was correct.
#
# Two sources, both evidence-led:
#
#   value types   the distance from a property member to the next bounds
#                 sizeof(member type) from above, so the minimum gap seen is
#                 the tightest upper bound. A size is accepted only when the
#                 minimum and the mode agree, which rejects a template whose
#                 instantiations differ in size -- ndVec_tpl<float,2> and
#                 <float,3> share a stripped name but not a size.
#
#   enum typedefs the demangler emits an unknown enum as a typedef to
#                 `undefined`. Sizing those at 4 assumes GCC's x86 default of
#                 int-sized enums. This is the one assumption here, and it is
#                 recorded in each type's description.
#
# Sizes are asserted with undefined<N>, not a guessed semantic type: the claim
# is how much room the value occupies, nothing more.
#
# SAFE BY DEFAULT: without --write nothing is written.
#
#   python apply_type_sizes.py out C:\projdir fc2 /FarCry2_server
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

from apply_properties import member_type_from_handler
from dump_properties import jstr

MARKER = "[fc2re:sized]"

# GCC on x86 gives an enum the size of an int.
ENUM_SIZE = 4

# Fewer samples than this is not evidence.
MIN_SAMPLES = 5

# min == mode is not enough on its own: CryMap's mode appears in 3 of 15
# samples, which says the type has no single size worth asserting.
MIN_AGREEMENT = 0.6

# The codebase names its enums with an E prefix -- EStimType, EMoveLayer,
# EEntityUpdateFlags. Every unsized typedef is NOT an enum: the same set holds
# std::_Deque_iterator, __gnu_cxx::__normal_iterator and ndRectT, which are
# nearer 16 bytes than 4. Sizing one of those wrongly shifts every parameter
# after it, so only the convention is trusted.
RE_ENUM_NAME = re.compile(r"^E[A-Z][A-Za-z0-9_]*$")

# A value type larger than this is not being passed around by value.
MAX_VALUE_SIZE = 256

Undefined = None
TypedefDataType = None
DataTypeConflictHandler = None


def bind_java_types():
    global Undefined, TypedefDataType, DataTypeConflictHandler
    from ghidra.program.model.data import Undefined as _U
    from ghidra.program.model.data import TypedefDataType as _TDT
    from ghidra.program.model.data import DataTypeConflictHandler as _DTCH
    Undefined = _U
    TypedefDataType = _TDT
    DataTypeConflictHandler = _DTCH


def load_jsonl(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


# ---------------------------------------------------------------------------
# pure logic -- kept Ghidra-free so it can be unit tested without a JVM
# ---------------------------------------------------------------------------
def gap_histogram(properties):
    """member type -> Counter of distances to the next member."""
    by_owner = defaultdict(list)
    for r in properties:
        if r.get("owner") and r.get("offset") is not None and r.get("name"):
            by_owner[r["owner"]].append(r)

    gaps = defaultdict(Counter)
    for rows in by_owner.values():
        rows = sorted(rows, key=lambda r: r["offset"])
        for i, r in enumerate(rows):
            name, declared = member_type_from_handler(r.get("handler_symbol"))
            if not name or declared:
                continue
            nxt = next((x["offset"] for x in rows[i + 1:]
                        if x["offset"] > r["offset"]), None)
            if nxt is not None:
                gaps[name][nxt - r["offset"]] += 1
    return gaps


def looks_like_enum(path):
    """The codebase's own naming convention, applied to the leaf name."""
    return bool(RE_ENUM_NAME.match(path.rsplit("/", 1)[-1]))


def value_sizes(properties, min_samples=MIN_SAMPLES,
                min_agreement=MIN_AGREEMENT):
    """Sizes only where the evidence is unambiguous.

    A gap is an upper bound on the member's size, so the smallest one seen is
    the tightest bound. The mode must agree with it, which drops a stripped
    name covering several differently-sized instantiations, and the mode must
    also dominate -- otherwise the type has no single size worth asserting.
    """
    out = {}
    for name, counts in gap_histogram(properties).items():
        total = sum(counts.values())
        if total < min_samples:
            continue
        smallest = min(counts)
        mode, hits = counts.most_common(1)[0]
        if smallest != mode or not 0 < smallest <= MAX_VALUE_SIZE:
            continue
        agreement = hits / float(total)
        if agreement < min_agreement:
            continue
        out[name] = {"size": smallest, "samples": total,
                     "agreement": round(agreement, 3)}
    return out


# ---------------------------------------------------------------------------
# Ghidra-side work
# ---------------------------------------------------------------------------
class Sizer(object):
    def __init__(self, program):
        self.program = program
        self.dtm = program.getDataTypeManager()
        self.fm = program.getFunctionManager()

    def unsized_parameter_types(self):
        """Types that leave a parameter without storage, and how often."""
        hits = Counter()
        holder = {}
        it = self.fm.getFunctions(True)
        while it.hasNext():
            f = it.next()
            for p in f.getParameters():
                stor = p.getVariableStorage()
                if stor is not None and not stor.isUnassignedStorage():
                    continue
                dt = p.getDataType()
                key = jstr(dt.getPathName())
                hits[key] += 1
                holder.setdefault(key, dt)
        return hits, holder

    @staticmethod
    def resolves_to_undefined(dt):
        """A typedef chain ending in Ghidra's `undefined`."""
        seen = 0
        cur = dt
        while cur is not None and seen < 8:
            if isinstance(cur, TypedefDataType) or hasattr(cur, "getBaseDataType"):
                try:
                    nxt = cur.getBaseDataType()
                except Exception:
                    return False
                if nxt is cur:
                    return False
                if Undefined.isUndefined(nxt):
                    return True
                cur = nxt
                seen += 1
                continue
            return False
        return False

    def plan(self, hits, holder, evidence):
        """(datatype, new size, why) for each type worth sizing, plus misses.

        Anything without gap evidence and without the enum naming convention
        is left alone. Guessing 4 bytes for a std::_Deque_iterator or an
        ndRectT would misplace every parameter that follows it.
        """
        out, unsized = [], []
        for path, count in hits.most_common():
            dt = holder[path]
            length = dt.getLength()
            if length and length > 1:
                continue                      # sized already; another fault
            leaf = path.rsplit("/", 1)[-1]
            ev = evidence.get(leaf)
            if ev:
                out.append((dt, ev["size"], "member gap x%d" % ev["samples"],
                            count))
            elif looks_like_enum(path) and self.resolves_to_undefined(dt):
                out.append((dt, ENUM_SIZE, "enum by name", count))
            else:
                unsized.append((path, count))
        return out, unsized

    def apply(self, dt, size, why):
        """Assert a size without asserting a meaning."""
        filler = Undefined.getUndefinedDataType(size)
        if filler is None:
            return "no_filler_for_%d" % size
        # A TypedefDataType refuses setDescription in this Ghidra version, so
        # provenance lives in the report rather than on the type.
        name = jstr(dt.getName())
        replacement = TypedefDataType(dt.getCategoryPath(), name, filler,
                                      self.dtm)
        self.dtm.replaceDataType(dt, replacement, False)
        return "sized"


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
def run(program, properties, opts):
    evidence = value_sizes(properties)
    print("[*] %d value types sized by member-gap evidence" % len(evidence))
    for name in sorted(evidence, key=lambda n: -evidence[n]["samples"])[:10]:
        e = evidence[name]
        print("      %-26s %3d bytes  (n=%d, agreement %.2f)"
              % (name[:26], e["size"], e["samples"], e["agreement"]))

    sz = Sizer(program)
    hits, holder = sz.unsized_parameter_types()
    print("\n[*] %d distinct types leave a parameter without storage"
          % len(hits))

    todo, unsized = sz.plan(hits, holder, evidence)
    reach = sum(c for _, _, _, c in todo)
    left = sum(c for _, c in unsized)
    print("[*] %d of them can be sized, covering %d parameter uses"
          % (len(todo), reach))
    print("[*] %d left alone for lack of evidence, covering %d uses"
          % (len(unsized), left))
    for path, count in unsized[:8]:
        print("      %-52s %d uses" % (path[:52], count))

    counts = Counter()
    if not opts.write:
        for _, _, why, _ in todo:
            counts["would_size:" + why.split(" x")[0]] += 1
        return counts, todo

    tx = program.startTransaction("fc2re: size placeholder types")
    try:
        for i, (dt, size, why, _) in enumerate(todo, 1):
            try:
                counts[sz.apply(dt, size, why)] += 1
            except Exception as e:
                counts["error"] += 1
                print("    %s: %s: %s" % (jstr(dt.getName()),
                                          type(e).__name__, e))
            if opts.checkpoint_every and i % opts.checkpoint_every == 0:
                program.endTransaction(tx, True)
                tx = program.startTransaction("fc2re: size placeholder types")
    finally:
        program.endTransaction(tx, True)
    return counts, todo


def write_report(path, counts, todo, opts):
    lines = ["mode                 : %s"
             % ("WRITE" if opts.write else "DRY RUN"), "", "== outcomes =="]
    for k in sorted(counts, key=lambda k: -counts[k]):
        lines.append("  %-30s %d" % (k, counts[k]))
    lines += ["", "== largest reach =="]
    for dt, size, why, count in todo[:25]:
        lines.append("  %-46s %3d bytes  %-16s %d uses"
                     % (jstr(dt.getPathName())[:46], size, why, count))
    if not opts.write:
        lines += ["", "Nothing was written. Re-run with --write to commit."]
    text = "\n".join(lines)
    if path:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    return text


def main_headless():
    ap = argparse.ArgumentParser(
        description="Size placeholder types so parameter storage resolves.")
    ap.add_argument("outdir")
    ap.add_argument("project_location")
    ap.add_argument("project_name")
    ap.add_argument("program")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--checkpoint-every", type=int, default=200)
    ap.add_argument("--report", default=None)
    opts = ap.parse_args()

    import pyghidra
    pyghidra.start()
    bind_java_types()

    from ghidra.base.project import GhidraProject
    from ghidra.util.task import ConsoleTaskMonitor

    monitor = ConsoleTaskMonitor()
    properties = load_jsonl(os.path.join(opts.outdir,
                                         "register_properties.jsonl"))
    report = opts.report or os.path.join(opts.outdir,
                                         "apply_type_sizes_report.txt")

    project = GhidraProject.openProject(opts.project_location,
                                        opts.project_name, True)
    try:
        path = opts.program if opts.program.startswith("/") \
            else "/" + opts.program
        folder, _, pname = path.rpartition("/")
        program = project.openProgram(folder or "/", pname, not opts.write)
        try:
            counts, todo = run(program, properties, opts)
            print()
            print(write_report(report, counts, todo, opts))
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
