---
title: Morning fog
kind: component
bundle: visuals
claims:
  - "Added foggy weather during the early morning hours"
status: located
systems: [environment, graphics]
match:
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[+{20,21}]"
  - "domino/system/overrideenvironmentfog.lua@L*"
exclude: []
requires: [graphics-environment-manager]
verified: diff
---

# Morning fog

From 05:00 until 08:00 the world is wrapped in fog, which lifts over 30 seconds.

## How

- **The preset.** Both worlds' `world*.managers.fcb`, `DataBaseItemManager`, gain
  `Default.Morning.Fog` (`[+20]` in `world1`, `[+21]` in `world2`), GUID
  `{AF6E0FD6-1C9E-4EA6-AAF9-8989EE12B111}`. It is a copy of `Default.Storm.Fog` with its
  `curveAmount` halved, 1.0 -> 0.5.
- **The switch.** `graphics-environment-manager` sets it as the fog override from 05:00 to 07:59 and
  removes it from 08:00, keeping it while a world's ceasefire mission runs.
- **Not over other fog.** `domino/system/overrideenvironmentfog.lua`, the base game's fog override
  box, now records the fog it sets in `MASTER_GameGlobals.FogOverride` (base line 38) and clears it
  on removal (base line 48). The manager skips the morning fog while that names a sandstorm or
  defoliant fog.

## Depends on

- `graphics-environment-manager`.
- The `FogOverride` and `MorningEnvironmentEnabled` globals in
  `domino/user/master_gameglobals.globals.lua`, claimed elsewhere.
