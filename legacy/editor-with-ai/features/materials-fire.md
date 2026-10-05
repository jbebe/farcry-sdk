---
title: Fire passes through every material and burning surfaces make no smoke
kind: component
bundle: materials
status: located
systems: [world, graphics]
match:
  - "databases/materials/logicmaterials.xml#Materials[*]@{bFireGoesThrough,fFireStickyBurnFactor,selFireStickyKindOfSmoke}"
exclude: []
requires: []
verified: diff
---

# Fire passes through every material and burning surfaces make no smoke

Every logic material gets the same fire settings: fire goes through it, sticky fire burns on it at
full strength, and burning on it gives off no smoke.

## How

`databases/materials/logicmaterials.xml`, three fields set to one value on every material that did
not have it already (95 values):

- `bFireGoesThrough` 0 -> 1 on 38 materials; only `Base.Metal_fence` had 1.
- `fFireStickyBurnFactor` -> 1 on 26 materials that had less, from 0.01 (`Base.BulletProof_Glass`,
  `Base.Metal_fence`, `Base.Metal_hard`, `Base.Metal_light`, `Base.Metal_res`, `Base.Metal_sheet`,
  `Base.Water_deep`) through 0.1-0.7 on the rest (concrete, glass, ground, mud, sand, pebble, tile,
  roof tile, plastics, painted and burnt metal, shallow water, fruit, cactus, carpet, leather,
  electronics). Flesh, foliage, straw and wood already had 1.
- `selFireStickyKindOfSmoke` -> 2 on 31 materials: 14 from 1 (grass, bush, vegetation, straw,
  stubble, the three woods, carpet, leather, plastics, painted metal) and 17 from 0 (ground, mud,
  sand, flesh, tile, water, most metals and the rest). The 8 that already had 2 are glass,
  bulletproof glass, concrete, pebble, fruit and three metals.

`selFireStickyKindOfSmoke` is an enum registered by `CMaterialFx::RegisterProperties` (`0x105d3e10`
in the Steam `Dunia.dll`) in the order `LightSmoke`, `HeavySmoke`, `NoSmoke`, so 2 is `NoSmoke`:
grass and bushes that gave heavy smoke when burning, and the rest that gave light smoke, now give
none.

## Uncertain

- The effect of `bFireGoesThrough` (fire spreading through walls and ground, or a raycast for fire
  that ignores the surface) and of `fFireStickyBurnFactor` is read from the names; neither is traced.
  Together they may make fire spread much further, including through concrete.
- That 0/1/2 map to the enum in registration order is the usual reflection layout, not checked
  against the loader. The base game's spread fits it: glass, concrete and hard metal had 2,
  foliage and wood 1.
- The uniform values look like a "set all" edit in the tool the file was saved with
  (`Far Cry 2 - Multi... Editor v1.3.2.2`), not a per-material tuning.
