"""Resolve one stubborn address by hand, with evidence from several channels.

The bulk pipeline deliberately refuses to guess, so a handful of addresses come
out unmapped. This tool works those one at a time and shows its reasoning, so
what lands in overrides.csv is a derivation you can check rather than a number
you have to trust.

Channels, strongest first:

  vtable     If the address is stored in a vtable, and any function in that same
             vtable is already mapped, the whole table can be located in both
             builds and read slot for slot. Self-checking: every other slot's
             pointers must agree with the existing mapping, so a wrong table
             announces itself instead of producing a plausible answer.
  callee     A function's sequence of calls, mapped through the existing
             mapping, is a fingerprint. The counterpart must call the same
             functions in the same order.
  window     The nearest mapped neighbours bracket where the counterpart must
             lie, which bounds the search even when nothing else survives.

    python resolve_address.py 0x005E8CE0 0x0061D3F0
    python resolve_address.py 0x105E8CE0          # VAs accepted too
"""

import argparse
import bisect
import os
import sys
from collections import defaultdict

import pefile

import common


def load_pe(cfg, key):
    path = cfg["builds"][key]["dll"]
    if not os.path.isabs(path):
        path = os.path.join(common.repo_root(), path)
    pe = pefile.PE(path, fast_load=True)
    return pe, path


def section_of(pe, rva):
    for s in pe.sections:
        size = max(s.Misc_VirtualSize, s.SizeOfRawData)
        if s.VirtualAddress <= rva < s.VirtualAddress + size:
            return s.Name.rstrip(b"\x00").decode("ascii", "replace")
    return "?"


def dword(pe, rva):
    try:
        return pe.get_dword_at_rva(rva)
    except Exception:
        return None


def is_code(pe, rva, text_lo, text_hi):
    return text_lo <= rva < text_hi


def find_pointer_slots(pe, data, target_va, base):
    """Every 4-byte aligned location holding target_va, as RVAs.

    `data` is the memory-mapped image, so an index into it *is* an RVA. Do not
    route it through get_rva_from_offset(), which converts *file* offsets and
    silently returns a plausible-but-wrong address for every hit.
    """
    needle = target_va.to_bytes(4, "little")
    hits, start = [], 0
    while True:
        i = data.find(needle, start)
        if i < 0:
            break
        start = i + 1
        if i % 4 == 0:
            hits.append(i)
    return hits


def vtable_bounds(pe, slot_rva, text_lo, text_hi, max_slots=512):
    """Walk out from a slot while neighbours still look like code pointers."""
    lo = slot_rva
    for _ in range(max_slots):
        prev = lo - 4
        v = dword(pe, prev)
        if v is None or not is_code(pe, v - pe.OPTIONAL_HEADER.ImageBase, text_lo, text_hi):
            break
        lo = prev
    hi = slot_rva + 4
    for _ in range(max_slots):
        v = dword(pe, hi)
        if v is None or not is_code(pe, v - pe.OPTIONAL_HEADER.ImageBase, text_lo, text_hi):
            break
        hi += 4
    return lo, hi


