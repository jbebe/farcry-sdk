---
title: Sharper sniper scope reticle
kind: component
status: located
systems: [weapons, graphics]
match:
  - "graphics/weapons/special/sniper_dragunov/dragunov_crosshair_d{,_mip0}.xbt"
exclude: []
requires: []
verified: diff
---

# Sharper sniper scope reticle

The reticle texture the scoped rifles share is drawn at twice the resolution.

## How

The mod's `patch.dat` carries both halves of `graphics/weapons/special/sniper_dragunov/dragunov_crosshair_d.xbt`
at double size, still DXT5: the low mips `dragunov_crosshair_d.xbt` 512x256 -> 1024x512 (175,028 ->
699,316 bytes) and the top mip `dragunov_crosshair_d_mip0.xbt` 1024x512 -> 2048x1024 (524,448 ->
2,796,400 bytes). Whole files; take them from the archive.

The texture is shared: the Dragunov's reticle and the AS50's red cross and range marks both sample
it ([presentation and input](../../../docs/docs/engine-internals/presentation-and-input.md)).

## Uncertain

- Whether the art is only upscaled or redrawn is not checked.
