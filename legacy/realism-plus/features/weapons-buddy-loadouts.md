---
title: Buddies carry machine guns and late-game rifles early
kind: component
bundle: gameplay
status: located
systems: [buddies, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[{buddy,buddy_shotgun}]/**"
exclude: []
requires: []
verified: diff
---

# Buddies carry machine guns and late-game rifles early

Buddies no longer start on the G3 or the Ithaca: from the first level they may carry an AK-47, a PKM
or a SPAS-12, and from the middle of the game an M249 or a USAS-12.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/InventoryPacks` (primary weapons;
the Desert Eagle sidearm is unchanged):

| Pack | Base game | Mod |
|---|---|---|
| `buddy` | G3 at levels 0-6, then AK-47 / FAL / PKM, then FAL / M16 / M249 | levels 0-13: AK-47 0.50, PKM 0.35, FAL/M16/M249 0.05; levels 14-27: FAL 0.30, M16 0.30, M249 0.40 |
| `buddy_shotgun` | Ithaca, then SPAS-12 | levels 0-13: SPAS-12 0.75, PKM 0.15, USAS-12/M249 0.05; levels 14-27: USAS-12 0.80, M249 0.20 |

The analysis pairs list entries by position, so these read as 415 field edits, adds and removes.

## Depends on

Nothing. The enemies' packs are `weapons-enemy-loadouts`.
