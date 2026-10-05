---
title: Hand-painted road signs and sharper billboards
kind: component
bundle: graphics
status: located
systems: [graphics, world]
match:
  - "graphics/objects/_signskit/**"
  - "graphics/_materials/jfcarrier2-m-1504200826749329.xbm"
exclude: []
requires: []
verified: diff
---

# Hand-painted road signs and sharper billboards

The place-name and shop signs along the roads are relettered in a rough hand-painted style at twice
the resolution, and the billboards, faction posters and graffiti are redone at two to five times
the resolution.

## How

71 textures in `graphics/objects/_signskit/` are replaced whole (a `_mip0` file is the
full-resolution top level of the texture of the same name):

- **Redrawn lettering, 45 files**: the 43 `*_m.xbt` masks of the place and shop signs (`pala_m`,
  `safehouse02_m`, `weaponsshopsigns_m`, `postoffice_m`, `sehlakalase_m` and so on; not
  `factions/posters/postermask_m`) plus `taemoco.xbt` and `mertensegolopipelinecomp.xbt`, 512x128 ->
  1024x256 DXT1. The channel that carries the lettering is redrawn: block capitals become a
  brush-lettered style (`Pala` -> a painted `PALA`), and the safehouse mask gains a house-and-cross
  symbol on each side, the symbol `ui-map-icons` uses for safehouses. Scaled down, these match the
  base images poorly (structural similarity 0.45-0.89), which is what sets them apart from plain
  upscales.
- **Upscaled, 26 files**: `masters/*_billboard_d.xbt` (6), `factions/posters/*` (11, with their
  `_mip0` tops and `postermask_m`), `bowaseko_d`, `mertenssegolosign_d` and `ceasefire/ceasefire`
  (each with its `_mip0`), `safehousesign_d`, `weaponsdepotsign_d` and `weaponsshopsign_d`: the same
  pictures at two to five times the resolution (similarity 0.89-0.99).
- `graphics/_materials/jfcarrier2-m-1504200826749329.xbm`, the `WEAPONSSHOP` sign material that
  reads `weaponsshopsigns_m.xbt`, is re-saved without its `RainOccluder` and `Maskable` parameters
  (1252 -> 1221 bytes); its textures are unchanged.

Every file is a whole unit and is taken as shipped, by its path in the mod's archive.

## Depends on

The sign colour tinting that guides to missions is a separate entity value
(`nav-plain-road-signs`); these textures are the lettering it tints.

## Uncertain

- Why the shop-sign material loses `RainOccluder` and `Maskable` is not known; it may be a side
  effect of the tool that re-saved it. Its effect (no rain occlusion under the sign) is inferred
  from the names.
