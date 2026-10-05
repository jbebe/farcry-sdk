---
title: M16 sight with a red dot
kind: component
bundle: graphics
claims: []
status: located
systems: [weapons, graphics]
match:
  - "graphics/_materials/sdore2-m-{2008022858577422,2008040342572454,2008040357204961}.xbm"
exclude: []
requires: []
verified: diff
---

# M16 sight with a red dot

The M16's sight shows a red dot instead of a green one, and the tints of the M16 and MGL-140 sight
lenses trade places. Not a line of the readme.

## How

Three weapon materials, replaced whole (base copies in `worlds.dat`):

- `sdore2-m-2008040357204961.xbm`, `M16_GREEN_POINT` (an `Unlit` material drawing
  `m16_greencircle.xbt`): `DiffuseColor1` (0, 2, 0) -> (2, 0, 0).
- `sdore2-m-2008022858577422.xbm` and `sdore2-m-2008040342572454.xbm`, `MGL140_LENS_EXT` and
  `M16_LENS_EXT`, swap contents. `…22858577422` now holds the base M16 lens (named `M16_LENS_EXT`,
  `SpecularPower` 50, `DiffuseColor1` (0.635, 1.435, 0.149)); `…40342572454` holds the base MGL-140
  lens (`SpecularPower` 35, `SpecularColor1` (2, 1.365, 0)) with `DiffuseColor1` (2, 0.941, 0) ->
  (2, 0.5, 0).

The three files are byte for byte the ones of `graphics-m16-red-dot` in `legacy/realism-plus`.

## Uncertain

- Which model reads which lens file is inferred from the materials' names, not from the models; if
  the base names were already swapped relative to the models, the swap gives each lens its intended
  tint.
