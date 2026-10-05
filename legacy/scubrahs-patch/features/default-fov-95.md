---
title: Default field of view 95
kind: component
bundle: gameplay
claims:
  - "Increased default FOV to 95"
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/cameras/**#**/fFOV"
exclude: []
requires: []
existing: mods/UFCP — src/options/fov.cpp (Field of view option, at run time)
verified: diff
---

# Default field of view 95

The on-foot first-person camera opens at 95 degrees instead of 75.

## How

`generated/entitylibrarypatchoverride.fcb` gains a copy of `cameras.Camera.First`, which the base
game keeps only in the world libraries; the override library wins, so this copy is the one used.
Against `world1`/`world2` it changes two values of `CCameraPawnComponent`:

- `fFOV` `75` -> `95` (degrees) - this feature
- `fFarDistance` `1000` -> `10000` - the camera's far distance, which is draw distance rather than
  field of view

The copy is a whole unit, so both values travel together.

## Uncertain

- The far-distance change belongs in spirit to "Improved Graphics" (draw distance). It cannot be
  split off this unit; a draw-distance page that wants it needs this one.
- `fov-changer-script` rewrites this same `fFOV` value inside the installed `patch.dat`.
