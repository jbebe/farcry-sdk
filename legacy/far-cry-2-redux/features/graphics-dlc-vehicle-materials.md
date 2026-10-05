---
title: DLC vehicle paint takes vehicle colours
kind: component
bundle: graphics
claims: []
status: located
systems: [vehicles, graphics]
match:
  - "graphics/_materials/sdore2-m-{2008081267040340,2008081958233898,2008100649183233}.xbm"
exclude: []
requires: []
verified: diff
---

# DLC vehicle paint takes vehicle colours

Three paint materials of the Fortune's Pack vehicles (the DLC Unimog and quad) are swapped for
versions that take a per-vehicle colour, as multiplayer vehicles do. Not a line of the readme.

## How

The base copies are in `downloadcontent/dlc1`; the mod ships replacements at
`graphics/_materials/` in the patch archive, whole, same sizes. Decoded:

- `sdore2-m-2008081267040340.xbm`, `UNIMOG_PAINT01` -> `UNIMOG_PAINT01_MULTI`, and
  `sdore2-m-2008081958233898.xbm`, `UNIMOG_PAINT02` -> `UNIMOG_PAINT02_MULTI`: `SpecularTexture1`
  `infra_bridges_metal_s.xbt` -> `graphics\vehicles\_textures\diffusevariation_d.xbt`;
  `DiffuseColor1` grey 0.941 -> brown (0.275, 0.125, 0.031); `DiffuseColorBase` dark brown ->
  white 1.02; `SpecularColor1` 0.549 -> (0.157, 0.149, 0.133); `ColorOverride` 0 -> 1; and tiling
  changes (`SpecularTiling1`, on the second also `NormalTiling1`).
- `sdore2-m-2008100649183233.xbm`, `QUAD_BODY_PLASTIC` -> `QUAD_BODY_PLASTIC_MULTI`: blue tints
  adjusted (`DiffuseColor1` (0.086, 0.306, 0.706) -> (0, 0.204, 0.784) and the base and specular
  colours), `VertexColorEnabled` 0 -> 1, `ColorOverride` 0 -> 1.

The three files are byte for byte the ones of `graphics-dlc-vehicle-materials` in
`legacy/realism-plus`, decoded here.

## Uncertain

- That `ColorOverride` 1 lets the vehicle's own colour tint the paint (as on the `_MULTI`
  multiplayer vehicles) is read from the names; no vehicle archetype in this mod sets a colour, so
  which colour shows is not known.
- That the patch archive's copies win over the DLC archive's at the same path is assumed, not
  checked in game.
