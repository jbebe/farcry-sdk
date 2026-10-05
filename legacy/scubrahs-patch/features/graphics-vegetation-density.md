---
title: Vegetation zone density and minimum scale
kind: component
bundle: improved-graphics
claims: []
status: located
systems: [graphics, world]
match:
  - "worlds/*/generated/world*.managers.fcb/zonelogicmanager.*#Zones/Zone[*]/DensityScale"
  - "worlds/*/generated/world*.managers.fcb/collectionmanager.*#SerializationData/VegeResInfos/ScaleMin/**"
exclude: []
requires: []
verified: diff
---

# Vegetation zone density and minimum scale

Two edits to the vegetation managers whose effect in game is not established: every logic zone's
density scale is brought to 2, and the smallest scale a scattered plant may take is raised. Not a line
of the feature list.

## How

Both worlds' `world*.managers.fcb`:

- `ZoneLogicManager`, every `Zone`'s `DensityScale` brought to 2, the value `Logic.DenseJungle` and
  `Logic.HOD` already had: 1 -> 2 for `Logic.Savannah`, `Logic.NoFx`, `Logic.LightJungle`,
  `Logic.Desert` and `Logic.Urban`, `Logic.Woodland` 1.15 -> 2, `Logic.MediumJungle` 1.5 -> 2 (seven
  changes per world).
- `CollectionManager`, `VegeResInfos/ScaleMin`: 330 of the 1,716 entries are raised in each world,
  most often 0.5 -> 1.4 (124), 0.7 -> 1 (44), 0.8 -> 1 (39), 0.9 -> 1 (33), 0.9 -> 1.2 (30). The
  array keeps its length; the change list shows it as matched removes and adds. `ScaleMax`,
  `Density`, `Radius` and `Bias` are untouched.

## Uncertain

- Campaign vegetation is baked into the `landmark*` sector files with its own scale, so
  `VegeResInfos` (the editor's scatter parameters) may do nothing at run time.
- What `DensityScale` scales is not traced. The render config's per-zone `DensityLodScales`
  (`lod-distances`) use the same zone names.
