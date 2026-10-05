---
title: Darker gamma and deeper sky occlusion
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#@{GammaRamp,AmbientSkyOcclusion*}"
exclude: []
requires: []
verified: diff
---

# Darker gamma and deeper sky occlusion

A new profile starts slightly darker, and places sheltered from the sky (under trees, roofs, beside
walls and moving objects) lose more of their ambient light. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile` attributes:

- `GammaRamp` 1.0 -> 0.9, the default of the brightness setting;
- `AmbientSkyOcclusionMinVisibility` 0.45 -> 0.35, the least sky light a fully occluded spot keeps;
- `AmbientSkyOcclusionDynamicMinVisibility` 0.3 -> 0.1, the same for occlusion by dynamic objects.

The tree opacity values beside them are unchanged.

## Uncertain

- The meaning of the two occlusion floors is read from their names. The brightness slider in the
  options still overrides `GammaRamp` once moved.
