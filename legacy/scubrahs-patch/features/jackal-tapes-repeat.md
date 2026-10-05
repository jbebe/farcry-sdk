---
title: Jackal tapes no longer repeat
kind: component
bundle: fixes
claims:
  - "Fixed the repeating Jackal Tapes bug (thanks FoxAhead)"
status: located
systems: [missions, engine]
match:
  - "install/bin/dunia.dll@0x74e465"
exclude: []
requires: []
existing: mods/UFCP — src/fixes/jackal_tapes.cpp, byte for byte, both builds
verified: re
---

# Jackal tapes no longer repeat

Picking up a Jackal tape in the southern map plays the next recording instead of the same one every
time.

## How

One `Dunia.dll` patch in the tape picker (`FUN_1074e3f0`): the loop's first `jne +0A` becomes
`jne +14`, so a tape already played is skipped. It is the same edit UFCP makes.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| picker loop | `0x1074E465` | `0x10740F55` | `0A -> 14` |

Pattern (UFCP's; one match in each build; site at +1): `75 0A 3B CA 75 0A 80 7E 75 00 75 16`
