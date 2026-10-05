---
title: Darker green GPS
kind: component
bundle: graphics
claims: []
status: located
systems: [ui, graphics]
match:
  - "graphics/_materials/sdore2-m-2008071452532338.xbm"
exclude: []
requires: []
verified: diff
---

# Darker green GPS

The GPS gadget's casing is a dark green instead of a bright yellow-green. Not a line of the readme.

## How

`graphics/_materials/sdore2-m-2008071452532338.xbm`, `GPS_BODY`, replaced whole at the same size
(base copy in `worlds.dat`): `DiffuseColor1` (0.369, 0.627, 0) -> (0, 0.344, 0.086). Textures and
every other value are unchanged.

## Uncertain

- The colour is applied through the material's mask (`gps_m.xbt`), so only the masked part of the
  casing changes; which part that is is not checked.
