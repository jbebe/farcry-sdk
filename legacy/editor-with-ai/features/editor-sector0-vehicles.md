---
title: Sixteen vehicles parked in the template world's sector 0
kind: component
bundle: editor-content
status: located
systems: [world, vehicles]
match:
  - "_hash/99499ecc.fcb"
  - "worlds/tmpla/generated/tmpla.sectorsdep.fcb"
exclude: []
requires: []
verified: diff
---

# Sixteen vehicles parked in the template world's sector 0

The editor's template world (`tmpla`) gets main sector data for sector 0. That data places one of
each multiplayer vehicle, all on one spot far from the playable area.

## How

- `_hash/99499ecc.fcb` is `levels\tmpla\generated\worldsectors\worldsector0.data.fcb` (name
  recovered by CRC32 of the path). It is a `WorldSector`: `Id` 0, `X` 4, `Y` 4, one
  `MissionLayer` `main` holding 16 entities. All 16 sit at (1024, 1024, 0) with different
  rotations. Their archetypes (`tplCreatureType`) are `vehicle.Sea.FishingBoat`,
  `vehicle.Land.BigTruck`, `vehicle.Land.Rover.Multi`, `vehicle.Land.Buggy.Multi`,
  `vehicle.Land.JeepWrangler.Multi` (twice), `vehicle.Land.JeepLiberty.Multi`,
  `vehicle.Land.Datsun.Multi` (twice), `vehicle.Sea.SwampBoat.Multi_M249_Mounted.Neutral`, and the
  Fortunes Pack's `vehicle.Land.DLC_Vehicle1_DLC1` (plain and `.Multi`, three in all) and
  `vehicle.Land.DLC_Vehicle2_DLC1.Multi_M2_Mounted`, `_M249_Mounted` and `_MK19_Mounted`. Some
  carry jokey names (`Sea.FatBoat`, `Land.BigFatTruck`, `Land.EvilRover`). Their entity ids mix
  small hand-typed numbers with real editor ids, so they were copied out of an editor map.
- `worlds/tmpla/generated/tmpla.sectorsdep.fcb` (a whole-file change, 2,341 to 2,347 bytes):
  sector 0 gains `HasMainSectorData` `True`, which is what makes the engine read the new file.
  Every sector's `DetailMask` also changes from 268,435,455 (`0x0FFFFFFF`) to 304,676,863
  (`0x1228FFFF`).

All 16 archetypes are in the template world's own library.

## Uncertain

- The purpose is inferred. Parking one of each vehicle off the map would make every map built on
  the template load those vehicles' meshes, physics and sounds. That would be a way to make placed
  vehicles, the DLC ones above all, work in the editor. They could also be leftovers of a test map.
- Whether the vehicles show up in a user's map, and whether an `.fc2map` saved with them keeps
  them, has not been checked.
- What `DetailMask` selects is unknown. The new value has bits 0 to 15, 19, 21, 25 and 28 set,
  where vanilla sets bits 0 to 27.
