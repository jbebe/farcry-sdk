---
title: Redrawn sniper reticle and a round M16 dot
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, graphics]
match:
  - "graphics/weapons/special/sniper_dragunov/dragunov_crosshair_d.xbt"
  - "graphics/weapons/primary/m16/m16_greencircle.xbt"
exclude: []
requires: []
verified: diff
---

# Redrawn sniper reticle and a round M16 dot

The sniper scope reticle carries new range markings, and the M16's sight dot is a smooth disc
instead of a blocky diamond.

## How

Two textures, replaced whole (take them from the archive's `patch.dat`):

- `graphics/weapons/special/sniper_dragunov/dragunov_crosshair_d.xbt`, the reticle sheet the
  Dragunov's and AS50's scopes sample: same size and format (512x256 DXT5, 175,028 bytes), redrawn:
  the rangefinder and bullet-drop marks get other spacing and new numbers (`10` to `2` along the
  drop curve, a `1,7` mark), the red `2`-`10` scale turns black. Its `_mip0` high-resolution half is
  not replaced.
- `graphics/weapons/primary/m16/m16_greencircle.xbt`, the dot drawn by the M16's `M16_GREEN_POINT`
  material: 8x8 -> 32x32 DXT3 (272 -> 1,184 bytes), a filled round dot.

The new dot is saved without mipmaps (the base file has four levels).

## Depends on

- `graphics-m16-red-dot` tints the M16's dot material red; the round shape comes from this page.
- Realism Plus doubles the reticle's resolution instead of redrawing it
  ([`weapons-scope-reticle`](../../realism-plus/features/weapons-scope-reticle.md)).

## Uncertain

- What the new marks measure is read off the picture only. With no `_mip0` replacement, high
  texture settings may still show the base reticle (inference). Not a line of the readme.
