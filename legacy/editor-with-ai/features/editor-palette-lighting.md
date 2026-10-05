---
title: More lamps in the editor palette, wall lights as archetypes
kind: component
bundle: editor-content
status: located
systems: [world, graphics, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[7]/**"
exclude: []
requires: []
verified: diff
---

# More lamps in the editor palette, wall lights as archetypes

The palette's Lighting folder grows from 16 to 21 entries. The three wall lights stop costing 400
budget points each.

## How

`ingameeditor/object_inventory.xml`, top-level `Lighting` (`Directory[7]`, `PcOnly`), 23 changes:

- The vanilla "Wall Light 01", "Wall Light 02" and "Wall Red Light" entries were prefabs
  (`SourceType` 1, `Lighting_LampWall0N`, `ObjectCost` 400, `IsBreakable`). They become the
  archetypes `OA_Lights.Lights.LampWall01_BK.Multi`, `LampWall02.Multi` and `LampWall03_BK.Multi`
  (`SourceType` 0), with no `ObjectCost`, no `IsBreakable` and `Display` "1". The street light
  `Lighting.LightsStreetColonial02` that sat in that slot is re-added further down.
- Added: the `Lighting_BarrelFire_BK` prefab (a burning barrel),
  `OA_Lights.Lights.LanternExplotator01_Hang.Multi`, `LampDesk01` and `LampTable01.Multi`.

All four archetypes are in the template world's vanilla library and the prefab is in its
`tmpla.managers.fcb`.

## Uncertain

- An entry with no `ObjectCost` probably costs nothing against the map budget. That is not checked.
- The archetype wall lights may light the scene differently from the vanilla prefabs, which bundle a
  lamp mesh with a light. Not compared.
