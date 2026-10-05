---
title: Four-times-resolution paper map
kind: component
bundle: graphics
status: located
systems: [ui, graphics]
match:
  - "worlds/*/mapcompass{,_mip0}.xbt"
exclude: []
requires: []
verified: diff
---

# Four-times-resolution paper map

The map the player holds is drawn from a texture four times as large in each direction, so terrain,
roads and rivers stay crisp when the map is raised.

## How

`worlds/world1/mapcompass.xbt` and `worlds/world2/mapcompass.xbt` (512x512 -> 2048x2048 DXT1), with
their full-resolution top levels `mapcompass_mip0.xbt` (1024x1024 -> 4096x4096), are replaced whole.
The content is the base map, upscaled: the same roads, rivers, relief, faction zones and town
highlights (structural similarity 0.97 against the base after scaling down). Nothing is added or
removed, so the map's information is the same in either navigation variant.

## Uncertain

- The load cost of a 4096x4096 map texture on low-memory setups is not measured.
