"""Gives the car the player drives sound ids of the overhaul's own, so every other car plays the game's.

The bank scripts in sounds\\ build each car's engine over the game's own events and under their ids, into
sounds\\built\\. This copies what each car's engine slots play from there, the game's banks standing in
where built\\ has none, into the layer under new ids: each event a slot plays in a bank named after it,
everything below them in one bank of the car's, so no record shares an id with the game's. A slot whose
sound built\\ leaves as the game's keeps the game's id. The wheels' rolling sounds are the game's, GAIN dB
down. The layer then carries no bank of a game id, and src\\own_sound_ids.h and SoundBench's OwnSounds.cs get
the table the plugin writes into a car's sound slots while the player drives it.

    python mods\\vehicle-overhaul\\data\\own_sounds.py
"""
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
MOD = HERE.parent
REPO = MOD.parent.parent
sys.path.insert(0, str(REPO / "scripts"))
from modstatus import MAIN_TREE  # noqa: E402

CLI = MAIN_TREE / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe"
GAMEFILES = MAIN_TREE / "tmp/gamefiles"
RETAIL = [GAMEFILES / "worlds/worlds/soundbinary", GAMEFILES / "downloadcontent/dlc1/entitylibrary/soundbinary"]
DEPLOADS = [GAMEFILES / "worlds/worlds/worlds/world1/generated/world1_depload.dat",
            GAMEFILES / "downloadcontent/dlc1/entitylibrary/downloadcontent/dlc1/generated/entitylibrary_depload.dat"]
BUILT = MAIN_TREE / "mods/vehicle-overhaul/sounds/built"
LAYER = MOD / "layer/mods/soundbinary"
HEADER = MOD / "src/own_sound_ids.h"
BENCH = REPO / "tools/SoundBench/Game/OwnSounds.cs"

# A vehicle's sound slots: the offset on its CVehicleTypeWheeled and the archetype field it is read from.
SLOTS = [(0x2A0, "sndPlayEngineIdleLoop"), (0x2A4, "sndStopEngineIdleLoop"), (0x2A8, "sndEngineLoop"),
         (0x2AC, "sndExtraTorqueEngineLoop"), (0x2B0, "sndEngineIgnition"), (0x2B4, "sndTurnOffEngine"),
         (0x2C8, "sndThrustPedal")]
ENGINE_LOOP = 2
WHEEL_SLOTS = [(0x2DC, "sndWheelRoll_1"), (0x2E0, "sndWheelRoll_2"), (0x2E4, "sndWheelRoll_3"),
               (0x2E8, "sndWheelRoll_4")]
NONE = 0xFFFFFFFF

# Each car's block of new ids and its slots' game ids, as every archetype of it has them; a pair is the
# game's id and one of the overhaul's own (0x00FD....), a bank of built\\ the slot plays as it is.
CARS = [
    (1, "kRover", [0x0044F142, 0x0044F149, 0x0044F143, NONE, 0x0045CA79, 0x0045D712, 0x0044F150]),
    (2, "kBuggy", [0x0044F47B, 0x0044F47C, 0x0044F481, 0x0044F4B9, 0x0045CA6C, 0x0045D2F6, 0x0044F4D1]),
    (3, "kDatsun", [0x0045CBD8, 0x0045CBF3, 0x0045CBD9, NONE, 0x0045CBDB, 0x0045D2FC, 0x0045CBD6]),
    (4, "kLiberty", [0x0044F264, 0x0044F26B, 0x0044F265, NONE, 0x0045CC02, 0x0045D714, 0x0044F272]),
    (5, "kWrangler", [0x0045CCBC, 0x0045CCC3, 0x0045CCBD, NONE, 0x0045CCC1, 0x0045D715, 0x0045CCBA]),
    (6, "kQuad", [0x004EE8C2, 0x004EE8C4, 0x004EE8C3, NONE, 0x004EE8C7, 0x004EE8C8, 0x004EE8C6]),
    (7, "kUnimog", [0x004EE930, 0x004EE933, 0x004EE931, NONE, 0x004EE932, 0x004EE934, 0x004EE92F]),
    # The game's turn-off is its stop of the idle; the overhaul's is an engine-off of its own.
    (8, "kZil", [0x0045CD73, 0x0045CD7A, 0x0045CD74, NONE, 0x0045CD79, (0x0045CD7A, 0x00FD0240), 0x0045CD71]),
]
WHEELS = (0xA, [0x004EEA82, 0x004EEA84, 0x004EEA85, 0x004EEA83])
# The surfaces' banks under the wheels' rolling sounds, and how far down their multilayers go.
SURFACES = {f"8044f{i:03x}" for i in range(0x7F6, 0x80A)}
GAIN = -7.0

