"""Where two mods collide: the same engine function hooked, the same layer file shipped, the same
control or default key added, or the same string given two wordings.

Same-address hooks stack in FCSE, so a hook collision is not necessarily a bug - it is a place where
two mods can step on each other.
"""
import bisect
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

import modstatus
from modstatus import REPO

ADDRESS_TABLE = REPO / "tools" / "FCSE" / "assets" / "address_table.bin"
ADDRLIB = REPO / "tools" / "FCSE" / "tools" / "addrlib"
VANILLA_CONTROLS = REPO / "tmp" / "gamefiles" / "common" / "config" / "defaultusercontrols.xml"
CONTROL_FRAGMENTS = "mods/config/defaultusercontrols.xml/"
STRINGS = "mods/languages/english/oasisstrings.fragment.xml"
# Element-level checks below cover these, so a shared path alone is not a collision. An action map
# binds a control's default keys, which the control check already compares.
CHECKED_ELSEWHERE = (CONTROL_FRAGMENTS, "mods/config/inputactionmap", "oasisstrings.fragment.xml")
# COM methods hooked through a vtable slot, recognised by the detour's name.
COM_METHODS = ("Present", "EndScene", "BeginScene", "Reset", "DrawIndexedPrimitive", "DrawPrimitive",
               "CreateVertexShader", "CreatePixelShader", "SetVertexShader", "SetPixelShader",
               "GetDeviceState", "GetDeviceData", "SetRenderState", "SetTexture")


@dataclass(frozen=True)
class Collision:
    kind: str
    a: str
    b: str
    # What the mods do, as a verb phrase: "ship X", "hook Y".
    what: str
    # What they share - a function address, a container, a control - which a STATUS.md decision may
    # name instead of the other mod, to settle every pair at once.
    thing: str = ""
    # (mod, how its source names the thing) for each side, where that differs from `thing`.
    via: tuple = ()

    def involves(self, key):
        return key in (self.a, self.b)

    def other(self, key):
        return self.b if key == self.a else self.a

    def sentence(self, mods=2):
        return ("all " if mods > 2 else "both ") + self.what


# --- hooks ----------------------------------------------------------------------------------------

def _address_table():
    """(Steam function starts, (retail RVA, Steam RVA) pairs), from FCSE's address table."""
    sys.path.insert(0, str(ADDRLIB))
    import emit
    blob = ADDRESS_TABLE.read_bytes()
    count = emit.HEADER.unpack(blob[:emit.HEADER.size])[3]
    uplay, retail = emit.decode(blob[emit.HEADER.size:], count)
    pairs = sorted(zip(uplay, retail))
    return [u for u, _ in pairs], sorted((r, u) for u, r in pairs if r != emit.MISSING)


