"""The notebook's arm clips in the layer: the map's banks as arms.py remade them, at their own paths
(eqmpcp becomes eqnote), MOVE weapon 9's branches cloned onto NOTEBOOK and repointed at them, and
each registered in each world's depload wherever the map's is.

    python clips.py <folder the banks were extracted to> <layer> <work folder>
"""
import os
import re
import shutil
import subprocess
import sys
import zlib

BANKS, LAYER, WORK = sys.argv[1], sys.argv[2], sys.argv[3]
REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
JACKALL = os.path.join(REPO, r"tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe")
GAME = os.path.join(REPO, r"tmp\gamefiles")
RETAIL_MOVE = GAME + r"\common\graphics\move\movemgr.bin"
WORLDS = {w: GAME + r"\worlds\worlds\worlds\%s\generated\%s_depload.dat" % (w, w) for w in ("world1", "world2")}
FOLDER = r"graphics\characters\_common\animations\equipment\equipped_gadget"
MAP, NOTEBOOK = 9, 44


def run(*args):
    result = subprocess.run([JACKALL, *args], capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stdout, result.stderr)
        raise SystemExit("failed: " + " ".join(args))
    return result.stdout


def crc(path):
    return zlib.crc32(path.lower().encode())


def state_names():
    table = {}
    for line in open(os.path.join(REPO, r"tools\JackAll\assets\fc2.movenames.tsv"), encoding="utf-8"):
        if not line.startswith("#") and "\t" in line:
            hash_, name = line.rstrip("\n").split("\t")[:2]
            table[int(hash_, 16)] = name
    return table


os.makedirs(WORK, exist_ok=True)
os.makedirs(os.path.join(LAYER, "mods", FOLDER), exist_ok=True)
pairs = []
for directory, _, files in os.walk(BANKS):
    for old in sorted(name for name in files if name.endswith(".mab")):
        new = old.replace("eqmpcp", "eqnote")
        pairs.append((FOLDER + "\\" + old, FOLDER + "\\" + new))
        shutil.copyfile(os.path.join(directory, old), os.path.join(LAYER, "mods", FOLDER, new))

with open(os.path.join(WORK, "pairs.tsv"), "w", encoding="utf-8") as tsv:
    tsv.writelines("%s\t%s\n" % pair for pair in pairs)

cloned, repointed = os.path.join(WORK, "cloned.bin"), os.path.join(WORK, "repointed.bin")
print(run("move", "clone", RETAIL_MOVE, cloned, "--from", str(MAP), "--to", str(NOTEBOOK)).strip().splitlines()[0])
print(run("move", "repoint", cloned, repointed, "--weapon", str(NOTEBOOK), "--map", os.path.join(WORK, "pairs.tsv")))
fragments = os.path.join(LAYER, "mods", "graphics", "move", "movemgr.bin")
shutil.rmtree(fragments, ignore_errors=True)
run("move", "fragments", repointed, "--base", RETAIL_MOVE, "--out", fragments)
print(len(os.listdir(fragments)), "MOVE fragments")

names = state_names()
for world, retail in WORLDS.items():
    scratch = os.path.join(WORK, os.path.basename(retail))
    shutil.copyfile(retail, scratch)
    run("depload", "decode", scratch, "-o", scratch + ".xml")
    parents, parent = {}, None
    for line in open(scratch + ".xml", encoding="utf-8"):
        if line.startswith("\t<") and not line.startswith("\t</"):
            parent = int(re.search(r'crc_ID="(\d+)"', line).group(1))
        elif line.startswith("\t\t") and (found := re.search(r'crc_ID="(\d+)"', line)):
            parents.setdefault(int(found.group(1)), []).append(parent)
    touched = {}
    for old, new in pairs:
        for owner in parents.get(crc(old), []):
            run("depload", "add", scratch, "--parent", "%08X" % owner, "--child", new, "--type", "CAnimationResource")
            touched[owner] = new
    out = os.path.join(LAYER, "mods", "worlds", world, "generated", world + "_depload.dat")
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)
    for owner, child in touched.items():
        label = names.get(owner, "%08X" % owner)
        run("depload", "add", scratch, "--parent", label, "--child", child, "--type", "CAnimationResource",
            "--fragment", "-o", os.path.join(out, "%s.%d.xml" % (label, owner)))
    print(world, len(touched), "depload parents:", ", ".join(names.get(p, "%08X" % p) for p in touched))
