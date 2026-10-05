---
title: Weapon wear masks and finishes
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, graphics]
match:
  - "graphics/weapons/primary/fn_fal/fn_fal_m_state2{,_mip0}.xbt"
  - "graphics/weapons/primary/m16/m16_state01_m_mip0.xbt"
  - "graphics/weapons/primary/m16/m16_state03_m{,_mip0}.xbt"
  - "graphics/weapons/secondary/makarov/makarovstate1_m{,_mip0}.xbt"
  - "graphics/_textures/diffuse/fabric/camo_02_d.xbt"
  - "graphics/_textures/specular/genericwood_02_s.xbt"
exclude: []
requires: []
verified: diff
---

# Weapon wear masks and finishes

The wear masks of the FAL, M16 and Makarov are repainted, the M16's camouflage plastic becomes plain
dark, and the wood grain of the Ithaca and other wooden parts gets a finer specular pattern.

## How

Whole textures, taken from the archive's `patch.dat`:

- **Wear masks** (`_m`, the masks a weapon's material blends between clean, worn and broken):
  `fn_fal_m_state2` (1024x1024, subtle edits), `m16_state03_m` (the worn-state mask, visibly less
  wear) and `m16_state01_m_mip0` (near identical), `makarovstate1_m` (much less wear marked). Where
  the base game splits a texture into a low-resolution file and a `_mip0` half, the mod puts the
  full 1024x1024 (or 512x512) image in both: `fn_fal_m_state2.xbt` 174,992 -> 524,448 bytes,
  `m16_state03_m.xbt` 174,984 -> 524,448, `makarovstate1_m_mip0.xbt` 131,232 -> 174,936.
- **`graphics/_textures/diffuse/fabric/camo_02_d.xbt`**, read by `M16_CAMOE_PLASTIC`: a 256x256
  desert camouflage -> a flat dark grey 512x512.
- **`graphics/_textures/specular/genericwood_02_s.xbt`**, read by `ITHACA37WOOD_BLEND` and some
  generic wood materials (a dead-tree canopy, colonial interiors): a coarse grain -> a finer,
  straighter 512x512 grain.

## Depends on

Nothing. The weapon materials the graphics pages change (`graphics-weapon-finishes`,
`graphics-m16-red-dot`) are separate; together they are the mod's weapon look.

## Uncertain

- Which state each mask number is (`state1`/`state2`/`state03`) and how much the repaint shows in
  game are not checked; `genericwood_02_s` also changes a few world objects. Not a line of the
  readme.
