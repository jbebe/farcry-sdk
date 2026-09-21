# Build JackAll's component schema from the registry dumps (no Ghidra needed).
#
#   python build_component_schema.py [out] [..\JackAll\assets\component_schema.json]
#
# Keeps the classes whose registry chain reaches CEntityComponent or CBaseEntity
# and reports where its wire types disagree with binary_classes.xml.

import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter

from derive_size_floors import load_jsonl
from dump_properties import ACCESSOR_KINDS, CONTAINER_KINDS

HERE = os.path.dirname(os.path.abspath(__file__))
ROOTS = {"CEntityComponent": "component", "CBaseEntity": "entity"}

# C++ value type -> JackAll FcbMemberType, for the types whose wire shape is known.
SCALARS = {
    "bool": "Bool", "float": "Float",
    "char": "Int8", "unsigned_char": "UInt8",
    "short": "Int16", "unsigned_short": "UInt16",
    "int": "Int32", "long": "Int32",
    "unsigned_int": "UInt32", "unsigned_long": "UInt32",
    "unsigned_long_long": "UInt64", "EntityId": "UInt64",
    "ndVec_tpl<float,2>": "Vector2", "ndVec_tpl<float,3>": "Vector3",
    "ndVec_tpl<float,4>": "Vector4", "ndAngle3<float>": "Vector3",
    "Gear::Quaternion4<float>": "Vector4", "Matrix44_tpl<float>": "Matrix4",
    "CryStringBase<char>": "String", "char_const*": "String",
    "CStringID": "Hash", "CNoCaseStringID": "Hash", "CPathID": "Hash",
}
VECTORS = {
    "unsigned_int": "UInt32Array", "int": "Int32Array", "float": "FloatArray",
    "ndVec_tpl<float,3>": "Vector3Array", "CStringID": "HashArray",
}
RE_VECTOR = re.compile(r"^CryVector<(?P<item>.+?),NoLock,CryVectorProperties<32u,5u,26u>>$")
RE_ARRAY = re.compile(r"^(?P<item>.+)\[\d+\]$")
RE_HANDLE = re.compile(r"^CSceneObjectHandle<(?P<cls>.+)>$")
STRUCT_HANDLERS = ("GenericTypeHandler", "NoChildTypeHandler")
DATA_KINDS = ACCESSOR_KINDS | {"CGenericMember", "COffsetMember"}


def wire_type(value_type, handler):
    """C++ member type + handler -> FcbMemberType name, or None if unknown."""
    if not value_type:
        return None
    if handler and handler.startswith("SoundIDHandler"):
        return "String"
    if handler and handler.startswith("BasicTypeHandlerEncode64"):
        return None
    m = RE_ARRAY.match(value_type)
    if m:
        value_type = m.group("item")
    if value_type.startswith("TEntityHandle<"):
        return "UInt64"
    m = RE_VECTOR.match(value_type)
    if m:
        return VECTORS.get(m.group("item"))
    return SCALARS.get(value_type)


def parent_of(klass):
    for key in ("inherits", "bases"):
        if klass.get(key):
            return klass[key][0]
    return None


def chain(name, parents):
    seen = []
    while name and name not in seen:
        seen.append(name)
        name = parents.get(name)
    return seen


def embedded_class(row, rows_of):
    """The registered class a struct-typed member loads, if it has one."""
    m = RE_HANDLE.match(row["value_type"] or "")
    cls = m.group("cls") if m else row["value_type"]
    return cls if cls in rows_of and row["handler"].startswith(STRUCT_HANDLERS) else None


