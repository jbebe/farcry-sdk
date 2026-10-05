---
title: M16 sight with a red dot
kind: component
bundle: weapons
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
lenses trade places.

## How

Three weapon materials are replaced whole:

- `sdore2-m-2008040357204961.xbm`, material `M16_GREEN_POINT` (an `Unlit` material drawing
  `graphics\weapons\primary\m16\m16_greencircle.xbt`): `DiffuseColor1` (0, 2, 0) -> (2, 0, 0), so the
  dot is drawn red. The texture and the name are unchanged.
- `sdore2-m-2008022858577422.xbm` and `sdore2-m-2008040342572454.xbm`, the materials named
  `MGL140_LENS_EXT` and `M16_LENS_EXT`: their contents are swapped. Both draw
  `graphics\weapons\primary\mgl140\mgl140_lens_d.xbt` with the lens cube map; they differ in
  `DiffuseColor1`, (2.0, 0.94, 0) for the MGL-140 lens and (0.64, 1.44, 0.15) for the M16's. In the
  mod, `…2008022858577422` holds the old M16 material byte for byte, and `…2008040342572454` holds
  the old MGL-140 material with its colour's second component 0.94 -> 0.5, giving (2.0, 0.5, 0).

Models name materials by file, so if the M16 model reads `…2008040342572454` (the file named
`M16_LENS_EXT` in the base game), its lens now takes the orange-red tint and the MGL-140's the green
one.

## Uncertain

- Which model reads which lens file is inferred from the materials' internal names, not from the
  models. If the base names were already swapped relative to the models, the swap is a fix that gives
  each lens its intended tint.
- Not in the mod's notes; the red dot was found in the diff.
