---
title: Reputation kept after story missions
kind: component
bundle: gameplay
claims:
  - "The player's reputation level will no longer be lowered/reset after completing certain story missions"
status: located
systems: [player, missions, engine]
match:
  - "install/bin/dunia.dll@0x7535{ca,f2}"
exclude: []
requires: []
verified: re
---

# Reputation kept after story missions

The player's reputation is no longer lowered when A1SM03 or A2SM08 completes.

## How

`CFCXMissionManager::MissionCompleted` sets reputation to `0x1C` (and runs `FUN_10744a00`) when the
completed mission is `"A1SM03"`, and to `0x41` when it is `"A2SM08"`, through the setter at
`this+0x180`, clamped to the current level's range. The mod repoints both string comparisons at
`"A3SM16"`, so neither reset fires after those missions. No script or data change is involved.

## Dunia.dll

Both operands are pushed string addresses; a plugin computes the new one as module base plus the
string's offset.

| | Steam | GOG | Bytes |
|---|---|---|---|
| A1SM03 test | `0x107535CA` | `0x1074617A` | Steam `8C 0F -> 7C 10`, GOG `BC 8B -> AC 8C` |
| A2SM08 test | `0x107535F2` | `0x107461A2` | Steam `44 -> 7C`, GOG `74 -> AC` |

Patterns (one match in each build; sites at +9 and +3):
- `51 8B CB E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8D 4C 24 74 E8 ?? ?? ?? ?? 84 C0 74 16 6A 1C 8B CB E8`
- `EB 3F 68 ?? ?? ?? ?? 8D 4C 24 74 E8 ?? ?? ?? ?? 84 C0 74 0B 6A 41 8B CB E8`

## Uncertain

`A3SM16` is a real mission: completing it now takes the old A1SM03 branch and sets reputation to
`0x1C`. Harmless if it is the last mission played, which is inferred and not checked. Retargeting the
comparison at a string no mission has would avoid it.