def channel_vtable(ctx, rva, reporter):
    ref_pe, tgt_pe = ctx["ref_pe"], ctx["tgt_pe"]
    base = ref_pe.OPTIONAL_HEADER.ImageBase
    mapping, data_map = ctx["mapping"], ctx["data_map"]
    text_lo, text_hi = ctx["ref_text"]
    ttext_lo, ttext_hi = ctx["tgt_text"]

    slots = find_pointer_slots(ref_pe, ctx["ref_data"], base + rva, base)
    if not slots:
        reporter.line("    no vtable slot holds this address")
        return []

    results = []
    for slot in slots[:8]:
        lo, hi = vtable_bounds(ref_pe, slot, text_lo, text_hi)
        nslots = (hi - lo) // 4
        index = (slot - lo) // 4
        reporter.line("    slot at %s -> table %s..%s (%d slots), this is +0x%X"
                      % (common.fmt_rva(slot), common.fmt_rva(lo),
                         common.fmt_rva(hi), nslots, (slot - lo)))

        tgt_lo = data_map.get(lo)
        how = "data map"
        if tgt_lo is None:
            # The table's own address may not be referenced anywhere the data
            # mapper could see. Fall back to locating it by a sibling slot whose
            # function *is* mapped: that pins the table in the other build just
            # as well, and the agreement check below still has to pass.
            for k in range(nslots):
                fn = dword(ref_pe, lo + 4 * k)
                if fn is None:
                    continue
                mapped = mapping.get(fn - base)
                if mapped is None:
                    continue
                for cand in find_pointer_slots(tgt_pe, ctx["tgt_data"],
                                               tgt_pe.OPTIONAL_HEADER.ImageBase + mapped,
                                               tgt_pe.OPTIONAL_HEADER.ImageBase):
                    tgt_lo = cand - 4 * k
                    how = "sibling slot +0x%X (%s)" % (4 * k, common.fmt_rva(fn - base))
                    break
                if tgt_lo is not None:
                    break
        if tgt_lo is None:
            reporter.line("      could not locate the counterpart table")
            continue
        reporter.line("      counterpart table at %s  (via %s)"
                      % (common.fmt_rva(tgt_lo), how))

        # Agreement check across the whole table: every slot whose reference
        # function is already mapped must point at that function's counterpart.
        agree = disagree = unknown = 0
        for k in range(nslots):
            rf = dword(ref_pe, lo + 4 * k)
            tf = dword(tgt_pe, tgt_lo + 4 * k)
            if rf is None or tf is None:
                continue
            want = mapping.get(rf - base)
            if want is None:
                unknown += 1
            elif want == tf - tgt_pe.OPTIONAL_HEADER.ImageBase:
                agree += 1
            else:
                disagree += 1
        reporter.line("      slot agreement: %d agree, %d DISAGREE, %d unmapped"
                      % (agree, disagree, unknown))

        answer = dword(tgt_pe, tgt_lo + (slot - lo))
        if answer is None:
            continue
        answer_rva = answer - tgt_pe.OPTIONAL_HEADER.ImageBase
        ok = disagree == 0 and agree >= 2 and is_code(tgt_pe, answer_rva,
                                                      ttext_lo, ttext_hi)
        reporter.line("      => %s  %s" % (common.fmt_rva(answer_rva),
                                           "CONSISTENT" if ok else "rejected"))
        if ok:
            results.append((answer_rva, "vtable +0x%X, %d/%d slots agree"
                            % (slot - lo, agree, agree + disagree)))
    return results


def channel_callee(ctx, rva, reporter):
    ref_funcs, tgt_funcs = ctx["ref_funcs"], ctx["tgt_funcs"]
    mapping = ctx["mapping"]
    row = ref_funcs.get(rva)
    if row is None or not row["callees"]:
        reporter.line("    no call sites to fingerprint")
        return []

    want = []
    for t in row["callees"]:
        want.append(t if isinstance(t, str) else mapping.get(t))
    known = [w for w in want if w is not None]
    reporter.line("    call sequence: %d sites, %d resolvable"
                  % (len(want), len(known)))
    if len(known) < 2:
        reporter.line("    too few resolvable call sites to be decisive")
        return []

    hits = []
    for trva, trow in tgt_funcs.items():
        if len(trow["callees"]) != len(want):
            continue
        ok = True
        for a, b in zip(want, trow["callees"]):
            if a is None:
                continue
            if a != b:
                ok = False
                break
        if ok:
            hits.append(trva)
    for h in hits[:10]:
        reporter.line("      candidate %s (size %d vs %d)"
                      % (common.fmt_rva(h), tgt_funcs[h]["size"], row["size"]))
    if len(hits) == 1:
        return [(hits[0], "unique call-sequence match over %d resolved sites"
                 % len(known))]
    reporter.line("    %d candidates - not decisive on its own" % len(hits))
    return []


