---
title: Buddies carry an even mix of rifles
kind: component
bundle: gameplay
claims: []
status: located
systems: [buddies, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[{buddy,buddy_shotgun}]/**"
exclude: []
requires: []
verified: diff
---

# Buddies carry an even mix of rifles

Buddies no longer move up a weapon ladder: each draws one of four guns with equal odds for the
whole game, and no longer carries a machine gun.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/InventoryPacks`, the primary
weapons of the two buddy packs (machete and Desert Eagle unchanged; 136 removes, adds and field
edits, paired by position):

| Pack | Base game (by level 0-27) | Mod (every level) |
|---|---|---|
| `buddy` | G3 0-6, AK-47 3-15, FAL from 6, M16 from 15, PKM 7-23, M249 from 19 | AK-47, G3, FAL, M16, 0.25 each |
| `buddy_shotgun` | Ithaca, SPAS-12 from 5 | Ithaca, SPAS-12, USAS-12, M16, 0.25 each |

## Depends on

Nothing. Enemies are `weapons-enemy-loadouts`. Realism Plus rewrites the same two packs differently
([`weapons-buddy-loadouts`](../../realism-plus/features/weapons-buddy-loadouts.md)).

## Uncertain

- The M16 in the shotgun buddy's list looks deliberate (a long gun for the shotgun buddies) but is
  not explained. Not a line of the readme.
