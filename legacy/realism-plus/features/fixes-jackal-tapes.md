---
title: Jackal tapes no longer repeat
kind: component
bundle: fixes
status: located
systems: [missions, engine]
match: []
exclude: []
requires: [engine-dunia-dll]
existing: mods/UFCP — src/fixes/jackal_tapes.cpp, byte for byte, both builds
verified: re
---

# Jackal tapes no longer repeat

Picking up a Jackal tape plays the next recording instead of the same one every time.

## How

One `Dunia.dll` patch in the tape picker: the loop's first `jne +0A` becomes `jne +14`. It is the edit
UFCP makes and Scubrah's Patch ships
([`jackal-tapes-repeat`](../../scubrahs-patch/features/jackal-tapes-repeat.md)).

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| picker loop | `0x1074E465` | `0x10740F55` | `0A -> 14` |

Pattern (UFCP's; one match in GOG at `0x10740F54`, site at +1): `75 0A 3B CA 75 0A 80 7E 75 00 75 16`
