"""Stage F: emit the address table as a compact binary resource, plus symbols.h.

The table is embedded in FCSE.exe as an RCDATA resource and expanded into memory
once at startup, rather than compiled in as a constexpr array. Two reasons: a
2.2 MB translation unit of hex literals is the slowest thing in the build for no
benefit, and the encoded form is a fraction of the size.

Encoding is domain-specific rather than a general compressor, and beats one
outright: consecutive entries are near-neighbours in both builds, so storing
per-entry deltas leaves mostly small numbers, and varint makes small numbers
cheap. Measured over this mapping:

    raw u32 pairs                715 KB   (zlib: 331 KB)
    delta + slide-of-slide       217 KB   (zlib:  92 KB)

so the encoding alone beats zlib-on-raw by 1.5x with no dependency, no OS
version floor, and a decoder short enough to audit at a glance. The header
carries a `codec` field, so layering a real compressor on later is a format
extension rather than a redesign.

    python emit.py [--config addrlib.toml]
"""

import argparse
import os
import struct
import sys
import time
from collections import defaultdict

import common

MISSING = 0xFFFFFFFF

MAGIC = b"FADR"
FORMAT_VERSION = 1
CODEC_VARINT_DELTA = 0

# magic, format, codec, entries, payload bytes, missing sentinel,
# mapping version, two build ids
HEADER = struct.Struct("<4sHHIII16s24s24s")


def varint(value, out):
    while True:
        byte = value & 0x7F
        value >>= 7
        if value:
            out.append(byte | 0x80)
        else:
            out.append(byte)
            return


def zigzag(value):
    return (value << 1) ^ (value >> 63) if value < 0 else (value << 1)


def encode(ref_col, tgt_col):
    """Delta the reference column, and delta the per-entry slide of the target.

    Both deltas are zigzagged. The reference column is *almost* ascending but
    not entirely -- IDs appended after the first generation sit at the end of
    the table while their RVAs do not -- and an encoder that assumed ascending
    would silently mis-decode exactly those entries.
    """
    out = bytearray()
    prev_ref = 0
    prev_slide = 0
    for ref, tgt in zip(ref_col, tgt_col):
        varint(zigzag(ref - prev_ref), out)
        prev_ref = ref
        if tgt == MISSING:
            varint(0, out)          # 0 is reserved for "absent"
        else:
            slide = tgt - ref
            varint(zigzag(slide - prev_slide) + 1, out)
            prev_slide = slide
    return bytes(out)


def decode(payload, count):
    """Reference decoder - proves the stream round-trips before it ships."""
    ref_col, tgt_col = [], []
    i = 0
    prev_ref = 0
    prev_slide = 0

    def read():
        nonlocal i
        shift = 0
        value = 0
        while True:
            byte = payload[i]
            i += 1
            value |= (byte & 0x7F) << shift
            if not byte & 0x80:
                return value
            shift += 7

    def unzig(v):
        return (v >> 1) ^ -(v & 1)

    for _ in range(count):
        prev_ref += unzig(read())
        ref_col.append(prev_ref)
        raw = read()
        if raw == 0:
            tgt_col.append(MISSING)
        else:
            prev_slide += unzig(raw - 1)
            tgt_col.append(prev_ref + prev_slide)
    return ref_col, tgt_col


