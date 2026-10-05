---
title: Separate safe-house spawn spots for the rescue buddy
kind: component
status: located
systems: [buddies, world]
match:
  - "domino/user/savepoints/savepoints.savepoints.lua@L{2034,2040,2070,2130,2160,2166,2292}"
  - "worlds/*/generated/world*.mapsdata.fcb/buddies.spawnpointbuddy_*.xml"
  - "worlds/*/generated/world*.mapsdata.fcb/_layout.xml#layer[main]*/entity[buddies.spawnpointbuddy*]"
exclude: []
requires: []
verified: diff
---

# Separate safe-house spawn spots for the rescue buddy

In seven safe houses the buddy waiting there no longer appears on the exact spot another buddy uses,
so two buddies do not stand inside each other. Not on the published list.

## How

- `domino/user/savepoints/savepoints.savepoints.lua`: the per-safe-house boxes carry an `SH_Spawn`
  entity, where the safe house's buddy spawns. Seven of them now point at new spawn points
  (`@L2034` box `38`, `@L2040` `41`, `@L2070` `48`, `@L2130` `61`, `@L2160` `72`, `@L2166` `75`,
  `@L2292` `99`).
- The new points are `Domino.Buddies.SpawnPointBuddy` entities about 2 m from the old ones:
  `Buddies.SpawnPointBuddy_0_2`, `_2_2`, `_3_2`, `_5_2` and `_A1LM05_Subv_2` in `world1.mapsdata.fcb`,
  `_A2LM10_Subv_2` and `_PipeDreams_2` in `world2.mapsdata.fcb`. Several of the spots they replace are
  the library missions' subversion buddy spawns (`A1LM05`, `A2LM10`, `PipeDreams`).
- `_layout.xml`: the `main` layers of the level cells that receive them (`world1` `[main][+1]`,
  `[+2]`, `[+3]`, `world2` `[main][+1]`) list the new entities.

## Depends on

- Two of the points (`_A1LM05_Subv_2` in world 1, `_PipeDreams_2` in world 2) are listed in their
  cell's `[main][+0]` layout entry, which `economy-briefcase-tracker` claims because it also lists that
  page's 224 briefcase icons; without it those two entities have no layer.

## Uncertain

- The `[main][+N]` layout entries list every entity of the cell's `main` layer; only the spawn points
  are new in them (`world2` `[main][+1]` also holds a changed `ConvoyMission` controller).
