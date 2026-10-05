---
title: Monocular zoom and look speed
kind: component
bundle: gameplay
claims:
  - "Decreased ADS sensitivity on the monocular"
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/monocular.xml"
exclude: []
requires: []
verified: diff
---

# Monocular zoom and look speed

Looking through the monocular zooms in half as far and turns at a different rate.

## How

`generated/entitylibrarypatchoverride.fcb` gains a copy of `gadgets.Equipped.Monocular`, which the
base game keeps only in the world libraries. Against them its `CGadget/UseStrategy/Zoom` changes:

- `fFOV` `0.2` -> `0.4` (radians, about 11.5 -> 23 degrees: half the magnification)
- `fLookSensitivity` `0.1` -> `0.3`

## Uncertain

- Read as a multiplier on look speed, `0.1` -> `0.3` makes turning faster, also relative to the
  wider view (about 1.5 times per screen width), which is the opposite of the published line. What
  the engine does with `fLookSensitivity` is not traced.
