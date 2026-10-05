---
title: Predecessor tapes unlocked
kind: component
bundle: gameplay
claims:
  - "Predecessor tape missions are now unlocked by default (thanks FoxAhead)"
status: located
systems: [missions, engine]
match:
  - "install/bin/dunia.dll@0x2e1d15"
exclude: []
requires: []
existing: mods/UFCP — src/fixes/bonus_content.cpp (ApplyPredecessorTapesUnlock), both builds
verified: re
---

# Predecessor tapes unlocked

The seven bonus predecessor-tape missions, normally gated behind a retired Ubisoft promotion, are
available in every game.

## How

One `Dunia.dll` patch. The bonus-content check (`FUN_102e1d10`, called from `FUN_102d7510`) asks
Uplay's privileges service and returns 0 when it has no answer. Its `test ecx,ecx; jz` (return 0)
becomes a `jmp` to the `mov eax,1; ret 4` tail, so it always answers "unlocked". UFCP already does the
same on both builds.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| gate | `0x102E1D15` | none | `74 16 -> EB 0E` |

Pattern (Steam only; site at +5): `8B 49 0C 85 C9 74 16 8B 44 24 04 50 E8 ?? ?? ?? ?? 84 C0 74 08 B8 01 00 00 00 C2 04 00`

GOG gates the tapes on a registry value (`PartnerKey%d`, read at `0x10048900`) instead, so the mod's
patch has no GOG counterpart; UFCP patches that check with a second pattern.
