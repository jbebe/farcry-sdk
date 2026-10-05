---
title: Bullet penetration made all-or-nothing per material
kind: component
bundle: materials
status: located
systems: [weapons]
match:
  - "databases/materials/logicmaterials.xml#Materials[*]@iPiercingResistance"
exclude: []
requires: []
verified: diff
---

# Bullet penetration made all-or-nothing per material

Each surface material's resistance to bullets passing through it is pushed to one of the two
extremes: hard and soft ground, concrete, hard metal, hard wood, deep water, straw and bulletproof glass stop
everything, while flesh, glass, light and sheet metal, plastic, tile, foliage and shallow water
offer none.

## How

`databases/materials/logicmaterials.xml`, `iPiercingResistance` on 30 of the 39 logic materials,
old -> new:

- -> 100: `Base.BulletProof_Glass` 30, `Base.Concrete` 29, `Base.Ground_hard` 30,
  `Base.Ground_soft` 25, `Base.Metal_hard` 29, `Base.Straw` 1, `Base.Water_deep` 29,
  `Base.Wood_hard` 25.
- -> 0: `Base.Cactus` 10, `Base.Carpet` 6, `Base.Electronic_stuff` 6, `Base.Flesh` 6,
  `Base.Flesh_Head` 6, `Base.Fruit` 1, `Base.Glass` 1, `Base.Grass` 1, `Base.Grass_high` 1,
  `Base.Leather` 5, `Base.Metal_burn` 4, `Base.Metal_fence` 1, `Base.Metal_light` 10,
  `Base.Metal_painted` 10, `Base.Metal_res` 10, `Base.Metal_sheet` 7, `Base.Plastic_sheet` 6,
  `Base.Plastic_soft` 5, `Base.Roof_tile` 5, `Base.Tile` 4, `Base.Vegetation` 1, `Base.Water_low` 28.
- Unchanged: `Base.Bush` 1, `Base.Default` 6, `Base.Mud` 100, `Base.NoImpact` 29, `Base.Pebble` 100,
  `Base.Sand` 100, `Base.Stubble` 1, `Base.Wood_light` 10, `Base.Wood_res` 7.

The field belongs to the engine's `CMaterialFx` (`RegisterProperties` at `0x105d3e10` in the Steam
`Dunia.dll`). The file carries the comment `Far Cry 2 - Multi... Editor v1.3.2.2`, the tool it was
saved with; the same header is on `decal.xml`.

## Uncertain

- How the engine compares `iPiercingResistance` with a weapon's penetration is not traced, so what
  0 and 100 do exactly is read from the base game's spread of values. Flesh at 0 could let a bullet
  go on through a body; deep water at 100 could stop bullets at the surface.
- Some choices look odd for a deliberate rebalance (straw 1 -> 100, shallow water 28 -> 0). Whether
  the author meant all of them is unknown.
