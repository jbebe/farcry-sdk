---
title: Pickups do not flash
kind: component
bundle: graphics
status: located
systems: [graphics, engine]
match: []
exclude: []
requires: [engine-dunia-dll]
verified: re
---

# Pickups do not flash

Interactable items (weapons, ammo, health, diamonds) no longer pulse with the highlight shader. This
is the "No Flashing Items" engine choice. The "Flashing Items" variants leave the string intact.

## How

`FUN_103BB6C0` (GOG) looks `Mesh_Highlight` up in a named list held at `0x11558C24`
(`push 0x10DC1A90; call 0x10425E00`). `FUN_10425E00` compares each entry's name case-insensitively
and returns its index plus one, or 0 when nothing matches. The mod overwrites the string with zeros,
so the search is for an empty name and returns 0: there is no highlight entry, and items draw
without the pulse.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| technique name | `0x10E49D04` | `0x10DC1A90` | `Mesh_Highlight` -> 14 zero bytes |

The string is unique in both builds. A plugin would blank it, or skip the lookup's caller.

## Uncertain

- What the list at `0x11558C24` holds (render passes or material techniques) and how its caller
  treats index 0 were not read. That 0 means "no highlight" is inferred from the option's name and
  the mod shipping it.
