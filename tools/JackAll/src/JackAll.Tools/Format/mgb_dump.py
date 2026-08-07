"""Canonical dump from the Python reference parser, matching MgbDifferentialDumpTests' output
line for line so the two independent implementations of the .mgb spec can be diffed.

Round-tripping proves the C# codec reproduces a file's bytes; it cannot prove those bytes are
*attributed* to the right fields, because swapping two adjacent fields of the same width round-trips
perfectly while labelling both wrong. Comparing decoded values against a separate implementation is
what catches that.

Usage:
    dotnet test src/JackAll.Core.Tests --filter FullyQualifiedName~Dump_corpus_when_requested
        (with MGB_DUMP set to the output path)
    python mgb_dump.py py_dump.txt
    diff py_dump.txt cs_dump.txt      # expected: no output

Note both sides sort the corpus by raw code point; .NET's default Order() is culture-aware and
orders '.' against '_' differently, which shuffles whole file blocks.
"""
import sys, os, glob, importlib.util

PARSER = r"c:\Projects\FarCry2\tools\JackAll\src\JackAll.Tools\Format\mgb_parser.py"
spec = importlib.util.spec_from_file_location("mgb_parser", PARSER)
mgb = importlib.util.module_from_spec(spec); spec.loader.exec_module(mgb)

out = []
def w(s): out.append(s)

def ansi(b): return b.decode("latin1")
def utf16(b): return b.decode("utf-16-le", "replace")
def bit(v): return "1" if v else "0"

def act(a):
    if not a:
        return "-"
    return f"{a['type_name']}:{a['action_count']}"

for path in sorted(glob.glob(r"c:\Projects\FarCry2\tmp\menu\*.mgb")):
    hdr, reader = mgb.parse_file(path)
    st = str(len(hdr.string_table["strings"])) if hdr.string_table else "-"
    got = str(len(hdr.generic_object_table["objects"])) if hdr.generic_object_table else "-"
    defmat = ansi(hdr.default_material_name) if hdr.default_material_name else ""
    w(f"FILE {os.path.basename(path)} page={hdr.dims[0]}x{hdr.dims[1]} "
      f"off={hdr.dims[2]},{hdr.dims[3]} "
      f"mat={len(hdr.materials)} fs={len(hdr.font_substs)} "
      f"fr={len(hdr.font_refs)} ff={len(hdr.font_families)} "
      f"areas={len(hdr.areas)} st={st} got={got} defmat={defmat}")

    for m in hdr.materials:
        w(f"  MAT {m['name_id']:08X} {ansi(m['tex_name'])}")

    for area in hdr.areas:
        box = ",".join(str(v) for v in area["static_box"])
        w(f"  AREA {area['type_name']} {area['name_id']:08X} props={len(area['user_data'])} "
          f"fps={area['frame_rate']} frame={area['current_frame']} elems={len(area['elements'])} "
          f"box={box} act={act(area.get('action'))}")
        for e in area["elements"]:
            nb = str(len(e["neighbors"])) if "neighbors" in e else "-"
            w(f"    EL {e['type_name']} {e['name_id']:08X} "
              f"props={len(e['user_data'])} hidden={bit(e['hidden'])} "
              f"dup={bit(e['is_duplicatable'])} mask={e['mask_mode']} "
              f"kf={e['keyframe_count']} state={mgb.WIDGET_STATE[e['type_name']]} "
              f"act={act(e.get('action'))} nb={nb}")
            wd = e["widget"]
            tn = e["type_name"]
            if tn == "Text":
                if wd.get("localized"):
                    w(f"      TEXTREF {wd['table_id']:08X}/{wd['resource_id']:08X}")
                else:
                    w(f"      TEXT \"{utf16(wd['string'])}\"")
            elif tn in ("AreaInstance", "AutonomousAreaInstance", "ButtonInstance",
                        "CheckBoxInstance", "RadioButtonInstance", "PageInstance"):
                mat = f"{wd['material']['id']:08X}" if wd["material"]["present"] else "-"
                w(f"      INST \"{utf16(wd['name'])}\" mat={mat} idx={wd['final_value']}")
            elif tn == "Image":
                mat = f"{wd['material']['id']:08X}" if wd["material"]["present"] else "-"
                w(f"      IMG mat={mat} blend={wd['blending_mode']} "
                  f"u={wd['addressing_mode_u']} v={wd['addressing_mode_v']}")
            elif tn == "RectShape":
                w(f"      RECT out={bit(wd['is_outlined'])} fill={bit(wd['is_filled'])} "
                  f"blend={wd['blending_mode']}")
            for kf in e.get("keyframes", []):
                s = kf["state"]
                w(f"      KF {kf['name_id']:08X} idx={kf['idx']} interp={kf['interpolation']} "
                  f"flags={s['interpolation_flags']} color={s['state_color']:08X}")

open(sys.argv[1], "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
print(f"{len(out)} lines")
