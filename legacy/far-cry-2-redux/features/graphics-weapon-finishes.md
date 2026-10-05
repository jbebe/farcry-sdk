---
title: AK-47, FAL and G3KA4 finishes
kind: component
bundle: graphics
claims: []
status: located
systems: [weapons, graphics]
match:
  - "graphics/_materials/{jpcormier-m-2007100950589039,pfernandez-m-2007050933476401,sdore2-m-2008052063473318}.xbm"
exclude: []
requires: []
verified: diff
---

# AK-47, FAL and G3KA4 finishes

Three weapons look different: the AK-47's wood is redder, the FAL's plastic has another grain, and
the G3KA4's green furniture becomes grey-tan. Not a line of the readme.

## How

Three weapon materials, replaced whole at the same size (base copies in `worlds.dat`):

- `jpcormier-m-2007100950589039.xbm`, `AK47WOOD`: `DiffuseColorBase` (0.275, 0.118, 0) ->
  (0.494, 0.055, 0).
- `pfernandez-m-2007050933476401.xbm`, `FN_FAL_PLASTIC`: `DiffuseTexture1` and `SpecularTexture1`
  `graphics\_textures\specular\plasticbump_01_s.xbt` -> `plasticbump_02_s.xbt`.
- `sdore2-m-2008052063473318.xbm`, `G3KA4_PLASTIC`: `SpecularPower` 6 -> 5; `DiffuseColor1` and
  `DiffuseColor1Clean` (0.659, 0.918, 0.322) -> (0.68, 0.656, 0.594); `DiffuseColor1Broken`
  (0.18, 0.314, 0) -> (0.453, 0.438, 0.398).

## Uncertain

- Which models read these materials is inferred from their names; presumably the weapon's first-
  and third-person models alike.
- How the colour values combine with the weapon shader's masks (and so the exact on-screen colour)
  is not checked in game.
