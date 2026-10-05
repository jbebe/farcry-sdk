---
title: Higher-resolution dirt road texture
kind: component
bundle: graphics
status: located
systems: [graphics, world]
match:
  - "graphics/terrain/**"
exclude: []
requires: []
verified: diff
---

# Higher-resolution dirt road texture

The main dirt-road surface is shown at twice the resolution.

## How

`graphics/terrain/_textures/roads/officialoffroadgeneric_d.xbt` (512x1024 -> 1024x2048 DXT1) and
its full-resolution top level `officialoffroadgeneric_d_mip0.xbt` (1024x2048 -> 2048x4096) are
replaced whole. Scaled back down the new image matches the old one closely (structural similarity
0.98), so it is an upscale of the same texture rather than a new design.

## Uncertain

- Whether the upscale was sharpened or AI-upscaled is not distinguishable from the diff.
