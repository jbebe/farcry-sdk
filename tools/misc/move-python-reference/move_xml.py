"""Converts a MOVE graph to and from an editable XML document.

An interchange format, not one the game loads - the same relationship .fcb has with
Gibbed's XML. Export it, edit it, build it back to a binary .bin.

It is not the engine's own authoring format either. movemgrnamed.bin is closer to
that, but it addresses objects by GUID rather than by stream position and no shipped
executable can read it, so it cannot be the basis for something that builds a
loadable file. What this borrows from the engine is the vocabulary: every field name
is the debug string the matching Transfer call passes.

Pass --names with a *named* twin and criteria are labelled with the channel and enum
value they test. Those labels are informational; the builder ignores them.

  move_xml.py export dlc1.bin dlc1.xml --names movemgrnamed.bin
  move_xml.py build  dlc1.xml out.bin
  move_xml.py verify dlc1.bin --names movemgrnamed.bin
"""
import argparse
import struct
from xml.etree import ElementTree
from xml.sax.saxutils import quoteattr

import move_codec as mc

ROOT = "MoveGraph"
PRINTABLE = set(range(0x20, 0x7F))


def _text(b):
    """A string field as readable text, or None if it is not clean ASCII."""
    return b.decode("ascii") if b and all(c in PRINTABLE for c in b) else None


def to_xml(mf, channels=None):
    out = ['<?xml version="1.0" encoding="utf-8"?>']
    out.append('<%s type=%s version="%d" flags="0x%08X">'
               % (ROOT, quoteattr(struct.pack("<I", mf.mtype).decode("latin1").rstrip("\0")),
                  mf.mversion, mf.flags))
    for kind, name, value in mf.root.ops:
        _emit(out, kind, name, value, 1, channels or [], None)
    out.append("</%s>" % ROOT)
    out.append("")
    return "\n".join(out)


def _annotate(channels, channel, name, value):
    """Informational channel/enum names; the builder ignores them."""
    if not channels:
        return ""
    if name == "m_eValueID" and value < len(channels):
        return " channel=%s" % quoteattr(channels[value][0])
    if name == "m_Value" and channel is not None and channel < len(channels):
        values = channels[channel][1]
        if values and 0 <= value < len(values):
            return " enum=%s" % quoteattr(values[value])
    return ""


def _emit(out, kind, name, value, depth, channels, channel):
    pad = "  " * depth
    n = quoteattr(name)
    if kind == "pnew":
        out.append("%s<obj n=%s class=%s id=\"%d\">"
                   % (pad, n, quoteattr(value.cls), value.index))
        inner = mc.field(value, "m_eValueID")
        for k2, n2, v2 in value.ops:
            _emit(out, k2, n2, v2, depth + 1, channels, inner)
        out.append("%s</obj>" % pad)
    elif kind == "pref":
        out.append('%s<ref n=%s id="%d"/>' % (pad, n, value.index))
    elif kind == "pnull":
        out.append("%s<null n=%s/>" % (pad, n))
    elif kind == "nover":
        out.append("%s<nover n=%s/>" % (pad, n))
    elif kind == "ver":
        out.append('%s<ver n=%s v="%d"/>' % (pad, n, value))
    elif kind == "f32":
        out.append("%s<f32 n=%s %s/>" % (pad, n, _float_attr(value)))
    elif kind in ("u8", "u32", "s32"):
        out.append('%s<%s n=%s v="%d"%s/>'
                   % (pad, kind, n, value, _annotate(channels, channel, name, value)))
    elif kind == "str":
        text = _text(value)
        body = "v=%s" % quoteattr(text) if text is not None else 'hex="%s"' % value.hex()
        out.append("%s<str n=%s %s/>" % (pad, n, body))
    else:
        out.append('%s<%s n=%s hex="%s"/>' % (pad, kind, n, value.hex()))


def _float_attr(raw):
    """Prefer readable decimal, fall back to hex when it would not round-trip."""
    value = struct.unpack("<f", raw)[0]
    text = repr(value)
    try:
        if struct.pack("<f", float(text)) == raw:
            return 'v="%s"' % text
    except (OverflowError, ValueError):
        pass
    return 'hex="%s"' % raw.hex()


def from_xml(text):
    root = ElementTree.fromstring(text)
    if root.tag != ROOT:
        raise mc.Drift("expected a <%s> document, got <%s>" % (ROOT, root.tag))
    mf = mc.MoveFile()
    raw_type = root.get("type", "MVM").encode("latin1")
    mf.mtype = struct.unpack("<I", raw_type.ljust(4, b"\0"))[0]
    mf.mversion = int(root.get("version"))
    mf.flags = int(root.get("flags"), 0)
    mf.root = mc.Obj("#file")
    by_id = {}
    for child in root:
        mf.root.ops.append(_parse(child, by_id))
    mf.seq = [by_id[k] for k in sorted(by_id)]
    return mf


def _parse(el, by_id):
    name = el.get("n", "")
    tag = el.tag
    if tag == "obj":
        obj = mc.Obj(el.get("class"))
        obj.index = int(el.get("id"))
        by_id[obj.index] = obj
        for child in el:
            obj.ops.append(_parse(child, by_id))
        return ("pnew", name, obj)
    if tag == "ref":
        return ("pref", name, by_id[int(el.get("id"))])
    if tag == "null":
        return ("pnull", name, None)
    if tag == "nover":
        return ("nover", name, 0)
    if tag == "ver":
        return ("ver", name, int(el.get("v")))
    if tag == "f32":
        hexed = el.get("hex")
        raw = bytes.fromhex(hexed) if hexed else struct.pack("<f", float(el.get("v")))
        return ("f32", name, raw)
    if tag in ("u8", "u32", "s32"):
        return (tag, name, int(el.get("v")))
    if tag in ("str", "data", "raw"):
        hexed = el.get("hex")
        raw = bytes.fromhex(hexed) if hexed is not None else el.get("v").encode("ascii")
        return (tag, name, raw)
    raise mc.Drift("unexpected element <%s>" % tag)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=("export", "build", "verify"))
    ap.add_argument("src")
    ap.add_argument("dst", nargs="?")
    ap.add_argument("--names", metavar="NAMEDBIN",
                    help="a *named* twin to label channels and enum values with")
    args = ap.parse_args()
    channels = mc.channel_table(args.names) if args.names else None

    if args.mode == "build":
        open(args.dst, "wb").write(mc.save(from_xml(open(args.src, encoding="utf-8").read())))
        return
    xml = to_xml(mc.load(args.src), channels)
    if args.mode == "export":
        open(args.dst, "w", encoding="utf-8").write(xml)
        return
    original = open(args.src, "rb").read()
    rebuilt = mc.save(from_xml(xml))
    print("%s" % args.src)
    print("   %d bytes -> %d chars of XML -> %d bytes" % (len(original), len(xml), len(rebuilt)))
    print("   bin -> xml -> bin: %s"
          % ("byte-identical" if rebuilt == original else "*** MISMATCH ***"))


if __name__ == "__main__":
    main()