class Resolver:
    """Turns an address as a plugin names it into the Steam-build function that contains it."""

    def __init__(self):
        self.starts, self.by_retail = _address_table()
        self._retail_keys = [r for r, _ in self.by_retail]
        self._sections = {}
        cache = Path(modstatus.git("rev-parse", "--git-common-dir").strip())
        self._cache_path = (cache if cache.is_absolute() else REPO / cache) / "modstatus-patterns.json"
        try:
            self._cache = json.loads(self._cache_path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            self._cache = {}

    def save(self):
        self._cache_path.write_text(json.dumps(self._cache), encoding="utf-8")

    def function(self, uplay_rva):
        index = bisect.bisect_right(self.starts, uplay_rva) - 1
        return self.starts[index] if index >= 0 else uplay_rva

    def from_retail(self, rva):
        index = bisect.bisect_right(self._retail_keys, rva) - 1
        if index < 0:
            return None
        retail, uplay = self.by_retail[index]
        return uplay + (rva - retail)

    def pattern(self, text):
        """The pattern's Steam RVA, or None if it matches no build exactly once."""
        import verify_patterns
        for build, path in (("uplay", Path(verify_patterns.DEFAULT_UPLAY)), ("retail", Path(verify_patterns.DEFAULT_RETAIL))):
            if not path.exists():
                continue
            stat = path.stat()
            key = f"{build}|{stat.st_size}:{int(stat.st_mtime)}|{text}"
            if key not in self._cache:
                if build not in self._sections:
                    self._sections[build] = verify_patterns.text_section(str(path))
                base, data = self._sections[build]
                hits = [m.start() for m, _ in zip(re.finditer(verify_patterns.to_regex(text), data, re.S), range(2))]
                self._cache[key] = base + hits[0] if len(hits) == 1 else None
            rva = self._cache[key]
            if rva is not None:
                return rva if build == "uplay" else self.from_retail(rva)
        return None


RELOCATION = re.compile(r"FCSE::(Uplay|Retail)\(\s*(0x[0-9A-Fa-f]+)\s*\)|FCSE::Pattern\(\s*((?:\"[^\"]*\"\s*)+)\)")
NAME_BEFORE = re.compile(r"(\w+)\s*(?:\{|\(|=)\s*(?:FCSE::Relocation<[^;{}]*>\s*\{\s*)?$")
CALL = re.compile(r"(?:->|\.)\s*(Hook|MidHook|Patch)\s*\(")


def _call_text(source, start):
    depth, i = 1, start
    while i < len(source) and depth:
        depth += {"(": 1, ")": -1}.get(source[i], 0)
        i += 1
    return source[start:i - 1]


def hooks(project, resolver):
    """{function key: (description, relocation names)} for each engine function the plugin hooks or
    patches: every FCSE::Uplay/Retail/Pattern relocation that reaches a Hook, MidHook or Patch call."""
    found = {}
    for path in sorted((project.path / "src").rglob("*.cpp")):
        source = path.read_text(encoding="utf-8", errors="replace")
        relocations = {}
        anonymous = []
        for m in RELOCATION.finditer(source):
            address = ("uplay" if m.group(1) == "Uplay" else "retail", int(m.group(2), 16)) if m.group(1) \
                else ("pattern", "".join(re.findall(r"\"([^\"]*)\"", m.group(3))))
            name = NAME_BEFORE.search(source[max(0, m.start() - 200):m.start()])
            if name and name.group(1) not in ("Relocation", "Data"):
                relocations[name.group(1)] = address
            elif address[0] == "pattern":
                anonymous.append(address)
        targets = [("", address) for address in anonymous]
        for call in CALL.finditer(source):
            text = _call_text(source, call.end())
            names = [n for n in re.findall(r"\b(\w+)\b", text) if n in relocations]
            if not names:
                first = text.split(",", 1)[0]
                local = re.search(r"\b(\w+)\b\W*$", first.replace(".address()", "").replace(".get()", ""))
                if local:
                    before = source[max(0, call.start() - 2500):call.start()]
                    assigned = re.findall(rf"\b{local.group(1)}\b\s*=\s*([^;]+);", before)
                    names = [n for rhs in assigned[-1:] for n in re.findall(r"\b(\w+)\b", rhs) if n in relocations]
            targets += [(n, relocations[n]) for n in names]
            if not names:
                detour = re.search(r"&(\w+?)Detour\b", text)
                if detour and detour.group(1) in COM_METHODS:
                    found[detour.group(1)] = (f"the Direct3D/DirectInput method {detour.group(1)}", set())
        for name, (kind, value) in targets:
            rva = value if kind == "uplay" else resolver.from_retail(value) if kind == "retail" \
                else resolver.pattern(value)
            if rva is None:
                continue
            function = resolver.function(rva)
            entry = found.setdefault(f"0x{function:08X}", (f"Dunia.dll function 0x{function:08X} (Steam build)", set()))
            if name:
                entry[1].add(name)
    return found


# --- layers ---------------------------------------------------------------------------------------

def layer_files(project, plugins=False):
    """{path inside layer/: file} on disk, generated files included; plugins/ only when asked."""
    layer = project.path / "layer"
    files = {}
    for root, folders, names in os.walk(layer):
        inside = Path(root).relative_to(layer).as_posix()
        if inside == "." and not plugins:
            folders[:] = [f for f in folders if f != "plugins"]
        for name in names:
            files[name if inside == "." else f"{inside}/{name}"] = Path(root) / name
    return files


def _controls(path):
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError):
        return {}
    return {c.get("name"): dict(c.attrib) for c in root.iter("Control") if c.get("name")}


def _default_keys(controls):
    return {value: name for name, attrs in controls.items() for k, value in attrs.items() if k.startswith("key")}


