---
title: AI sees through every surface
kind: component
bundle: materials
status: located
systems: [ai, world]
match:
  - "databases/materials/logicmaterials.xml#Materials[*]@fTransparency"
exclude: []
requires: []
verified: re
---

# AI sees through every surface

Every logic material's `fTransparency` is set to 1. AI line of sight multiplies this value along the
ray, so nothing blocks it any more: walls, rocks, metal, ground. Soldiers can see the player, and
each other, through anything. This holds in the campaign as well as in editor maps.

## How

`databases/materials/logicmaterials.xml`: `fTransparency` → 1 on 37 of the 39 materials.

- 0 → 1 on 32 opaque materials: concrete, ground, metal, wood, tile, flesh, grass and the rest.
- `Base.Bush` 0.5, `Base.Vegetation` 0.5, `Base.BulletProof_Glass` 0.8, `Base.Glass` 0.8 and
  `Base.Metal_fence` 0.9 → 1.
- `Base.Water_deep` and `Base.Water_low` were already 1.

`CSensorySystemHelpers::ValidateLineOfSightWithTransparency` (traced in the symbolized server
build) takes the result of an AI agent's delayed sight ray. It skips the agent and target themselves,
looks up the material of every hit, and multiplies a running visibility, starting at 1, by that
material's transparency. Its debug overlay prints this as "Transparency: %0.2f, Running
transparency: %0.2f". Sight is blocked only once the product reaches 0, and the result, at most 1,
is the visibility the senses use. Vanilla values make a wall block outright (0) and a bush halve
visibility (0.5). With every material at 1, the product never drops.

`CTaskShoot::CheckTransparencyOcclusion` reads the same field when a soldier's shot ray hits
something. An opaque hit (0) can set the second of its two out-flags. A non-zero transparency never
sets it, so with this mod no surface takes that branch. What the two flags make the shoot task do
was not traced.

The field registers in `CMaterialFx::RegisterProperties` (Steam `Dunia.dll` `0x105D3E10`), between
`bFireGoesThrough` and `fFireStickyBurnFactor`.

## Uncertain

- How a soldier who sees through a wall behaves (shooting into it, pathing, staying alert) has not
  been seen in game.
- The uniform value, like the fire fields', looks like a "set all" edit in the
  `Far Cry 2 - Multi... Editor v1.3.2.2` tool the file was saved with, rather than a deliberate AI
  change.