def channel_window(ctx, rva, reporter):
    """Propose candidates from address-order locality. Never decides.

    "The only unclaimed function in the window" feels conclusive and is not: it
    was wrong on the first address this tool was pointed at, offering a 6-byte
    stub as the counterpart of a 48-byte function. Locality is good at saying
    *where to look* and bad at saying *which one*, so everything here is a
    candidate for verify_candidate() to accept or reject.
    """
    mapping = ctx["mapping"]
    anchors = ctx["anchors_sorted"]
    i = bisect.bisect_left(anchors, rva)
    lo = anchors[i - 1] if i > 0 else None
    hi = anchors[i] if i < len(anchors) else None
    if lo is None or hi is None:
        reporter.line("    no bracketing anchors")
        return []
    reporter.line("    bracketed by %s->%s and %s->%s"
                  % (common.fmt_rva(lo), common.fmt_rva(mapping[lo]),
                     common.fmt_rva(hi), common.fmt_rva(mapping[hi])))

    taken = ctx["taken"]
    cands = [t for t in ctx["tgt_sorted"]
             if mapping[lo] < t < mapping[hi] and t not in taken]

    # Ghidra's auto-analysis found ~3k fewer functions in the target build, so
    # the counterpart is often code that was never turned into a function and
    # therefore appears in no list. Predict it directly: immediately after the
    # previous mapped function ends, rounded up to the usual 16-byte alignment.
    prev_row = ctx["tgt_funcs"].get(mapping[lo])
    if prev_row is not None:
        end = mapping[lo] + prev_row["size"]
        for align in (16, 4):
            guess = (end + align - 1) & ~(align - 1)
            if mapping[lo] < guess < mapping[hi] and guess not in cands:
                cands.append(guess)
                reporter.line("      predicted (undiscovered function) %s"
                              % common.fmt_rva(guess))
    for t in sorted(cands)[:12]:
        row = ctx["tgt_funcs"].get(t)
        reporter.line("      candidate %s (%s)"
                      % (common.fmt_rva(t),
                         "size %d" % row["size"] if row else "not a Ghidra function"))
    return [(c, "window candidate") for c in cands]


def decode_calls(img, rva, length):
    """(offset, target_rva) for every E8 rel32 in a byte range."""
    out = []
    for i in range(max(length - 4, 0)):
        if img[rva + i] == 0xE8:
            disp = int.from_bytes(img[rva + i + 1:rva + i + 5], "little", signed=True)
            out.append((i, rva + i + 5 + disp))
    return out


