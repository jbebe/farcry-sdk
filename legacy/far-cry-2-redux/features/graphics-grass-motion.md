---
title: Grass sways more in the wind
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics, environment]
match:
  - "engine/settings/defaultrenderconfig.xml#@Grass*"
exclude: []
requires: []
verified: diff
---

# Grass sways more in the wind

Grass bends further and moves a little faster in the wind. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile` attributes:

| Key | Base | Mod |
|---|---|---|
| `GrassWindFactor` | 0.1714 | 0.3 |
| `GrassWindMax` | 2 | 4 |
| `GrassMotionFrenquency` | 7 | 8 |
| `GrassMotionFrenquency2` | 3 | 4 |
| `GrassMotionOnSlope` | 1.5 | 1.6 |

`GrassWindMin`, `GrassWindFactorLerpTime` and the perturbation values are unchanged.

## Uncertain

- The effect is read from the key names; how the factor and maximum combine with the weather's wind
  is not traced.
