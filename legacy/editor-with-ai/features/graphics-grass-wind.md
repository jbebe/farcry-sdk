---
title: Calmer grass in the wind
kind: component
bundle: graphics
status: located
systems: [graphics, environment]
match:
  - "engine/settings/defaultrenderconfig.xml#@GrassWind*"
exclude: []
requires: []
verified: diff
---

# Calmer grass in the wind

Grass sways a quarter as much: the wind's push on grass is clamped to a much lower range.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile`:

- `GrassWindMin` 0.4 -> 0.1
- `GrassWindMax` 2 -> 0.5

`GrassWindFactor` (0.1714) and `GrassWindFactorLerpTime` (1) are unchanged.

## Uncertain

- The meaning is read from the names: a minimum and a maximum on the grass wind strength, both cut
  to a quarter. How the engine combines them with `GrassWindFactor` and the wind presets is not
  traced, so whether calm days look calmer too (the minimum) is an inference.