# The attributes that name a record to play, and the one a record names an event to stop with. A resource
# switch's default is one of its cases' values instead.
REFERENCES = ("sound", "stop", "play", "default", "event", "resource", "audio")
TARGET = "target"


def references(element):
    """The (element, attribute) pairs under `element` that name a record to play."""
    return [(part, a) for part in element.iter() for a in REFERENCES
            if part.get(a) and not (part.tag == "Switch" and a == "default")]


def run(*args):
    subprocess.run([str(a) for a in args], check=True, stdout=subprocess.DEVNULL)


def own(id):
    return id >> 16 == 0x00FD


def hex8(id):
    return f"0x{id:08x}"


def bank_name(path):
    """The bank a depload path names, or None for anything but a sound bank."""
    path = (path or "").lower()
    prefix, suffix = "soundbinary\\", ".spk"
    return path[len(prefix):-len(suffix)] if path.startswith(prefix) and path.endswith(suffix) else None


class Banks:
    """Banks by name, decoded once, from built\\ over the game's or from the game's alone."""

    def __init__(self, work):
        self.work = work
        self.decoded = {}
        self.children = {}
        self.used = set()
        for path in DEPLOADS:
            xml = work / f"{path.stem}.xml"
            run(CLI, "depload", "decode", path, "-o", xml)
            for resource in ET.parse(xml).getroot().iter("Resource"):
                name = bank_name(resource.get("ID"))
                if name:
                    kids = (bank_name(child.get("ID")) for child in resource)
                    self.children.setdefault(name, []).extend(k for k in kids if k)

    def decode(self, name, built):
        """The bank's root element, its folder of audio and whether it is built\\'s, or None."""
        if (name, built) not in self.decoded:
            folders = ([BUILT] if built else []) + RETAIL
            path = next((f / f"{name}.spk" for f in folders if (f / f"{name}.spk").exists()), None)
            self.decoded[name, built] = None
            if path is not None:
                from_built = path.parent == BUILT
                out = self.work / f"{'built' if from_built else 'game'}-{name}"
                if not out.exists():
                    run(CLI, "spk", "decode", path, "-o", out)
                if from_built:
                    self.used.add(name)
                self.decoded[name, built] = (ET.parse(out / f"{name}.xml").getroot(), out, from_built)
        return self.decoded[name, built]

    def closure(self, event, built):
        """Every record `event` plays, by id: its element, its folder of audio, its bank and whether that is
        built\\'s."""
        name = f"{event:08x}"
        index = {}
        for bank in [name] + self.children.get(name, []):
            decoded = self.decode(bank, built)
            if decoded is not None:
                root, folder, from_built = decoded
                for record in root:
                    index.setdefault(int(record.get("id"), 16), (record, folder, bank, from_built))
        found = {}
        need = [event]
        while need:
            id = need.pop()
            if id in found:
                continue
            if id not in index:
                raise SystemExit(f"{hex8(id)}, played from {hex8(event)}, is in no bank its depload lists")
            found[id] = index[id]
            need.extend(int(part.get(a), 16) for part, a in references(index[id][0]))
        return found


