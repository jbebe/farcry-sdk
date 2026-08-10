"""Stage B3: score candidate pairs with BSim, Ghidra's own function similarity.

BSim signatures are feature vectors derived from the *decompiler's* view of a
function, not from its bytes. That makes them robust to register reallocation,
instruction scheduling and the small codegen differences that separate two
builds of one source tree -- which is exactly the "exact in a broader sense"
tier, implemented by Ghidra rather than by a hand-rolled normaliser here.

Only the pairs proposed by candidates.py are scored. BSim costs a decompilation
per function, so the all-pairs form the shipped LocalBSimQueryScript uses is not
an option at this scale, and a call-graph/address-window candidate is a better
prior than a global scan anyway.

    python ghidra_bsim.py [--config addrlib.toml]

Requires the Ghidra GUI to be closed. Read-only: programs are opened without
write access and never saved.
"""

import argparse
import os
import sys
import time
from collections import defaultdict

import common
from ghidra_extract import jvm_running


def build_vectors(program, rvas, vector_factory, monitor, reporter, label):
    """rva -> LSHVector for the requested functions only."""
    from ghidra.features.bsim.query import GenSignatures
    from java.util import ArrayList

    base = program.getImageBase().getOffset()
    fm = program.getFunctionManager()
    space = program.getAddressFactory().getDefaultAddressSpace()

    wanted = ArrayList()
    for rva in sorted(rvas):
        func = fm.getFunctionAt(space.getAddress(base + rva))
        if func is not None:
            wanted.add(func)
    reporter.line("    %s: decompiling %d function(s)" % (label, wanted.size()))

    gensig = GenSignatures(False)
    gensig.setVectorFactory(vector_factory)
    gensig.openProgram(program, None, None, None, None, None)
    started = time.time()
    gensig.scanFunctions(wanted.iterator(), wanted.size(), monitor)
    reporter.line("    %s: signatures generated in %.0fs" % (label, time.time() - started))

    out = {}
    it = gensig.getDescriptionManager().listAllFunctions()
    while it.hasNext():
        desc = it.next()
        rec = desc.getSignatureRecord()
        if rec is None:
            continue
        out[int(desc.getAddress()) - base] = rec.getLSHVector()
    reporter.line("    %s: vectors kept %d" % (label, len(out)))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    ap.add_argument("--prefix", default="")
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    pfx = args.prefix
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: BSim scoring%s"
                               % (" [%s]" % pfx if pfx else ""))

    cand_path = os.path.join(out, pfx + "candidates.jsonl")
    if not os.path.exists(cand_path):
        raise SystemExit("[!] missing %s - run candidates.py first" % cand_path)
    candidates = list(common.read_jsonl(cand_path))
    ref_wanted = {c["ref_rva"] for c in candidates}
    tgt_wanted = {t for c in candidates for t in c["candidates"]}
    reporter.line("candidate pairs: %d (%d reference, %d target functions)"
                  % (sum(len(c["candidates"]) for c in candidates),
                     len(ref_wanted), len(tgt_wanted)))

    location = common.resolve_project_location(cfg)
    project_name = cfg["project"]["name"]
    lock = os.path.join(location, project_name + ".lock")
    if os.path.exists(lock):
        reporter.line("[!] project lock present: %s" % lock)
        reporter.line("    %s" % ("close the Ghidra GUI" if jvm_running()
                                  else "STALE - delete it and its '~' sibling"))
        return 1

    import pyghidra
    pyghidra.start()

    from ghidra.base.project import GhidraProject
    from ghidra.features.bsim.query import FunctionDatabase
    from ghidra.util.task import ConsoleTaskMonitor
    from generic.lsh.vector import VectorCompare

    monitor = ConsoleTaskMonitor()
    bc = cfg["bsim"]

    vector_factory = FunctionDatabase.generateLSHVectorFactory()
    config = FunctionDatabase.loadConfigurationTemplate(bc["template"])
    vector_factory.set(config.weightfactory, config.idflookup, config.info.settings)
    reporter.line("BSim template: %s" % bc["template"])

    project = GhidraProject.openProject(location, project_name, True)
    try:
        reporter.section("signatures")
        ref_prog = project.openProgram("/", cfg["builds"]["reference"]["program"], True)
        try:
            ref_vecs = build_vectors(ref_prog, ref_wanted, vector_factory,
                                     monitor, reporter, "reference")
        finally:
            project.close(ref_prog)

        tgt_prog = project.openProgram("/", cfg["builds"]["target"]["program"], True)
        try:
            tgt_vecs = build_vectors(tgt_prog, tgt_wanted, vector_factory,
                                     monitor, reporter, "target")
        finally:
            project.close(tgt_prog)
    finally:
        project.close()

    reporter.section("scoring")
    vec_cmp = VectorCompare()
    sim_min = bc["similarity_min"]
    sig_min = bc["significance_min"]
    self_min = bc["self_significance_min"]
    margin = cfg["accept"]["uniqueness_margin"]

    anchors = []
    stats = defaultdict(int)
    for entry in candidates:
        rv = ref_vecs.get(entry["ref_rva"])
        if rv is None:
            stats["no_reference_vector"] += 1
            continue
        if vector_factory.getSelfSignificance(rv) <= self_min:
            stats["reference_too_generic"] += 1
            continue

        scored = []
        for tgt_rva in entry["candidates"]:
            tv = tgt_vecs.get(tgt_rva)
            if tv is None:
                continue
            if vector_factory.getSelfSignificance(tv) <= self_min:
                continue
            sim = float(rv.compare(tv, vec_cmp))
            sig = float(vector_factory.calculateSignificance(vec_cmp))
            scored.append((sim, sig, tgt_rva))

        if not scored:
            stats["no_target_vector"] += 1
            continue
        scored.sort(reverse=True)
        sim, sig, tgt_rva = scored[0]
        if sim < sim_min:
            stats["below_similarity"] += 1
            continue
        if sig < sig_min:
            stats["below_significance"] += 1
            continue
        # A runner-up this close means the evidence does not distinguish them.
        if len(scored) > 1 and (sim - scored[1][0]) < margin:
            stats["ambiguous"] += 1
            continue
        anchors.append({"ref_rva": entry["ref_rva"], "tgt_rva": tgt_rva,
                        "stage": "bsim", "conf": round(min(sim, 0.999), 4),
                        "evidence": "sim=%.4f sig=%.1f" % (sim, sig)})
        stats["accepted"] += 1

    for k in sorted(stats, key=lambda k: -stats[k]):
        reporter.line("    %-24s %d" % (k, stats[k]))

    common.write_jsonl(os.path.join(out, pfx + "bsim.jsonl"), anchors)
    reporter.line("")
    reporter.line("    -> out/%sbsim.jsonl (%d anchors)" % (pfx, len(anchors)))
    reporter.save(os.path.join(out, pfx + "bsim_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