def strings(project):
    path = project.path / "layer" / STRINGS
    if not path.exists():
        return {}
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return {}
    return {(section.get("name"), s.get("enum")): s.get("value")
            for section in root.iter("section") for s in section.iter("string")}


def _same_bytes(a, b):
    return a.stat().st_size == b.stat().st_size and a.read_bytes() == b.read_bytes()


# --- all pairs ------------------------------------------------------------------------------------

def compute(projects):
    """Every collision between two of the projects, each pair named by folder in sorted order."""
    out = set()
    plugins = [p for p in projects if p.is_plugin and (p.path / "src").is_dir()]
    if ADDRESS_TABLE.exists() and plugins:
        resolver = Resolver()
        hooked = {p.key: hooks(p, resolver) for p in plugins}
        resolver.save()
        for a, b in _each_pair(hooked):
            for key in sorted(hooked[a].keys() & hooked[b].keys()):
                via = tuple((mod, "/".join(sorted(hooked[mod][key][1]))) for mod in (a, b) if hooked[mod][key][1])
                out.add(Collision("hook", a, b, f"hook {hooked[a][key][0]}", key, via))

    files = {p.key: layer_files(p) for p in projects}
    for a, b in _each_pair(files):
        shared = {}
        for rel in sorted(files[a].keys() & files[b].keys()):
            if not any(c in rel for c in CHECKED_ELSEWHERE) and not _same_bytes(files[a][rel], files[b][rel]):
                shared.setdefault(modstatus.container(rel), []).append(rel)
        for rel in shared.pop(None, []):
            out.add(Collision("layer", a, b, f"ship {rel}, and one replaces the other by load order",
                              rel.split("/")[-1]))
        for container, rels in sorted(shared.items()):
            example = rels[0].split(container + "/", 1)[-1]
            more = f" and {len(rels) - 1} more" if len(rels) > 1 else ""
            out.add(Collision("layer", a, b, f"edit {container} ({example}{more}); JackAll merges them "
                                             "unless both change the same field", container))

    if VANILLA_CONTROLS.exists():
        vanilla = _controls(VANILLA_CONTROLS)
        controls = {}
        for key, layer in files.items():
            found = {}
            for rel, path in layer.items():
                if rel.startswith(CONTROL_FRAGMENTS):
                    found.update(_controls(path))
            controls[key] = {name: attrs for name, attrs in found.items() if vanilla.get(name) != attrs}
        keys = {key: _default_keys(changed) for key, changed in controls.items()}
        vanilla_keys = _default_keys(vanilla)
        for a, b in _each_pair(controls):
            for name in sorted(controls[a].keys() & controls[b].keys()):
                out.add(Collision("control", a, b, f"add or change control {name}", name))
            for key in sorted(keys[a].keys() & keys[b].keys()):
                if keys[a][key] != keys[b][key] and key not in vanilla_keys:
                    out.add(Collision("control", a, b, f"bind {key} by default ({a}: {keys[a][key]}, "
                                                       f"{b}: {keys[b][key]})", key))

    texts = {p.key: strings(p) for p in projects}
    for a, b in _each_pair(texts):
        for key in sorted(texts[a].keys() & texts[b].keys()):
            if texts[a][key] != texts[b][key]:
                out.add(Collision("string", a, b, f"word string {key[0]}/{key[1]} differently", key[1]))
    return sorted(out, key=lambda c: (c.a, c.b, c.kind, c.what))


def _each_pair(by_key):
    keys = sorted(k for k, v in by_key.items() if v)
    for i, a in enumerate(keys):
        for b in keys[i + 1:]:
            yield a, b


def decided(collision, by_key):
    """The decision either mod's STATUS.md records for the other mod or for the shared thing, or ''."""
    for key, other in ((collision.a, collision.b), (collision.b, collision.a)):
        project = by_key.get(key)
        if project is None:
            continue
        for name in (other.lower(), collision.thing.lower()):
            if name and name in project.status.collisions:
                return project.status.collisions[name]
    return ""


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    everything = modstatus.projects()
    by_key = {p.key: p for p in everything}
    for c in compute(everything):
        decision = decided(c, by_key)
        via = "".join(f" {mod} as {names};" for mod, names in c.via)
        print(f"{c.a} <-> {c.b} [{c.kind}] {c.sentence()}{via}" + (f"  (decided: {decision})" if decision else ""))