def build(banks, block, events, built, gain_banks=()):
    """One car's slots' banks under new ids into the layer: the slots' ids, new or the game's where kept, the
    new banks, and each audio record's new id by its old."""
    closures = {s: banks.closure(e, built) for s, e in enumerate(events) if e != NONE and not own(e)}
    owned = {s for s, c in closures.items() if not built or any(r[3] for r in c.values())}
    target = {s: int(c[events[s]][0].get(TARGET, hex8(NONE)), 16) for s, c in closures.items()}
    # A stop of an owned sound is owned with it, so it stops the new one.
    owned |= {s for s, t in target.items() if any(events[o] == t for o in owned)}
    if not owned:
        return list(events), [], {}

    ids = {events[s]: 0x00FD0000 | block << 12 | s for s in owned}
    records = {id: r for s in owned for id, r in closures[s].items()}
    inner = sorted(set(records) - set(ids))
    ids.update({id: 0x00FD0000 | block << 12 | 0x100 + n for n, id in enumerate(inner)})
    plays = sorted(ids[events[s]] for s in owned)

    with tempfile.TemporaryDirectory() as folder:
        out = Path(folder)

        def copy(id, preamble):
            record, audio_folder, bank, _ = records[id]
            element = ET.fromstring(ET.tostring(record))
            element.set("id", hex8(ids[id]))
            # spk encode derives a key from the new id.
            element.attrib.pop("key", None)
            if element.get("preamble"):
                element.set("preamble", preamble)
            # A record's own target is an event; a curve's is what it moves.
            for part, attribute in references(element) + [(element, TARGET)]:
                value = part.get(attribute)
                if value and int(value, 16) in ids:
                    part.set(attribute, hex8(ids[int(value, 16)]))
            if element.tag == "Multilayer" and bank in gain_banks:
                element.set("gainDb", f"{float(element.get('gainDb', '0')) + GAIN:g}")
            if element.get("file"):
                audio = f"{ids[id]:08x}{Path(element.get('file')).suffix}"
                shutil.copy(audio_folder / element.get("file"), out / audio)
                element.set("file", audio)
            return element

        def write(name, elements, preamble):
            root = ET.Element("SoundBank", preamble=preamble)
            root.extend(elements)
            ET.ElementTree(root).write(out / f"{name}.xml", encoding="utf-8")
            run(CLI, "spk", "encode", out / f"{name}.xml", "-o", LAYER / f"{name}.spk")

        for s in sorted(owned):
            stoppers = [ids[events[o]] for o in owned if target[o] == events[s]]
            preamble = " ".join(hex8(i) for i in [ids[events[s]]] + stoppers)
            write(f"{ids[events[s]]:08x}", [copy(events[s], preamble)], preamble)
        group = []
        if inner:
            group = [0x80000000 | ids[inner[0]]]
            preamble = " ".join(hex8(i) for i in plays)
            write(f"{group[0]:08x}", [copy(id, preamble) for id in inner], preamble)
    audio = {id: ids[id] for id in inner if records[id][0].tag == "Audio"}
    return [ids.get(e, e) for e in events], plays + group, audio


def header(cars, wheels, held):
    lines = ["// Written by data/own_sounds.py: the sounds the car the player drives plays from the overhaul's own",
             "// banks, each a sound slot on its CVehicleTypeWheeled with the sound written there and the game's; the",
             "// wheels' rolling sounds of every car; and every bank they are in.",
             "#pragma once", "", "#include <cstddef>", "#include <cstdint>", "#include <span>", "",
             "namespace VehicleOverhaul::OwnSounds {", "",
             "struct Slot {", "    ptrdiff_t offset;", "    uint32_t id;", "    uint32_t game;", "};", "",
             "// The slot every car's table has, and a car is known by.",
             f"inline constexpr ptrdiff_t kEngineLoop = 0x{SLOTS[ENGINE_LOOP][0]:03X};", ""]
    for name, _, _, slots, _ in cars + [("kWheels", None, None, wheels, None)]:
        body = ", ".join(f"{{0x{o:03X}, 0x{i:08X}, 0x{g:08X}}}" for o, i, g in slots)
        lines.append(f"inline constexpr Slot {name}[] = {{{body}}};")
    lines += ["", "inline constexpr std::span<const Slot> kCars[] = {"]
    lines += [f"    {name}," for name, *_ in cars]
    lines += ["};", "", "inline constexpr uint32_t kBanks[] = {"]
    lines += ["    " + ", ".join(f"0x{b:08X}" for b in held[i:i + 6]) + "," for i in range(0, len(held), 6)]
    lines += ["};", "", "}", ""]
    HEADER.write_text("\n".join(lines), encoding="utf-8")


