---
title: DLC vehicle paint materials
kind: component
bundle: gameplay
status: located
systems: [vehicles, graphics]
match:
  - "graphics/_materials/sdore2-m-{2008081267040340,2008081958233898,2008100649183233}.xbm"
exclude: []
requires: []
verified: diff
---

# DLC vehicle paint materials

Three materials of the Fortune's Pack (DLC) vehicles are replaced, part of the mod's "different
colours for DLC vehicles".

## How

The base copies are in `downloadcontent/dlc1`; the mod ships replacements in the patch archive at
`graphics/_materials/`, whole:

- `sdore2-m-2008081267040340.xbm`, `UNIMOG_PAINT01` -> `UNIMOG_PAINT01_MULTI`;
- `sdore2-m-2008081958233898.xbm`, `UNIMOG_PAINT02` -> `UNIMOG_PAINT02_MULTI`;
- `sdore2-m-2008100649183233.xbm`, `QUAD_BODY_PLASTIC` -> `QUAD_BODY_PLASTIC_MULTI`.

Each keeps the `Vehicle` shader, the dirt mask and its mask texture (`unimog_ext_m_multi.xbt`,
`quad_dlc_m.xbt`). The two Unimog paints also swap their `SpecularTexture1`
`graphics\_textures\specular\infra_bridges_metal_s.xbt` for
`graphics\vehicles\_textures\diffusevariation_d.xbt`. Most of the parameter block differs
byte-wise (about 1,330 of 1,716-1,732 bytes), so colours and other values change too; they are not
decoded here.

## Depends on

Nothing in the mod: the DLC Unimog and Quad models already read these material files, so swapping
the files is the whole colour change. The mod's edits to the DLC vehicle archetypes
(`vehicles-faster-land`, `vehicles-collision-damage`) do not touch paint, and the
`selVehicleColor` value on `noise-world-dlc-placeholder-copies` is a dead copy.

## Uncertain

- By the `_MULTI` names these look like the multiplayer variants of the same materials, which tint
  through a colour-variation texture; that reading is not verified.
- That the patch archive's copies win over the DLC archive's at the same path is assumed from how
  the patch archive overrides other files; not checked in game.
- The parameter values were not decoded, so the resulting colours are not described.
