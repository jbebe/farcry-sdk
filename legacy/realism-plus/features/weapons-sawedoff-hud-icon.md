---
title: Sawed-off shotgun on its own HUD icon slot
kind: component
bundle: weapons
status: located
systems: [weapons, ui, engine]
match: []
exclude: []
requires: [engine-dunia-dll, ui-hud-icon-dlc06]
verified: re
---

# Sawed-off shotgun on its own HUD icon slot

The HUD shows the sawed-off shotgun with the icon in the spare `dlc6` slot, which the mod redraws,
instead of the sawed-off's own entry.

## How

`CFCXWeaponIconMap` (named in the server build; GOG `FUN_1085D840`) builds a map from weapon-name
hash to HUD icon index. Its string table ends `dlc6`, `dlc5`, `dlc4`, `dlc3`, `dlc2`, `dlc1`,
`silencedshotgun`, `crossbow`, `sawedoffshotgun`, where `dlc5` is index `0x24` and `dlc6` is `0x25`.
The mod writes `sawedoffshotgun` over the `dlc6` and `dlc5` slots and zeroes the original
`sawedoffshotgun`. Now:

- `sawedoffshotgun` maps to index `0x25`, `dlc6`'s icon;
- the old sawed-off entry is registered under an empty name;
- the `dlc5` slot, 8 bytes into the new string, reads `shotgun`, which no weapon is called.

Index `0x25` is presumably the slot drawn with `hud_icon_dlc_06`, going by its name. That texture,
`ui/textures/hud/icons_weapons/hud_icon_dlc_06.xbt`, is among the mod's replaced textures, so the
sawed-off would get a new icon without disturbing the one it shared.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| `dlc6` slot | `0x10EB274C` | `0x10E2A2BC` | `dlc6\0\0\0\0dlc5\0\0\0` -> `sawedoffshotgun` |
| old name | `0x10EB2798` | `0x10E2A308` | `sawedoffshotgun` -> 15 zero bytes |

## Depends on

- The redrawn `hud_icon_dlc_06.xbt`, on the UI pages. Without it the sawed-off shows whatever the
  stock `dlc_06` icon is.

## Uncertain

- What index the sawed-off had before, and so what it showed, was not read.
