"""Dump one Area of a .mgb in full: UserData properties (name hashes resolved against
Dunia.dll's string pool) and every element with its widget name/type.

Used to confirm that CFCXOptionGamePage's RefreshOptionList arguments ("SETTING_1"...
"SETTING_8", "SETTING_SENSITIVITY", labelListParamName) really are UserData property
names on the Game page's Magma Area.
"""
import binascii, importlib.util, re, sys

PARSER = r"c:\Projects\FarCry2\tools\JackAll\src\JackAll.Tools\Format\mgb_parser.py"
spec = importlib.util.spec_from_file_location("mgb_parser", PARSER)
mgb = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mgb)

DUNIA = r"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\Dunia.dll"


def build_dict():
    data = open(DUNIA, "rb").read()
    d = {}
    for m in re.finditer(rb"[ -~]{3,120}", data):
        s = m.group()
        d.setdefault(binascii.crc32(s) & 0xFFFFFFFF, s.decode("ascii"))
    # explicit guesses the binary may only hold as substrings of longer literals
    for extra in ([f"SETTING_{i}" for i in range(1, 40)] +
                  ["SETTING_SENSITIVITY", "PARAM_LABEL_LIST", "LABEL_LIST", "l_menu_nav_list",
                   "p_menu_nav", "a_title_bar", "t_page_title", "SETTINGS_LABEL_LIST"]):
        d.setdefault(binascii.crc32(extra.encode()) & 0xFFFFFFFF, extra)
    return d


TAGS = {0x02: "u32", 0x07: "float", 0x0C: "bool", 0x10: "str",
        0x11: "link", 0x12: "link", 0x15: "link", 0x13: "strres", 0x14: "none"}


def main(path, want):
    names = build_dict()
    hdr, _ = mgb.parse_file(path)
    want = int(want, 16)
    for idx, a in enumerate(hdr.areas):
        if a["name_id"] != want:
            continue
        print(f"AREA[{idx}] {a['type_name']} {a['name_id']:08X} "
              f"({names.get(a['name_id'], '?')}) elems={len(a['elements'])}")
        print(f"\n--- UserData ({len(a['user_data'])} props) ---")
        for key, tag, v in a["user_data"]:
            val = ""
            if isinstance(v, dict) and "ids" in v:
                val = " -> " + ",".join(names.get(i, f"?{i:08X}") for i in v["ids"])
            elif isinstance(v, (bytes, bytearray)):
                val = " = " + v.decode("latin1")
            elif v is not None:
                val = f" = {v}"
            print(f"  {key:08X} {names.get(key, '?'):28s} "
                  f"tag={tag:#04x}({TAGS.get(tag, '?')}){val}")
        print(f"\n--- Elements ---")
        for i, e in enumerate(a["elements"]):
            extra = ""
            w = e["widget"]
            if e["type_name"] in ("AreaInstance", "AutonomousAreaInstance", "ButtonInstance",
                                  "CheckBoxInstance", "RadioButtonInstance", "PageInstance"):
                lk = w.get("link")
                extra = (' inst="' + w["name"].decode("utf-16-le", "replace") + '" ' +
                         ("link=None" if not lk else
                          f"link(pkg={lk['package_ref']:08X},area="
                          f"{'-' if lk.get('area_ref') is None else format(lk['area_ref'], '08X')},"
                          f"dup={int(lk.get('is_duplicate', 0))})"))
            elif e["type_name"] == "Text" and not w.get("localized"):
                extra = ' text="' + w["string"].decode("utf-16-le", "replace") + '"'
            print(f"  [{i:2d}] {e['type_name']:16s} {e['name_id']:08X} "
                  f"{names.get(e['name_id'], '?'):22s} hidden={int(e['hidden'])} "
                  f"props={len(e['user_data'])}{extra}")
            for key, tag, v in e["user_data"]:
                val = ""
                if isinstance(v, dict) and "ids" in v:
                    val = " -> " + ",".join(names.get(i, f"?{i:08X}") for i in v["ids"])
                elif isinstance(v, (bytes, bytearray)):
                    val = " = " + v.decode("latin1")
                elif v is not None:
                    val = f" = {v}"
                print(f"         prop {key:08X} {names.get(key, '?'):24s} "
                      f"tag={tag:#04x}{val}")
        return
    print(f"area {want:08X} not found")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
