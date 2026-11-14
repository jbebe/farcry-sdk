"""Difficulty Overhaul's brain trace: with SWITCH on the command line, each task a soldier's alert and
combat brains start is logged by the CRC32 of its instance name. names() reads the names back out of the merc
brain the installed game loads.
"""

import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import GAME_BIN

SWITCH = "-braintrace"
LINE = re.compile(r"soldier (\w{8}) brain: (\w{8})")
COMBAT = "::MercBrain/MercBrain/MercBrainCombat/"
ALERT = "::MercBrain/MercBrain/MercBrainAlert"
CLI = Path(__file__).resolve().parents[3] / r"tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"


def names():
    """Every instance of the installed merc brain by its name's CRC32, shortened below the combat brain,
    and the plans the combat and alert brains select among."""
    with tempfile.TemporaryDirectory() as work:
        # A layer that changes the brain puts it in patch.dat; the game's own is in worlds.dat.
        for archive in ("patch.fat", r"worlds\worlds.fat"):
            subprocess.run([CLI, "archive", "extract", GAME_BIN.parent / "Data_Win32" / archive, "--names",
                            "--filter", "mercbrain.ai.rml", "-o", work], check=True, capture_output=True)
            brain = next(Path(work).rglob("mercbrain.ai.rml"), None)
            if brain:
                break
        subprocess.run([CLI, "ai", "unpack", brain], check=True, capture_output=True)
        root = ElementTree.parse(brain.with_name("mercbrain.source.xml")).getroot()
        known = {zlib.crc32(name.encode()): name.removeprefix(COMBAT)
                 for name in (instance.get("Name") for instance in root)}
        brains = [root.find(f"*[@Name='{name}']") for name in (COMBAT.rstrip("/"), ALERT)]
        return known, {plan.removeprefix(COMBAT) for b in brains for plan in (s.get("Task") for s in b.iter("Selectable"))
                       if plan}


def tasks(lines, known):
    """Each soldier's tasks in the order his brain started them, from the trace's lines."""
    started = {}
    for m in filter(None, map(LINE.search, lines)):
        task = int(m.group(2), 16)
        started.setdefault(m.group(1), []).append(known.get(task, f"{task:08X}"))
    return started
