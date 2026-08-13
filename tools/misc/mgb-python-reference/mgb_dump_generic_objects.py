"""Dump options.mgb's GenericObjectTable + Page areas, and resolve the CRC32 name
hashes back to real strings by CRC32-ing every printable ASCII run found in Dunia.dll.

CUIPageBase::Init (FarCry2_server 0x09129ce0) resolves a native page class's authored
page-name string through magma::Id::Hash -> GenericObjectServer::FindGenericObject ->
FullLink::GetLastObject, so the GenericObjectTable is the name->Page registry.
"""
import binascii, glob, importlib.util, os, re, sys

PARSER = r"c:\Projects\FarCry2\tools\JackAll\src\JackAll.Tools\Format\mgb_parser.py"
spec = importlib.util.spec_from_file_location("mgb_parser", PARSER)
mgb = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mgb)

DUNIA = r"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\Dunia.dll"


def build_dict():
    data = open(DUNIA, "rb").read()
    d = {}
    for m in re.finditer(rb"[ -~]{4,120}", data):
        s = m.group()
        d.setdefault(binascii.crc32(s) & 0xFFFFFFFF, s.decode("ascii"))
        # also every suffix starting after a separator, cheap way to catch
        # substrings the binary stores inside a longer literal
    return d


def main(path):
    names = build_dict()
    print(f"dictionary: {len(names)} distinct hashes from Dunia.dll strings\n")

    hdr, _ = mgb.parse_file(path)
    print(f"FILE {os.path.basename(path)} areas={len(hdr.areas)}")

    got = hdr.generic_object_table
    print(f"\n=== GenericObjectTable ===")
    if not got:
        print("  (none)")
    else:
        print(f"  table name_id={got['name_id']:08X} "
              f"({names.get(got['name_id'], '?')}) objects={len(got['objects'])}")
        for o in got["objects"]:
            link = o["link"]
            ids = ",".join(f"{i:08X}" for i in link.get("ids", []))
            tgt = ""
            if link.get("ids"):
                tgt = " -> " + ",".join(names.get(i, f"?{i:08X}") for i in link["ids"])
            print(f"  GO {o['name_id']:08X} {names.get(o['name_id'], '?'):50s} "
                  f"link_type={link.get('type_name', '-')} ids=[{ids}]{tgt}")

    print(f"\n=== Areas ===")
    for i, a in enumerate(hdr.areas):
        print(f"  [{i:2d}] {a['type_name']:10s} {a['name_id']:08X} "
              f"{names.get(a['name_id'], '?'):45s} elems={len(a['elements'])} "
              f"box={a['static_box']}")


if __name__ == "__main__":
    main(sys.argv[1])
