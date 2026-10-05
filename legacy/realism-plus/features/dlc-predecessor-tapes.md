---
title: Predecessor tapes unlocked
kind: component
bundle: dlc-unlocks
status: located
systems: [missions, engine]
match: []
exclude: []
requires: [engine-dunia-dll]
existing: mods/UFCP — src/fixes/bonus_content.cpp (ApplyPredecessorTapesUnlock), both builds
verified: re
---

# Predecessor tapes unlocked

The seven Intel Bonus predecessor-tape missions, held behind a retired promotion, are available in
every game.

## How

The GOG `Dunia.dll` gates them on a registry value. `FUN_10048900(index)` opens
`HKCU\Software\Ubisoft\Far Cry 2`, reads `PartnerKey%d`, and returns 1 only if it equals 1. The mod
changes its last instruction before the epilogue, `mov al,bl`, to `mov al,1`, so it always returns 1.
UFCP patches the same function at its prologue.

The Steam build gates the tapes on Ubisoft's privileges service instead (`0x102E1D10`), so this patch
has no Steam counterpart. The mod sidesteps that by shipping the GOG binary to everyone. Scubrah's
Patch, which ships the Steam binary, patches the privileges gate instead
([`predecessor-tapes-unlocked`](../../scubrahs-patch/features/predecessor-tapes-unlocked.md)).

The optional `Install DLC Machetes.reg` also sets `PartnerKey1` and `PartnerKey2` to 1, which unlocks
the same content without this patch ([`dlc-machetes-registry`](dlc-machetes-registry.md)).

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| return value | none | `0x10048987` | `8A C3 -> B0 01` |

Pattern (one match in GOG, none in Steam; site at +7): `52 FF 15 ?? ?? ?? ?? 8A C3 5B 81 C4 10 01 00 00 C2 04 00`