def build_members(rows, rows_of, parents, seen=()):
    """Registry rows of one class -> nested member list (groups hold their members).

    A member whose type is itself a registered class expands into that class's members:
    NoChildTypeHandler loads them from the same node ("embedded"), GenericTypeHandler from a
    child named after the member ("group").
    """
    children = {}
    for r in rows:
        children.setdefault(r["parent"], []).append(r)

    def emit(parent):
        out = []
        scope = children.get(parent, [])
        labels = {r["name"][4:]: r["labels"] for r in scope
                  if r["kind"] == "CEnumMember" and r["name"] and r["name"].startswith("enum")}
        for r in scope:
            kind, name = r["kind"], r["name"]
            if not name or kind in ("CSerializationEvent", "CEnumMember"):
                continue
            m = {"name": name, "flags": r["flags"]}
            embedded = embedded_class(r, rows_of) if kind in DATA_KINDS else None
            if embedded and embedded not in seen:
                m["kind"] = "embedded" if r["handler"].startswith("NoChildTypeHandler") else "group"
                m["cpp"] = embedded
                m["members"] = [x for c in reversed(chain(embedded, parents)) if c in rows_of
                                for x in build_members(rows_of[c], rows_of, parents, seen + (embedded,))]
            elif kind in DATA_KINDS:
                m["kind"] = "value"
                m["type"] = wire_type(r["value_type"], r["handler"])
                m["cpp"] = r["value_type"]
                if name.startswith("sel") and labels.get(name[3:]):
                    m["labels"] = labels[name[3:]]
            elif kind in CONTAINER_KINDS:
                m["kind"] = "container"
                m["element"] = r["child_name"]
                m["wrapped"] = r["wrapped"]
                if r["labels"]:
                    m["labels"] = r["labels"]
            elif kind == "CGroupMember":
                m["kind"] = "group"
                m["members"] = emit(r["index"])
            elif kind == "CConditionalGroupMember":
                m["kind"] = "conditional"
                m["condition"] = r["getter"]
                m["members"] = emit(r["index"])
            else:
                continue
            out.append(m)
        return out

    return emit(None)


def xml_types(path):
    """binary_classes.xml -> {(class, member): type} for named members."""
    out = {}
    for c in ET.parse(path).getroot().iter("class"):
        cname = c.get("name")
        for m in c.findall("member"):
            if cname and m.get("name"):
                out[(cname, m.get("name"))] = (m.text or "").strip()
    return out


def walk(members):
    for m in members:
        yield m
        yield from walk(m.get("members", []))


def main():
    outdir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "out")
    target = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        HERE, "..", "JackAll", "assets", "component_schema.json")

    rows = load_jsonl(os.path.join(outdir, "register_properties.jsonl"))
    classes = load_jsonl(os.path.join(outdir, "register_properties_classes.jsonl"))
    uses = {u["class"]: u for u in load_jsonl(os.path.join(outdir, "component_uses.jsonl"))}

    parents = {c["owner"]: parent_of(c) for c in classes}
    rows_of = {}
    for r in rows:
        rows_of.setdefault(r["owner"], []).append(r)

    kinds = {}
    for name in parents:
        for ancestor in chain(name, parents):
            if ancestor in ROOTS:
                kinds[name] = ROOTS[ancestor]
                break
    included = set(kinds)
    for name in list(kinds):
        included.update(chain(name, parents))

    out = []
    for name in sorted(included):
        u = uses.get(name, {})
        out.append({
            "name": name,
            "parent": parents.get(name),
            "kind": kinds.get(name, "base"),
            "creatable": bool(u.get("creatable")),
            "uses": sorted(t for t in u.get("uses", {}) if kinds.get(t) == "component"),
            "members": build_members(rows_of.get(name, []), rows_of, parents),
        })

    with open(target, "w", encoding="utf-8", newline="\n") as fh:
        json.dump({"generator": "tools/fc2re/build_component_schema.py", "classes": out},
                  fh, indent=1, ensure_ascii=True)
        fh.write("\n")

    members = [m for c in out for m in walk(c["members"])]
    data = [m for m in members if m["kind"] == "value"]
    print("classes: %d (%d components, %d entities, %d bases), %d creatable components" % (
        len(out), sum(c["kind"] == "component" for c in out), sum(c["kind"] == "entity" for c in out),
        sum(c["kind"] == "base" for c in out), sum(c["kind"] == "component" and c["creatable"] for c in out)))
    print("members: %d, data %d, typed %d, with labels %d" % (
        len(members), len(data), sum(m["type"] is not None for m in data),
        sum("labels" in m for m in members)))
    print("untyped C++ types:", Counter(m["cpp"] for m in data if m["type"] is None).most_common(12))

    xml = xml_types(os.path.join(HERE, "..", "JackAll", "assets", "binary_classes.xml"))
    agree, disagree = 0, Counter()
    for c in out:
        for m in walk(c["members"]):
            want = xml.get((c["name"], m["name"]))
            if want and m.get("type"):
                if want == m["type"]:
                    agree += 1
                else:
                    disagree[(m["cpp"], m["type"], want)] += 1
    print("vs binary_classes.xml: %d agree, %d disagree" % (agree, sum(disagree.values())))
    for (cpp, got, want), n in disagree.most_common(20):
        print("  %4d  %-28s ours %-10s xml %s" % (n, cpp, got, want))
    print("-> %s" % os.path.normpath(target))


if __name__ == "__main__":
    main()