def ident(name):
    """registry name -> C++ identifier, e.g. kFileNameCtorRva -> kFileNameCtor."""
    out = "".join(c if c.isalnum() or c == "_" else "_" for c in name)
    if out.endswith("Rva"):
        out = out[:-3]
    if not out or out[0].isdigit():
        out = "k" + out
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=None)
    args = ap.parse_args()

    cfg = common.load_config(args.config)
    out = common.out_dir(cfg)
    reporter = common.Reporter("addrlib :: emit")

    target, tier_of = {}, {}
    for r in common.read_csv_rows(os.path.join(out, "mapping.csv")):
        if r["tier"] in common.SHIPPING_TIERS:
            a = common.parse_rva(r["ref_rva"])
            target[a] = common.parse_rva(r["tgt_rva"])
            tier_of[a] = r["tier"]
    data_path = os.path.join(out, "data_map.jsonl")
    if os.path.exists(data_path):
        for r in common.read_jsonl(data_path):
            target.setdefault(r["ref_rva"], r["tgt_rva"])
            tier_of.setdefault(r["ref_rva"], common.TIER_NEAR_EXACT)

    # Sorted by reference RVA. The reference RVA *is* the key an address is
    # looked up by - there is no separate ID layer - so sorting by it makes the
    # deltas strictly ascending and lets the runtime binary-search the encoded
    # order directly.
    ref_col = sorted(target)
    tgt_col, missing = [], 0
    for rva in ref_col:
        t = target.get(rva)
        if t is None:
            missing += 1
            tgt_col.append(MISSING)
        else:
            tgt_col.append(t)

    count = len(ref_col)
    ref_id = cfg["builds"]["reference"]["id"]
    tgt_id = cfg["builds"]["target"]["id"]
    version = cfg["emit"]["mapping_version"]

    payload = encode(ref_col, tgt_col)

    # Round-trip before writing. A table that decodes to the wrong addresses is
    # indistinguishable from a correct one until the game jumps into hyperspace,
    # so this is checked here rather than trusted.
    dec_ref, dec_tgt = decode(payload, count)
    if dec_ref != ref_col or dec_tgt != tgt_col:
        bad = next(i for i in range(count)
                   if dec_ref[i] != ref_col[i] or dec_tgt[i] != tgt_col[i])
        raise SystemExit("[!] encoding does not round-trip; first bad entry id %d: "
                         "%s/%s encoded, %s/%s decoded"
                         % (bad, common.fmt_rva(ref_col[bad]),
                            common.fmt_rva(tgt_col[bad]),
                            common.fmt_rva(dec_ref[bad]),
                            common.fmt_rva(dec_tgt[bad])))

    header = HEADER.pack(MAGIC, FORMAT_VERSION, CODEC_VARINT_DELTA, count,
                         len(payload), MISSING,
                         version.encode()[:15], ref_id.encode()[:23],
                         tgt_id.encode()[:23])
    blob = header + payload

    bin_path = os.path.join(common.repo_root(), "tools", "FCSE", "assets",
                            "address_table.bin")
    os.makedirs(os.path.dirname(bin_path), exist_ok=True)
    with open(bin_path + ".tmp", "wb") as fh:
        fh.write(blob)
    os.replace(bin_path + ".tmp", bin_path)

    raw_bytes = count * 8
    reporter.line("entries              : %d" % count)
    reporter.line("absent on %-10s : %d" % (tgt_id, missing))
    reporter.line("mapping version      : %s" % version)
    reporter.line("payload              : %d B (raw pairs would be %d B, %.1fx)"
                  % (len(payload), raw_bytes, raw_bytes / max(len(payload), 1)))
    reporter.line("resource             : %d B  -> assets/address_table.bin" % len(blob))
    reporter.line("round-trip           : OK")

    # ---- symbols.h: the addresses FCSE itself uses, by name ---------------
    named = []
    missing_names = []
    for r in common.read_csv_rows(os.path.join(common.tool_dir(), "names.csv")):
        rva = common.parse_rva(r["steam_rva"])
        named.append((r["name"], rva, r.get("kind", ""), r.get("notes", "")))
        if rva not in target:
            missing_names.append((r["name"], rva))

    lines = [
        "// Generated by tools/FCSE/tools/addrlib - do not edit by hand.",
        "//",
        "// The engine addresses FCSE itself uses, named. Each value is an RVA in the",
        "// reference build (%s) - the same number you would see in" % ref_id,
        "// Ghidra with that DLL open, which is the point: it can be pasted straight",
        "// into a disassembler and checked, unlike an opaque id.",
        "//",
        "// Pass these to AddressLibrary::Address(), which translates a reference-build",
        "// RVA into a live address on whichever build is running.",
        "//",
        "// mapping version : %s" % version,
        "// source          : tools/FCSE/tools/addrlib/names.csv",
        "",
        "#pragma once",
        "",
        "#include <cstdint>",
        "",
        "namespace FCSE {",
        "namespace Symbols {",
        "",
    ]
    for name, rva, kind, notes in sorted(named, key=lambda r: r[0]):
        suffix = ("  // %s" % notes) if notes else ("  // %s" % kind if kind else "")
        lines.append("constexpr uint32_t %s = 0x%08X;%s" % (ident(name), rva, suffix))
    lines += ["", "} // namespace Symbols", "} // namespace FCSE", ""]

    if missing_names:
        reporter.line("[!] %d named symbol(s) are not in the mapping and will resolve "
                      "to 0 at runtime:" % len(missing_names))
        for name, rva in missing_names:
            reporter.line("      %-28s %s" % (name, common.fmt_rva(rva)))

    sym_path = os.path.join(common.repo_root(), "tools", "FCSE", "src", "engine",
                            "address_symbols.h")
    with open(sym_path + ".tmp", "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines))
    os.replace(sym_path + ".tmp", sym_path)
    reporter.line("named symbols        : %d -> src/engine/address_symbols.h" % len(named))

    # Artifacts from earlier designs, if a previous run left them behind.
    for stale in (os.path.join(common.repo_root(), "tools", "FCSE", "src", "engine",
                               "address_table.inc"),
                  os.path.join(common.tool_dir(), "registry.csv")):
        if os.path.exists(stale):
            os.remove(stale)
            reporter.line("removed stale %s" % os.path.basename(stale))

    tiers = defaultdict(int)
    for rva in ref_col:
        tiers[tier_of.get(rva, "absent")] += 1

    manifest = {
        "mapping_version": version,
        "generated": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "entries": count,
        "absent_on_target": missing,
        "resource_bytes": len(blob),
        "payload_bytes": len(payload),
        "format_version": FORMAT_VERSION,
        "codec": CODEC_VARINT_DELTA,
        "tiers": dict(tiers),
        "config_sha256": cfg["_hash"],
        "builds": {},
    }
    for key in ("reference", "target"):
        spec = cfg["builds"][key]
        manifest["builds"][key] = {
            "id": spec["id"], "sha256": spec.get("sha256"),
            "size_of_image": spec.get("size_of_image"),
            "time_date_stamp": "0x%08X" % spec["time_date_stamp"],
        }
    common.write_json(os.path.join(out, "mapping_manifest.json"), manifest)

    reporter.section("tier composition")
    for t in sorted(tiers, key=lambda t: -tiers[t]):
        reporter.line("    %-12s %d" % (t, tiers[t]))
    reporter.save(os.path.join(out, "emit_report.txt"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
