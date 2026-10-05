---
title: No cap on mouse turn speed
kind: component
bundle: fixes
claims: []
status: located
systems: [input]
match:
  - "config/inputactionmapcommon.xml/*.xml#MouseFilter@maxOutput"
exclude: []
requires: []
existing: mods/UFCP — src/fixes/mouse_speed_cap.cpp
verified: diff
---

# No cap on mouse turn speed

Fast mouse movements turn the view as far as the hand moves instead of being clipped.

## How

`config/inputactionmapcommon.xml`: the `MouseFilter` element of six action maps (`common_look`,
`common_in_vehicle`, `common_using_mounted_weapon`, `common_scry`, `common_free_camera`,
`common_spectator_camera`) loses its `maxOutput="10"` attribute.

## Depends on

Nothing. Realism Plus removes the identical six attributes
([`input-mouse-filter`](../../realism-plus/features/input-mouse-filter.md)).

## Uncertain

- That `maxOutput` caps the filtered mouse delta per frame is read from the name; not traced. Not a
  line of the readme.