def bench(cars, wheels, wheel_banks, audio):
    fields = dict(SLOTS + WHEEL_SLOTS)

    def table(slots):
        return ", ".join(f'["{fields[o]}"] = 0x{i:08X}' for o, i, _ in slots)

    def groups(banks):
        return ", ".join(f"0x{b:08X}" for b in banks + wheel_banks if b >> 31)

    lines = ["// Written by mods/vehicle-overhaul/data/own_sounds.py.", "namespace SoundBench.Game;", "",
             "/// <summary>", "/// The overhaul's own sounds of the car the player drives, its wheels' among them, as its plugin writes",
             "/// them into the car's sound slots, by the game's engine loop of the car; and the banks below them.",
             "/// </summary>", "public static class OwnSounds", "{",
             "    public static readonly IReadOnlyDictionary<uint, IReadOnlyDictionary<string, uint>> ByEngineLoop =",
             "        new Dictionary<uint, IReadOnlyDictionary<string, uint>>", "        {"]
    lines += [f"            [0x{engine:08X}] = new Dictionary<string, uint> {{ {table(slots + wheels)} }},"
              for _, engine, _, slots, _ in cars]
    lines += ["        };", "",
              "    /// <summary>The banks below a car's sounds and its wheels', which no depload lists, by the car's own engine",
              "    /// loop.</summary>",
              "    public static readonly IReadOnlyDictionary<uint, IReadOnlyList<uint>> Banks =",
              "        new Dictionary<uint, IReadOnlyList<uint>>", "        {"]
    lines += [f"            [0x{own_engine:08X}] = [{groups(held)}]," for _, _, own_engine, _, held in cars]
    lines += ["        };", "",
              "    /// <summary>The audio records each record of the bank scripts' builds became, as their WAVs are tagged.</summary>",
              "    public static readonly IReadOnlyDictionary<uint, IReadOnlyList<uint>> Records =",
              "        new Dictionary<uint, IReadOnlyList<uint>>", "        {"]
    lines += [f"            [0x{old:08X}] = [{', '.join(f'0x{n:08X}' for n in sorted(new))}],"
              for old, new in sorted(audio.items())]
    lines += ["        };", "}", ""]
    BENCH.write_text("\n".join(lines), encoding="utf-8")


def main():
    if not BUILT.is_dir():
        raise SystemExit(f"{BUILT} is missing: it holds the bank scripts' builds")
    for old in LAYER.glob("*.spk"):
        old.unlink()
    kept = sorted(BUILT.glob("00fd*.spk"))
    for path in kept:
        shutil.copy(path, LAYER / path.name)
    with tempfile.TemporaryDirectory() as folder:
        banks = Banks(Path(folder))
        wheel_ids, wheel_banks, _ = build(banks, WHEELS[0], WHEELS[1], False, SURFACES)
        cars, audio = [], {}
        for block, name, ids in CARS:
            game = [i[0] if isinstance(i, tuple) else i for i in ids]
            events = [i[1] if isinstance(i, tuple) else i for i in ids]
            new, car_banks, car_audio = build(banks, block, events, True)
            slots = [(SLOTS[s][0], n, game[s]) for s, n in enumerate(new) if own(n)]
            cars.append((name, game[ENGINE_LOOP], new[ENGINE_LOOP], slots, car_banks))
            for old, n in car_audio.items():
                audio.setdefault(old, set()).add(n)
            print(f"{name}: {len(slots)} slots of its own, {len(car_banks)} banks")
        unused = sorted(p.stem for p in BUILT.glob("*.spk")
                        if not p.stem.startswith("00fd") and p.stem not in banks.used | SURFACES)
    if unused:
        print("built\\ banks no slot plays:", " ".join(unused))
    wheels = [(o, i, g) for (o, _), i, g in zip(WHEEL_SLOTS, wheel_ids, WHEELS[1])]
    held = sorted({b for *_, bs in cars for b in bs} | set(wheel_banks) | {int(p.stem, 16) for p in kept})
    header(cars, wheels, held)
    bench(cars, wheels, wheel_banks, audio)
    print(f"{len(list(LAYER.glob('*.spk')))} banks in the layer, {len(held)} to hold")


if __name__ == "__main__":
    main()
