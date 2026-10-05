---
title: No mouse speed cap
kind: component
bundle: fixes
status: located
systems: [input]
match:
  - "config/inputactionmapcommon.xml/*#MouseFilter@maxOutput"
exclude: []
requires: []
existing: mods/UFCP — src/fixes/mouse_speed_cap.cpp
verified: diff
---

# No mouse speed cap

A fast mouse flick turns the view as far as a slow one: the per-frame output cap that made quick
moves undershoot is gone.

## How

`config/inputactionmapcommon.xml`: the `MouseFilter` (`input="mouse:move"`) of six action maps loses
its `maxOutput="10"` attribute: `common_look`, `common_in_vehicle`, `common_using_mounted_weapon`,
`common_scry`, `common_free_camera` and `common_spectator_camera`. `sensitivity` and the aim curve
are unchanged.

`legacy/scubrahs-patch` `mouse-deceleration` lifts the same cap by raising the value to 999999;
UFCP fixes it in code.

## Uncertain

- That a missing `maxOutput` means no cap (rather than some default) is inferred; the filter's
  default is not traced.