def verify_candidate(ctx, rva, cand, reporter):
    """The decisive test: do the two bodies call the same already-mapped things?

    Byte equality cannot be required -- call displacements and absolute operands
    differ by construction. What cannot happen by coincidence is call site N in
    one build targeting the known counterpart of call site N's target in the
    other. Two such agreements with no disagreement settles it.
    """
    row = ctx["ref_funcs"].get(rva)
    length = row["size"] if row else 64
    ref_img, tgt_img = ctx["ref_data"], ctx["tgt_data"]
    if cand + length > len(tgt_img) or rva + length > len(ref_img):
        return None

    a = ref_img[rva:rva + length]
    b = tgt_img[cand:cand + length]
    ndiff = sum(1 for x, y in zip(a, b) if x != y)

    ca = decode_calls(ref_img, rva, length)
    cb = decode_calls(tgt_img, cand, length)
    if len(ca) != len(cb):
        reporter.line("      %s: %d/%d bytes differ, call-site count %d vs %d -> rejected"
                      % (common.fmt_rva(cand), ndiff, length, len(ca), len(cb)))
        return None

    agree = disagree = unknown = 0
    for (oa, ta), (ob, tb) in zip(ca, cb):
        want = ctx["mapping"].get(ta)
        if oa != ob:
            disagree += 1
        elif want is None:
            unknown += 1
        elif want == tb:
            agree += 1
        else:
            disagree += 1

    verdict = "rejected"
    ok = False
    if disagree == 0 and (agree >= 2 or (agree >= 1 and ndiff * 4 <= length)
                          or (not ca and ndiff * 8 <= length)):
        ok = True
        verdict = "VERIFIED"
    reporter.line("      %s: %d/%d bytes differ, calls %d agree / %d disagree / "
                  "%d unmapped -> %s"
                  % (common.fmt_rva(cand), ndiff, length, agree, disagree,
                     unknown, verdict))
    if not ok:
        return None
    return "%d/%d bytes differ, %d call target(s) agree, none disagree" \
        % (ndiff, length, agree)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("addresses", nargs="+")
    ap.add_argument("--config", default=None)
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: address resolution")

    cache = common.cache_dir(cfg, create=False)
    ref_funcs = {r["rva"]: r for r in common.read_jsonl(
        os.path.join(cache, "%s.functions.jsonl" % cfg["builds"]["reference"]["id"]))}
    tgt_funcs = {r["rva"]: r for r in common.read_jsonl(
        os.path.join(cache, "%s.functions.jsonl" % cfg["builds"]["target"]["id"]))}

    mapping = {}
    for r in common.read_csv_rows(os.path.join(out, "mapping.csv")):
        if r["tier"] in common.SHIPPING_TIERS:
            mapping[common.parse_rva(r["ref_rva"])] = common.parse_rva(r["tgt_rva"])
    data_map = {r["ref_rva"]: r["tgt_rva"]
                for r in common.read_jsonl(os.path.join(out, "data_map.jsonl"))}

    ref_pe, ref_path = load_pe(cfg, "reference")
    tgt_pe, tgt_path = load_pe(cfg, "target")

    def text_range(pe):
        for s in pe.sections:
            if s.Name.rstrip(b"\x00") == b".text":
                return s.VirtualAddress, s.VirtualAddress + max(
                    s.Misc_VirtualSize, s.SizeOfRawData)
        return 0, 0

    ctx = {
        "ref_funcs": ref_funcs, "tgt_funcs": tgt_funcs,
        "mapping": mapping, "data_map": data_map,
        "ref_pe": ref_pe, "tgt_pe": tgt_pe,
        "ref_data": ref_pe.get_memory_mapped_image(),
        "tgt_data": tgt_pe.get_memory_mapped_image(),
        "ref_text": text_range(ref_pe), "tgt_text": text_range(tgt_pe),
        "anchors_sorted": sorted(mapping),
        "tgt_sorted": sorted(tgt_funcs),
        "taken": set(mapping.values()),
    }
    reporter.line("reference %s" % os.path.basename(ref_path))
    reporter.line("target    %s" % os.path.basename(tgt_path))
    reporter.line("mapping   %d function pairs, %d data addresses"
                  % (len(mapping), len(data_map)))

    for spec in args.addresses:
        rva = common.parse_rva(spec)
        reporter.section("%s (%s)" % (common.fmt_rva(rva), section_of(ref_pe, rva)))
        row = ref_funcs.get(rva)
        if row:
            reporter.line("    known to Ghidra as %s, %d bytes, %d calls, %d data refs"
                          % (row["name"], row["size"], len(row["callees"]),
                             len(row["data"])))
        else:
            reporter.line("    NOT a function in the reference program")
        if rva in mapping:
            reporter.line("    already mapped -> %s" % common.fmt_rva(mapping[rva]))
            continue

        # Channels propose; verification decides. No channel is trusted on its
        # own, so a confident-sounding heuristic cannot become an answer.
        proposals = defaultdict(list)
        for name, fn in (("vtable", channel_vtable),
                         ("callee", channel_callee),
                         ("window", channel_window)):
            reporter.line("")
            reporter.line("  [%s]" % name)
            try:
                for answer, why in fn(ctx, rva, reporter):
                    proposals[answer].append("%s: %s" % (name, why))
            except Exception as exc:
                reporter.line("    channel failed: %s" % exc)

        reporter.line("")
        reporter.line("  [verify] %d distinct candidate(s)" % len(proposals))
        verified = {}
        for cand in sorted(proposals):
            evidence = verify_candidate(ctx, rva, cand, reporter)
            if evidence:
                verified[cand] = proposals[cand] + ["verify: " + evidence]

        reporter.line("")
        if not verified:
            reporter.line("  VERDICT: nothing verified. Candidates seen: %s"
                          % (", ".join(common.fmt_rva(c) for c in sorted(proposals))
                             or "none"))
        elif len(verified) == 1:
            answer, why = next(iter(verified.items()))
            reporter.line("  VERDICT: %s" % common.fmt_rva(answer))
            for w in why:
                reporter.line("      %s" % w)
            reporter.line("")
            reporter.line("  overrides.csv line:")
            reporter.line("    %s,%s,\"%s\""
                          % (common.fmt_rva(rva), common.fmt_rva(answer),
                             why[-1].replace('"', "'")))
        else:
            reporter.line("  VERDICT: %d candidates verified - resolve by hand."
                          % len(verified))
            for answer, why in verified.items():
                reporter.line("      %s <- %s" % (common.fmt_rva(answer), "; ".join(why)))

    reporter.save(os.path.join(out, "resolve_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
